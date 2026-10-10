using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using Wunderwaffe.Sim.World.Gen;

namespace Wunderwaffe.Sim;

/// <summary>
/// The authoritative game state, advanced in fixed ticks. Integer-only so every machine reaches the
/// same state from the same commands (NF-004). Godot reads from this; it never writes except via commands.
/// World building and setup (tiles, generation, starting tiles) is in <c>Simulation.World.cs</c>.
/// </summary>
public sealed partial class Simulation(ContentDb content)
{
    public const int TicksPerSecond = 60;

    public const string CoreBuildingId = "core";

    readonly List<TileState> _tiles = [];
    WorldMap _map = new(0, 0);
    /// <summary>Replaced with a seeded one by <see cref="GenerateWorld"/>; hand-built tiles use this default.</summary>
    TileNames _names = new(0);
    readonly List<Command> _pending = [];
    readonly List<Unit> _units = [];
    readonly Dictionary<int, Unit> _unitsById = [];
    readonly List<PlayerState> _players = [];
    readonly List<LoggedCommand> _log = [];
    readonly List<int> _startTileIds = [];
    int _nextUnitId = 1;

    public ContentDb Content { get; } = content;
    public long Tick { get; private set; }
    public IReadOnlyList<TileState> Tiles => _tiles;

    /// <summary>The global cell map every tile is cut from. Units move in its coordinates.</summary>
    public WorldMap Map => _map;

    /// <summary>The tile <see cref="GenerateWorld"/> picked for the first player, or -1.</summary>
    public int StartTileId => _startTileIds.Count > 0 ? _startTileIds[0] : -1;

    /// <summary>The starting tile <see cref="GenerateWorld"/> picked for each player, indexed by player id.</summary>
    public IReadOnlyList<int> StartTileIds => _startTileIds;

    /// <summary>What the match was built from, if it came from <see cref="NewGame"/>. Saves need it.</summary>
    public GameSetup? Setup { get; private set; }

    /// <summary>
    /// Every command applied so far, with the tick it applied on. Replaying it from <see cref="Setup"/> rebuilds this
    /// state (NF-002).
    /// </summary>
    public IReadOnlyList<LoggedCommand> CommandLog => _log;

    /// <summary>Players in id order. A player exists once they own a tile or something of theirs produces.</summary>
    public IReadOnlyList<PlayerState> Players => _players;

    /// <summary>All units, in id order.</summary>
    public IReadOnlyList<Unit> Units => _units;

    /// <summary>
    /// A whole match: the world from <paramref name="setup"/>, with each of its players (ids 0 up) settled on their
    /// starting tile. Player 0 starts in the middle.
    /// </summary>
    public static Simulation NewGame(ContentDb content, GameSetup setup)
    {
        if (setup.Players is < 1 or > GameSetup.MaxPlayers)
            throw new ArgumentOutOfRangeException(nameof(setup), $"{setup.Players} players; 1 to {GameSetup.MaxPlayers} allowed");
        var sim = new Simulation(content) { Setup = setup };
        sim.GenerateWorld(setup.Width, setup.Height, setup.Seed, setup.Players);
        for (int p = 0; p < setup.Players; p++)
            sim.SettleStartingTile(sim.Tiles[sim.StartTileIds[p]], p);
        return sim;
    }

    public PlayerState? PlayerOrNull(int id) => id >= 0 && id < _players.Count ? _players[id] : null;

    /// <summary>The player's state, created on first use.</summary>
    internal PlayerState Player(int id)
    {
        while (_players.Count <= id)
            _players.Add(new PlayerState(_players.Count));
        return _players[id];
    }

    public TileState? TryGetTile(int id) => id >= 0 && id < _tiles.Count ? _tiles[id] : null;

    /// <summary>The tile covering a world cell, or null.</summary>
    public TileState? TileAtCell(int gx, int gy) => _map.TileAtCell(gx, gy) is int id and >= 0 ? _tiles[id] : null;

