using System.Text;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using Wunderwaffe.Sim.World.Gen;

namespace Wunderwaffe.Sim;

/// <summary>
/// The authoritative game state, advanced in fixed ticks. Integer-only so every machine reaches the
/// same state from the same commands (NF-004). Godot reads from this; it never writes except via commands.
/// </summary>
public sealed class Simulation(ContentDb content)
{
    public const int TicksPerSecond = 60;

    /// <summary>
    /// Nominal tile size in cells: hand-built tiles are this square, and generated tiles average about this area.
    /// Placeholder until OQ-2 (tile size) is decided.
    /// </summary>
    public const int DefaultTileSize = 96;

    /// <summary>A generated starting tile has at least this share of buildable ground, in percent.</summary>
    const int StartLandPercent = 60;

    /// <summary>A neighbour counts as land, for the start and for regional deposits, with this share of buildable ground.</summary>
    const int LandPercent = 30;

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
    WorldMap _map = new(0, 0);
    /// <summary>Replaced with a seeded one by <see cref="GenerateWorld"/>; hand-built tiles use this default.</summary>
    TileNames _names = new(0);
    readonly List<Command> _pending = [];
    readonly List<Unit> _units = [];
    readonly Dictionary<int, Unit> _unitsById = [];
    readonly List<PlayerState> _players = [];
    readonly List<LoggedCommand> _log = [];
    int _nextUnitId = 1;

    public ContentDb Content { get; } = content;
    public long Tick { get; private set; }
    public IReadOnlyList<TileState> Tiles => _tiles;

    /// <summary>The global cell map every tile is cut from. Units move in its coordinates.</summary>
    public WorldMap Map => _map;

    /// <summary>The tile <see cref="GenerateWorld"/> picked for the first player, or -1.</summary>
    public int StartTileId { get; private set; } = -1;

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
        sim.GenerateWorld(setup.Width, setup.Height, setup.Seed);
        sim.SettleStartingTile(sim.Tiles[sim.StartTileId], player);
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

    /// <summary>The tile covering a world cell, or null.</summary>
    public TileState? TileAtCell(int gx, int gy) => _map.TileAtCell(gx, gy) is int id and >= 0 ? _tiles[id] : null;

    /// <summary>
    /// Adds a hand-built square tile, for tests and debugging: <paramref name="size"/> cells a side at world cell
    /// (x * size, y * size), on the default terrain. Neighbours and port zones are recomputed for every tile.
    /// </summary>
    public TileState AddTile(int x, int y, int owner = TileState.Unowned, int size = DefaultTileSize)
    {
        if (x < 0 || y < 0)
            throw new ArgumentOutOfRangeException(nameof(x), "hand-built tiles go at non-negative coordinates");
        int ox = x * size, oy = y * size;
        _map.Grow(ox + size, oy + size, (byte)Content.DefaultTerrain);
        for (int cy = oy; cy < oy + size; cy++)
        {
            for (int cx = ox; cx < ox + size; cx++)
            {
                if (_map.TileAtCell(cx, cy) >= 0)
                    throw new InvalidOperationException($"a tile already exists at ({x},{y})");
            }
        }
        for (int cy = oy; cy < oy + size; cy++)
        {
            for (int cx = ox; cx < ox + size; cx++)
                _map.SetTile(cx, cy, _tiles.Count);
        }

        var tile = CreateTile(ox, oy, size, size);
        tile.Owner = owner;
        if (owner != TileState.Unowned)
            Player(owner);
        RebuildTopology();
        return tile;
    }

