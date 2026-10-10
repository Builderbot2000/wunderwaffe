using System.Text;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim;

/// <summary>
/// The authoritative game state, advanced in fixed ticks. Integer-only so every machine reaches the
/// same state from the same commands (NF-004). Godot reads from this; it never writes except via commands.
/// </summary>
public sealed class Simulation(ContentDb content)
{
    public const int TicksPerSecond = 60;

    /// <summary>Local map size in cells. Placeholder until OQ-2 (tile size) is decided.</summary>
    public const int DefaultTileSize = 96;

    /// <summary>Share of a building's cost returned on removal (FB-002).</summary>
    public const int RefundPercent = 100;

    public const string CoreBuildingId = "core";

    /// <summary>What a new player's core starts with (WM-005).</summary>
    public static readonly IReadOnlyDictionary<string, int> StartingItems = new SortedDictionary<string, int>(StringComparer.Ordinal)
    {
        ["copper_ore"] = 300,
        ["iron_ore"] = 150,
    };

    /// <summary>Paid from a neighbouring tile the claimer owns to claim a tile (WM-006). Placeholder until OQ-3 is decided.</summary>
    public static readonly IReadOnlyDictionary<string, int> ClaimCost = new SortedDictionary<string, int>(StringComparer.Ordinal)
    {
        ["copper_ore"] = 100,
        ["iron_ore"] = 50,
    };

    /// <summary>What a newly claimed tile's core starts with: a few drills, a belt and an export port.</summary>
    public static readonly IReadOnlyDictionary<string, int> ClaimedTileItems = new SortedDictionary<string, int>(StringComparer.Ordinal)
    {
        ["copper_ore"] = 120,
    };

    /// <summary>Chance, in percent, that a tile other than a starting tile has a given regional deposit (RS-003).</summary>
    public const int RegionalChancePercent = 35;

    readonly List<TileState> _tiles = [];
    readonly Dictionary<(int X, int Y), TileState> _byCoord = [];
    readonly List<Command> _pending = [];
    readonly List<Unit> _units = [];
    readonly Dictionary<int, Unit> _unitsById = [];
    readonly List<PlayerState> _players = [];
    readonly List<LoggedCommand> _log = [];
    int _nextUnitId = 1;

    public ContentDb Content { get; } = content;
    public long Tick { get; private set; }
    public IReadOnlyList<TileState> Tiles => _tiles;

    /// <summary>What the match was built from, if it came from <see cref="NewGame"/>. Saves need it.</summary>
    public GameSetup? Setup { get; private set; }

    /// <summary>
    /// Every command applied so far, with the tick it applied on. Replaying it from <see cref="Setup"/> rebuilds this
    /// state (NF-002).
    /// </summary>
    public IReadOnlyList<LoggedCommand> CommandLog => _log;

    /// <summary>Players in id order. A player exists once they own a tile or something of theirs produces.</summary>
    public IReadOnlyList<PlayerState> Players => _players;

    public PlayerState? PlayerOrNull(int id) => id >= 0 && id < _players.Count ? _players[id] : null;

    internal PlayerState Player(int id)
    {
        if (id < 0)
            throw new ArgumentOutOfRangeException(nameof(id), "not a player");
        while (_players.Count <= id)
            _players.Add(new PlayerState(_players.Count));
        return _players[id];
    }

    /// <summary>A whole match: the world from <paramref name="setup"/>, with <paramref name="player"/> settled in the middle.</summary>
    public static Simulation NewGame(ContentDb content, GameSetup setup, int player = 0)
    {
        var sim = new Simulation(content) { Setup = setup };
        var start = setup.StartTile;
        sim.GenerateWorld(setup.Width, setup.Height, setup.Seed, start: start);
        sim.SettleStartingTile(sim.TileAt(start.X, start.Y)!, player);
        return sim;
    }

