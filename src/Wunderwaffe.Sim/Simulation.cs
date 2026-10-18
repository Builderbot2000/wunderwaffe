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
    PowerWorld? _power;

    /// <summary>Power networks over every tile (FB-005, FB-020), rebuilt from the layout; derived, so not hashed.</summary>
    internal PowerWorld Power => _power ??= new PowerWorld(this);

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

    /// <summary>Squads and factory rally settings (UC-002, UC-006).</summary>
    public Squads Squads { get; } = new();

    /// <summary>
    /// A whole match: the world from <paramref name="setup"/>, with each of its players (ids 0 up) settled on their
    /// starting tile. Player 0 starts in the middle. With a <see cref="GameSetup.Scenario"/>, the world and every seat's
    /// starting state come from the scenario instead (WM-017), plus any head starts (MP-008).
    /// </summary>
    public static Simulation NewGame(ContentDb content, GameSetup setup)
    {
        if (setup.Problem(content) is { } problem)
            throw new ArgumentOutOfRangeException(nameof(setup), problem);
        var sim = new Simulation(content) { Setup = setup, RateModeEnabled = true };
        if (setup.Scenario is { } scenario)
        {
            sim.LoadScenario(content.Scenarios[scenario]);
            for (int p = 0; p < setup.Players; p++)
                sim.GiveHeadStart(sim.Tiles[sim.StartTileIds[p]], p);
            return sim;
        }
        sim.GenerateWorld(setup.Width, setup.Height, setup.Seed, setup.Players, richnessPercent: setup.RichnessPercent);
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

    /// <summary>One tick: queued commands, claim timers, then each tile's entities, then units, then goals.</summary>
    public void Step()
    {
        MaybeSnapshot();
        foreach (var command in _pending)
        {
            _log.Add(new LoggedCommand(Tick, command));
            command.Apply(this);
        }
        _pending.Clear();

        Expansion.Step(this);
        foreach (var tile in _tiles)
        {
            if (FullSim(tile))
                TileEntities.Step(this, tile);
        }
        Combat.Step(this);
        UnitFactory.Step(this);
        UnitUpkeep.Step(this);
        UnitPhysics.Step(this);
        RateMode.Step(this);
        Progression.Step(this);
        Victory.Step(this);
        Alerts.Step(this);

        Tick++;
    }

    /// <summary>Full-state hash, for desync detection and determinism tests (see <see cref="Core.StateHash"/>).</summary>
    public ulong StateHash() => Core.StateHash.Of(this);

    // ---- Command handlers. Each re-runs the same read-only check the UI previews with, then applies. ----

    internal PlaceResult TryPlace(TileState tile, int player, string buildingId, int x, int y, int rotation, string? recipeId)
    {
        var result = Placement.Check(this, tile, player, buildingId, x, y, rotation, recipeId, out var plan);
        if (result != PlaceResult.Ok)
            return result;

        Funds.TryPay(tile, player, plan.Def.Cost);
        if (plan.Def.Layer != null)
        {
            tile.Grid.Lay(plan.Def, plan.X, plan.Y);
            return PlaceResult.Ok;
        }
        var recipe = plan.RecipeId == null ? null : Content.Recipes[plan.RecipeId];
        tile.Grid.Add(plan.Def, player, plan.X, plan.Y, plan.Width, plan.Height, plan.Rotation, recipe);
        return PlaceResult.Ok;
    }

    /// <summary>
    /// Removes a building of the player's and refunds it (FB-002), or, on a cell with no building, the topmost layer
    /// building there (cable, frame, platform: FB-022), if nothing stands on it that needs it.
    /// A player's last core stays (WM-016). The refund and whatever it held go to the player's nearest core on the tile
    /// (<see cref="Funds.Refund(TileState, int, int, int, int, int, IReadOnlyDictionary{string, int}, int)"/>), or are lost.
    /// </summary>
    internal bool TryRemove(TileState tile, int player, int x, int y)
    {
        var grid = tile.Grid;
        if (grid.BuildingAt(x, y) is not { } b)
            return grid.WreckAt(x, y) != null ? Combat.ClearWreck(this, tile, player, x, y) : TryLift(tile, player, TopLayer(grid, x, y), x, y);
        if (b.Owner != player || (b.Kind == BuildingKind.Core && CoreCount(player) <= 1))
            return false;

        grid.Remove(b);
        var held = new Inventory();
        TileEntities.Drain(b, held);
        Funds.Refund(tile, player, b.X, b.Y, b.Width, b.Height, held.Entries);
        Funds.Refund(tile, player, b.X, b.Y, b.Width, b.Height, Content.Buildings[b.DefId].Cost, Balance.RefundPercent);
        return true;
    }

    /// <summary>The topmost building layer with something at a cell, or -1.</summary>
    static int TopLayer(TileGrid grid, int x, int y)
    {
        for (int l = grid.Layers.Count - 1; l >= 0; l--)
        {
            if (!grid.Layers[l].HoldsItems && grid.Has(l, x, y))
                return l;
        }
        return -1;
    }

    /// <summary>
    /// Takes up the entry of a building layer at a cell and refunds it (FB-022). Layer buildings are ownerless: anyone
    /// with presence may, unless something standing on it needs it (<see cref="Placement.CanLift"/>).
    /// </summary>
    internal bool TryLift(TileState tile, int player, int layer, int x, int y)
    {
        var grid = tile.Grid;
        if (grid.LaidAt(layer, x, y) is not { } def || !Territory.HasPresence(this, player, tile) || !Placement.CanLift(Content, grid, layer, x, y))
            return false;
        grid.SetEntry(layer, x, y, 0);
        Funds.Refund(tile, player, x, y, 1, 1, def.Cost, Balance.RefundPercent);
        return true;
    }

    /// <summary>Cores <paramref name="player"/> has, standing on any tile or carried by a unit (WM-016): every unit has one.</summary>
    public int CoreCount(int player) =>
        _tiles.Sum(t => t.Grid.Cores.Count(b => b.Owner == player)) + _units.Count(u => u.Owner == player);
}