    /// <summary>A tile for the cells of the map already marked with the next id, inside the given box.</summary>
    TileState CreateTile(int ox, int oy, int w, int h)
    {
        int id = _tiles.Count;
        var mask = new bool[w * h];
        var buildable = new bool[w * h];
        var terrain = new byte[w * h];
        var counts = new int[Content.TerrainList.Count];
        long sx = 0, sy = 0, area = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x, t = _map.TerrainAt(ox + x, oy + y);
                terrain[i] = (byte)t;
                if (_map.TileAtCell(ox + x, oy + y) != id)
                    continue;
                mask[i] = true;
                buildable[i] = Content.TerrainList[t].Buildable;
                counts[t]++;
                sx += x;
                sy += y;
                area++;
            }
        }

        var tile = new TileState(id, ox, oy, new TileGrid(w, h, mask, buildable, terrain))
        {
            CenterX = ox + (int)(sx / Math.Max(1, area)),
            CenterY = oy + (int)(sy / Math.Max(1, area)),
            Biome = Content.TerrainList[Array.IndexOf(counts, counts.Max())].Id,
            Name = _names.Next(),
        };
        _tiles.Add(tile);
        return tile;
    }

    /// <summary>Recomputes which tiles neighbour each other and every tile's port zones.</summary>
    void RebuildTopology()
    {
        _map.RebuildAdjacency(_tiles.Count);
        foreach (var t in _tiles)
            t.Neighbors = _map.NeighborsOf(t.Id);
        // Each tile's zones depend only on the map, so tiles can be done in parallel.
        var zones = new int[_tiles.Count][];
        Parallel.For(0, _tiles.Count, i =>
        {
            var t = _tiles[i];
            zones[i] = Zones.Compute(_map, t.Id, t.OriginX, t.OriginY, t.Grid.Width, t.Grid.Height);
        });
        foreach (var t in _tiles)
            t.Grid.SetZones(zones[t.Id]);
    }

    /// <summary>Tiles sharing a border with <paramref name="tile"/> (WM-003), in id order.</summary>
    public IEnumerable<TileState> Neighbors(TileState tile)
    {
        foreach (int id in tile.Neighbors)
            yield return _tiles[id];
    }

    /// <summary>
    /// Generates the world (WM-001, WM-002, WM-009): terrain over (width x height) nominal tiles, cut into irregular
    /// tiles of varying size and neighbour count, each with deposits that follow its terrain. Picks
    /// <see cref="StartTileId"/>. Regional items appear on <see cref="RegionalChancePercent"/> of tiles, never on the
    /// start, and always on at least one of its land neighbours (RS-003).
    /// </summary>
    public void GenerateWorld(int width, int height, ulong seed, int tileSize = DefaultTileSize)
    {
        if (_tiles.Count > 0)
            throw new InvalidOperationException("the world already has tiles");
        int w = width * tileSize, h = height * tileSize;
        var (terrain, elevation) = TerrainGen.Generate(Content, w, h, tileSize, seed);
        var tileOf = Partition.Generate(Content, terrain, elevation, w, h, tileSize, seed);
        _map = new WorldMap(w, h, terrain, elevation, tileOf);
        _names = new TileNames(seed);

        int count = tileOf.Max() + 1;
        var box = Enumerable.Repeat((X0: int.MaxValue, Y0: int.MaxValue, X1: -1, Y1: -1), count).ToArray();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                ref var b = ref box[tileOf[y * w + x]];
                b = (Math.Min(b.X0, x), Math.Min(b.Y0, y), Math.Max(b.X1, x), Math.Max(b.Y1, y));
            }
        }
        foreach (var b in box)
            CreateTile(b.X0, b.Y0, b.X1 - b.X0 + 1, b.Y1 - b.Y0 + 1);
        RebuildTopology();

        StartTileId = ChooseStart(w, h);
        var startTile = _tiles[StartTileId];
        var missing = _tiles.ToDictionary(t => t.Id, _ => new HashSet<string>(StringComparer.Ordinal));
        var rng = new Rng(seed ^ 0xD1B54A32D192ED03UL);
        foreach (var item in RegionalItems())
        {
            foreach (var t in _tiles)
            {
                if (t == startTile || rng.Range(0, 100) >= RegionalChancePercent)
                    missing[t.Id].Add(item);
            }
            var near = Neighbors(startTile).Where(n => IsLand(n, LandPercent)).ToList();
            if (near.Count == 0)
                near = Neighbors(startTile).ToList();
            if (near.Count > 0 && near.All(n => missing[n.Id].Contains(item)))
                missing[near[rng.Range(0, near.Count)].Id].Remove(item);
        }

        foreach (var t in _tiles)
            TileGenerator.GenerateDeposits(t.Grid, Content, new Rng(seed ^ (ulong)(t.Id + 1) * 0x9E3779B97F4A7C15UL), missing[t.Id]);
    }

    static bool IsLand(TileState t, int percent) => t.Grid.BuildableCount * 100L >= (long)t.Grid.Area * percent;

    /// <summary>
    /// The land tile nearest the middle of the world with room for a core and at least two land neighbours to expand
    /// into; failing that, the tile with most buildable ground.
    /// </summary>
    int ChooseStart(int w, int h)
    {
        var fits = _tiles.Where(t => CoreSpot(t) != null).ToList();
        if (fits.Count == 0)
            throw new InvalidOperationException("no tile has room for a core");
        var good = fits.Where(t => IsLand(t, StartLandPercent) && Neighbors(t).Count(n => IsLand(n, LandPercent)) >= 2).ToList();
        if (good.Count == 0)
            return fits.MaxBy(t => t.Grid.BuildableCount)!.Id;
        return good
            .OrderBy(t => (long)(t.CenterX - w / 2) * (t.CenterX - w / 2) + (long)(t.CenterY - h / 2) * (t.CenterY - h / 2))
            .ThenBy(t => t.Id)
            .First().Id;
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

    BuildingDef CoreDef => Content.Buildings.TryGetValue(CoreBuildingId, out var core)
        ? core
        : throw new InvalidOperationException($"content has no '{CoreBuildingId}' building");

    /// <summary>
    /// Where a core fits nearest the tile's centre: on free buildable ground, searched in growing squares. Null if the
    /// tile has no room, as an all-water tile might not.
    /// </summary>
    (int X, int Y)? CoreSpot(TileState tile)
    {
        var core = CoreDef;
        var grid = tile.Grid;
        if (grid.BuildableCount < core.Width * core.Height)
            return null;
        int sx = tile.CenterX - tile.OriginX - core.Width / 2, sy = tile.CenterY - tile.OriginY - core.Height / 2;
        int reach = Math.Max(grid.Width, grid.Height);
        for (int r = 0; r <= reach; r++)
        {
            (int X, int Y)? best = null;
            long bestD = long.MaxValue;
            void Try(int x, int y)
            {
                if (!grid.InBounds(x, y, core.Width, core.Height) || !grid.Buildable(x, y, core.Width, core.Height)
                    || !grid.IsFree(x, y, core.Width, core.Height))
                    return;
                long d = (long)(x - sx) * (x - sx) + (long)(y - sy) * (y - sy);
                if (d < bestD || (d == bestD && (y < best!.Value.Y || (y == best.Value.Y && x < best.Value.X))))
                    (best, bestD) = ((x, y), d);
            }
            // Only the ring at distance r; the inside was searched already.
            for (int x = sx - r; x <= sx + r; x++)
            {
                Try(x, sy - r);
                if (r > 0)
                    Try(x, sy + r);
            }
            for (int y = sy - r + 1; y <= sy + r - 1; y++)
            {
                Try(sx - r, y);
                Try(sx + r, y);
            }
            if (best != null)
                return best;
        }
        return null;
    }

    /// <summary>Puts a core on the tile if there is room for one. Returns false if there isn't.</summary>
    bool PlaceCore(TileState tile)
    {
        var core = CoreDef;
        if (CoreSpot(tile) is not { } spot)
            return false;
        tile.Grid.Add(core, spot.X, spot.Y, core.Width, core.Height, 0, null);
        return true;
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
            Mix(t.OriginX);
            Mix(t.OriginY);
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