    /// <summary>
    /// Maps a cell in <paramref name="tile"/>'s local coordinates, possibly outside the tile, onto the tile it falls in.
    /// Null if no tile covers it.
    /// </summary>
    public (TileState Tile, int X, int Y)? ResolveCell(TileState tile, int x, int y)
    {
        var (gx, gy) = tile.ToGlobal(x, y);
        if (TileAtCell(gx, gy) is not { } t)
            return null;
        return (t, gx - t.OriginX, gy - t.OriginY);
    }

    /// <summary>Tiles sharing a border with <paramref name="tile"/> (WM-003), in id order.</summary>
    public IEnumerable<TileState> Neighbors(TileState tile)
    {
        foreach (int id in tile.Neighbors)
            yield return _tiles[id];
    }

    public Unit? UnitById(int id) => _unitsById.GetValueOrDefault(id);

    internal Unit SpawnUnit(int owner, int tileId, int x, int y, IReadOnlyList<UnitBlock> blocks,
        List<(int GX, int GY, int Mass)> cells, int coreGX, int coreGY)
    {
        var unit = new Unit(_nextUnitId++, owner, tileId, x, y, blocks, cells, Content.Buildings, coreGX, coreGY);
        _units.Add(unit);
        _unitsById.Add(unit.Id, unit);
        return unit;
    }

    internal void RemoveUnit(Unit unit)
    {
        _units.Remove(unit);
        _unitsById.Remove(unit.Id);
    }

    /// <summary>Queues a command to apply at the start of the next <see cref="Step"/>.</summary>
    public void Enqueue(Command command) => _pending.Add(command);

    /// <summary>One tick: queued commands, then each tile's entities, then units, then goals.</summary>
    public void Step()
    {
        foreach (var command in _pending)
        {
            _log.Add(new LoggedCommand(Tick, command));
            command.Apply(this);
        }
        _pending.Clear();

        foreach (var tile in _tiles)
            TileEntities.Step(this, tile);
        UnitFactory.Step(this);
        UnitUpkeep.Step(this);
        UnitPhysics.Step(this);
        Progression.Step(this);

        Tick++;
    }

    /// <summary>Full-state hash, for desync detection and determinism tests (see <see cref="Core.StateHash"/>).</summary>
    public ulong StateHash() => Core.StateHash.Of(this);

    // ---- Command handlers. Each re-runs the same read-only check the UI previews with, then applies. ----

    /// <summary>Claims an adjacent unclaimed tile, paying <see cref="Balance.ClaimCost"/> from a neighbouring tile (WM-006).</summary>
    internal ClaimResult TryClaim(int player, int tileId)
    {
        var result = Expansion.Check(this, player, tileId, out var source);
        if (result != ClaimResult.Ok)
            return result;

        var tile = _tiles[tileId];
        source!.Inventory.TryTake(Balance.ClaimCost);
        tile.Owner = player;
        Player(player);
        PlaceCore(tile);
        tile.Inventory.Add(Balance.ClaimedTileItems);
        return ClaimResult.Ok;
    }

    internal PlaceResult TryPlace(TileState tile, int player, string buildingId, int x, int y, int rotation, string? recipeId)
    {
        var result = Placement.Check(this, tile, player, buildingId, x, y, rotation, recipeId, out var plan);
        if (result != PlaceResult.Ok)
            return result;

        tile.Inventory.TryTake(plan.Def.Cost);
        var recipe = plan.RecipeId == null ? null : Content.Recipes[plan.RecipeId];
        tile.Grid.Add(plan.Def, player, plan.X, plan.Y, plan.Width, plan.Height, plan.Rotation, recipe);
        return PlaceResult.Ok;
    }

    internal bool TryRemove(TileState tile, int player, int x, int y)
    {
        if (tile.Owner != player || tile.Grid.BuildingAt(x, y) is not { } b || b.DefId == CoreBuildingId)
            return false;

        tile.Grid.Remove(b);
        TileEntities.Drain(tile, b);
        foreach (var (id, n) in Content.Buildings[b.DefId].Cost)
            tile.Inventory.Add(id, (long)n * Balance.RefundPercent / 100);
        return true;
    }
}