    /// <summary>
    /// Setup and debug: completes a tech node outside its condition. During a match use <see cref="DebugUnlockCommand"/>.
    /// </summary>
    public void GrantTech(int player, string techId)
    {
        if (!Content.Tech.ContainsKey(techId))
            throw new ArgumentException($"unknown tech '{techId}'", nameof(techId));
        Player(player).Unlock(techId, Tick);
    }

    /// <summary>All units, in id order.</summary>
    public IReadOnlyList<Unit> Units => _units;

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

    /// <summary>
    /// Maps a cell that may lie past the edge of <paramref name="tile"/> onto the tile it falls in, walking across
    /// neighbours. Null if it falls outside the world.
    /// </summary>
    public (TileState Tile, int X, int Y)? ResolveCell(TileState tile, int x, int y)
    {
        while (true)
        {
            var side = x < 0 ? Direction.West : x >= tile.Grid.Width ? Direction.East
                : y < 0 ? Direction.North : y >= tile.Grid.Height ? Direction.South : (Direction?)null;
            if (side is not { } s)
                return (tile, x, y);
            if (Neighbor(tile, s) is not { } next)
                return null;
            switch (s)
            {
                case Direction.West: x += next.Grid.Width; break;
                case Direction.East: x -= tile.Grid.Width; break;
                case Direction.North: y += next.Grid.Height; break;
                default: y -= tile.Grid.Height; break;
            }
            tile = next;
        }
    }

    public TileState AddTile(int x, int y, int owner = TileState.Unowned, int size = DefaultTileSize)
    {
        if (_byCoord.ContainsKey((x, y)))
            throw new InvalidOperationException($"a tile already exists at ({x},{y})");
        var tile = new TileState(_tiles.Count, x, y, size, size) { Owner = owner };
        if (owner != TileState.Unowned)
            Player(owner);
        _tiles.Add(tile);
        _byCoord.Add((x, y), tile);
        return tile;
    }

    public TileState? TileAt(int x, int y) => _byCoord.GetValueOrDefault((x, y));

    /// <summary>The tile across the <paramref name="side"/> edge, or null at the edge of the world (WM-003).</summary>
    public TileState? Neighbor(TileState tile, Direction side) => TileAt(tile.X + side.Dx(), tile.Y + side.Dy());

    /// <summary>
    /// Tiles sharing an edge with <paramref name="tile"/> on a square grid (OQ-1), in <see cref="Direction"/> order.
    /// </summary>
    public IEnumerable<TileState> Neighbors(TileState tile)
    {
        foreach (var side in Directions.All)
        {
            if (Neighbor(tile, side) is { } n)
                yield return n;
        }
    }

    /// <summary>
    /// Fills a width x height world with unowned tiles, each with its own seeded deposits (WM-001, WM-002).
    /// Tile ids run row by row. Regional items appear on <see cref="RegionalChancePercent"/> of tiles, never on
    /// <paramref name="start"/>, and always on at least one of its neighbours (RS-003).
    /// </summary>
    public void GenerateWorld(int width, int height, ulong seed, int tileSize = DefaultTileSize, (int X, int Y)? start = null)
    {
        var tiles = new List<TileState>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                tiles.Add(AddTile(x, y, size: tileSize));
        }

        var startTile = start is { } s ? TileAt(s.X, s.Y) : null;
        var missing = tiles.ToDictionary(t => t.Id, _ => new HashSet<string>(StringComparer.Ordinal));
        var rng = new Rng(seed ^ 0xD1B54A32D192ED03UL);
        foreach (var item in RegionalItems())
        {
            foreach (var t in tiles)
            {
                if (t == startTile || rng.Range(0, 100) >= RegionalChancePercent)
                    missing[t.Id].Add(item);
            }
            if (startTile == null)
                continue;
            var near = Neighbors(startTile).ToList();
            if (near.Count > 0 && near.All(n => missing[n.Id].Contains(item)))
                missing[near[rng.Range(0, near.Count)].Id].Remove(item);
        }

        foreach (var t in tiles)
            TileGenerator.GenerateDeposits(t.Grid, Content, new Rng(seed ^ (ulong)(t.Id + 1) * 0x9E3779B97F4A7C15UL), missing[t.Id]);
    }

    IEnumerable<string> RegionalItems() =>
        Content.Items.Values.Where(i => i.Regional).Select(i => i.Id).Order(StringComparer.Ordinal);

    /// <summary>Makes an existing unowned tile a player's first tile: a core and starting items (WM-005).</summary>
    public void SettleStartingTile(TileState tile, int owner)
    {
        if (tile.Owner != TileState.Unowned)
            throw new InvalidOperationException($"tile {tile.Id} is already owned");
        tile.Owner = owner;
        Player(owner);
        PlaceCore(tile);
        foreach (var (id, n) in StartingItems)
            tile.Inventory.Add(id, n);
    }

    /// <summary>
    /// Adds a standalone first tile: seeded deposits, a core in the middle and starting items (WM-005).
    /// </summary>
    public TileState AddStartingTile(int x, int y, int owner, ulong seed)
    {
        var tile = AddTile(x, y, owner);
        Player(owner);
        TileGenerator.GenerateDeposits(tile.Grid, Content, new Rng(seed), RegionalItems().ToHashSet());
        PlaceCore(tile);
        foreach (var (id, n) in StartingItems)
            tile.Inventory.Add(id, n);
        return tile;
    }

    void PlaceCore(TileState tile)
    {
        if (!Content.Buildings.TryGetValue(CoreBuildingId, out var core))
            throw new InvalidOperationException($"content has no '{CoreBuildingId}' building");
        int cx = (tile.Grid.Width - core.Width) / 2;
        int cy = (tile.Grid.Height - core.Height) / 2;
        tile.Grid.Add(core, cx, cy, core.Width, core.Height, 0, null);
    }

    public TileState? TryGetTile(int id) => id >= 0 && id < _tiles.Count ? _tiles[id] : null;

    /// <summary>Queues a command to apply at the start of the next <see cref="Step"/>.</summary>
    public void Enqueue(Command command) => _pending.Add(command);

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
        UnitPhysics.Step(this);
        Progression.Step(this);

        Tick++;
    }

    /// <summary>Claims an adjacent unclaimed tile, paying <see cref="ClaimCost"/> from a neighbouring tile (WM-006).</summary>
    internal ClaimResult TryClaim(int player, int tileId)
    {
        var result = Expansion.Check(this, player, tileId, out var source);
        if (result != ClaimResult.Ok)
            return result;

        var tile = _tiles[tileId];
        source!.Inventory.TryTake(ClaimCost);
        tile.Owner = player;
        Player(player);
        PlaceCore(tile);
        foreach (var (id, n) in ClaimedTileItems)
            tile.Inventory.Add(id, n);
        return ClaimResult.Ok;
    }

    internal PlaceResult TryPlace(TileState tile, int player, string buildingId, int x, int y, int rotation, string? recipeId)
    {
        var result = Placement.Check(this, tile, player, buildingId, x, y, rotation, recipeId, out var plan);
        if (result != PlaceResult.Ok)
            return result;

        tile.Inventory.TryTake(plan.Def.Cost);
        var recipe = plan.RecipeId == null ? null : Content.Recipes[plan.RecipeId];
        tile.Grid.Add(plan.Def, plan.X, plan.Y, plan.Width, plan.Height, plan.Rotation, recipe);
        return PlaceResult.Ok;
    }

    internal bool TryRemove(TileState tile, int player, int x, int y)
    {
        if (tile.Owner != player || tile.Grid.BuildingAt(x, y) is not { } b || b.DefId == CoreBuildingId)
            return false;

        tile.Grid.Remove(b);
        TileEntities.Drain(tile, b, Content);
        foreach (var (id, n) in Content.Buildings[b.DefId].Cost)
            tile.Inventory.Add(id, (long)n * RefundPercent / 100);
        return true;
    }

    /// <summary>FNV-1a hash of the full state, for desync detection and determinism tests.</summary>
    public ulong StateHash()
    {
        ulong h = 14695981039346656037UL;
        void Mix(long v)
        {
            for (int i = 0; i < 8; i++)
            {
                h ^= (byte)(v >> (i * 8));
                h *= 1099511628211UL;
            }
        }
        void MixStr(string? s)
        {
            if (s == null)
            {
                Mix(-1);
                return;
            }
            foreach (byte b in Encoding.UTF8.GetBytes(s))
            {
                h ^= b;
                h *= 1099511628211UL;
            }
            Mix(s.Length);
        }

        void MixItems(SortedDictionary<string, int> items)
        {
            foreach (var (id, n) in items)
            {
                MixStr(id);
                Mix(n);
            }
        }
        void MixEntity(PlacedBuilding b)
        {
            if (b.Belt is { } belt)
            {
                Mix(belt.Count);
                for (int i = 0; i < belt.Count; i++)
                {
                    MixStr(belt.Items[i]);
                    Mix(belt.Positions[i]);
                }
            }
            if (b.Machine is { } m)
            {
                Mix(m.Working ? m.Progress + 1 : 0);
                Mix(m.IdleTicks);
                Mix(m.DumpWait);
                Mix(m.DumpCursor);
                MixItems(m.Input);
                MixItems(m.Output);
            }
            if (b.Export is { } link)
            {
                Mix(link.Cursor);
                foreach (var s in link.InFlight)
                {
                    Mix(s.ArriveTick);
                    MixStr(s.ItemId);
                }
            }
        }

        Mix(Tick);
        foreach (var t in _tiles)
        {
            Mix(t.Id);
            Mix(t.X);
            Mix(t.Y);
            Mix(t.Owner);
            Mix(t.Contested ? 1 : 0);
            Mix(t.Grid.FrameCount);
            for (int y = 0; t.Grid.FrameCount > 0 && y < t.Grid.Height; y++)
            {
                for (int x = 0; x < t.Grid.Width; x++)
                {
                    if (t.Grid.HasFrame(x, y))
                        Mix(y * t.Grid.Width + x);
                }
            }
            foreach (var (id, n) in t.Inventory.Entries)
            {
                MixStr(id);
                Mix(n);
            }
            foreach (var b in t.Grid.Buildings)
            {
                Mix(b.Id);
                MixStr(b.DefId);
                Mix(b.X);
                Mix(b.Y);
                Mix(b.Rotation);
                MixStr(b.RecipeId);
                MixEntity(b);
            }
        }
        foreach (var p in _players)
        {
            Mix(p.Id);
            foreach (var (id, n) in p.Produced)
            {
                MixStr(id);
                Mix(n);
            }
            Mix(p.Shipped);
            Mix(p.Launched);
            foreach (var u in p.UnlockLog)
            {
                Mix(u.Tick);
                MixStr(u.TechId);
            }
            foreach (long t in p.GoalTicks)
                Mix(t);
        }
        foreach (var u in _units)
        {
            Mix(u.Id);
            Mix(u.Owner);
            Mix(u.TileId);
            Mix(u.X);
            Mix(u.Y);
            Mix(u.VX);
            Mix(u.VY);
            Mix(u.Angle);
            Mix(u.AngVel);
            Mix(u.PathIndex);
            foreach (var blk in u.Blocks)
            {
                MixStr(blk.DefId);
                Mix(blk.GX);
                Mix(blk.GY);
                Mix(blk.Rotation);
            }
            if (u.Route is { } route)
            {
                Mix(u.RouteIndex);
                Mix(u.DestTileId);
                Mix(u.DestX);
                Mix(u.DestY);
                foreach (int id in route)
                    Mix(id);
            }
            else
            {
                Mix(-1);
            }
        }
        return h;
    }
}
