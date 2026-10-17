using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using Wunderwaffe.Sim.World.Gen;

namespace Wunderwaffe.Sim;

/// <summary>Building the world: generated or hand-built tiles, topology, starting tiles and cores.</summary>
public sealed partial class Simulation
{
    /// <summary>
    /// Nominal tile size in cells (WM-001, FB-001, OQ-35): hand-built tiles are this square, and generated tiles average
    /// about this area. 256 puts a tile on the scale of a mid-size Mindustry map.
    /// </summary>
    public const int DefaultTileSize = 256;

    /// <summary>
    /// Generates the world (WM-001, WM-002, WM-009): terrain over (width x height) nominal tiles, cut into irregular
    /// tiles of varying size and neighbour count, each with deposits that follow its terrain. Picks a start for each of
    /// <paramref name="players"/> players (<see cref="StartTileIds"/>). Regional items appear on
    /// <see cref="Balance.RegionalChancePercent"/> of tiles, never on a start, and always on at least one land neighbour
    /// of each start (RS-003).
    /// </summary>
    public void GenerateWorld(int width, int height, ulong seed, int players = 1, int tileSize = DefaultTileSize, int richnessPercent = 100)
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

        _startTileIds.AddRange(ChooseStarts(w, h, players, seed, richnessPercent));
        var missing = RegionalExclusions(_startTileIds.Select(id => _tiles[id]).ToList(), seed);
        foreach (var t in _tiles)
            TileGenerator.GenerateDeposits(t.Grid, Content, DepositRng(seed, t.Id), missing[t.Id], richnessPercent);

        // Victory circles go on the deposits (WM-014); tiles are independent, so in parallel.
        var circles = new (int X, int Y)[_tiles.Count];
        Parallel.For(0, _tiles.Count, i => circles[i] = Circles.Place(_tiles[i].Grid));
        foreach (var t in _tiles)
            (t.CircleX, t.CircleY) = circles[t.Id];
    }

    static Rng DepositRng(ulong seed, int tileId) => new(seed ^ (ulong)(tileId + 1) * 0x9E3779B97F4A7C15UL);

    /// <summary>
    /// Per tile, the regional items it gets no deposits of (RS-003): all of them on the starts, and each one on
    /// <c>100 - RegionalChancePercent</c>% of other tiles, but never on every land neighbour of a start.
    /// </summary>
    Dictionary<int, HashSet<string>> RegionalExclusions(List<TileState> starts, ulong seed)
    {
        var missing = _tiles.ToDictionary(t => t.Id, _ => new HashSet<string>(StringComparer.Ordinal));
        var rng = new Rng(seed ^ 0xD1B54A32D192ED03UL);
        foreach (var item in RegionalItems())
        {
            foreach (var t in _tiles)
            {
                if (starts.Contains(t) || rng.Range(0, 100) >= Balance.RegionalChancePercent)
                    missing[t.Id].Add(item);
            }
            foreach (var start in starts)
            {
                var near = Neighbors(start).Where(n => IsLand(n, Balance.LandPercent) && !starts.Contains(n)).ToList();
                if (near.Count == 0)
                    near = Neighbors(start).Where(n => !starts.Contains(n)).ToList();
                if (near.Count > 0 && near.All(n => missing[n.Id].Contains(item)))
                    missing[near[rng.Range(0, near.Count)].Id].Remove(item);
            }
        }
        return missing;
    }

    IEnumerable<string> RegionalItems() =>
        Content.Items.Values.Where(i => i.Regional).Select(i => i.Id).Order(StringComparer.Ordinal);

    static bool IsLand(TileState t, int percent) => t.Grid.BuildableCount * 100L >= (long)t.Grid.Area * percent;

    /// <summary>
    /// Starting tiles for <paramref name="players"/> players (WM-005, OQ-22). Candidates are good starts (land with room
    /// for a core and at least two land neighbours to expand into), or any tile with room for a core when there aren't
    /// enough. Each of <see cref="Balance.StartAttempts"/> seeded attempts seeds one start (the first attempt: the
    /// candidate nearest the middle of the world; the rest: a random one) and adds the rest by farthest-point sampling
    /// over tile-graph steps (ties to the lowest id). The kept set has the most starts with a land neighbour to carry
    /// their regional deposits (within <see cref="Balance.RegionalHops"/>), then the most even starting deposits, then
    /// the widest spacing; earlier attempts win ties. One player just gets the middle start.
    /// </summary>
    List<int> ChooseStarts(int w, int h, int players, ulong seed, int richnessPercent)
    {
        var fits = _tiles.Where(t => StartSpot(t) != null).ToList();
        if (fits.Count < players)
            throw new InvalidOperationException($"only {fits.Count} tiles have room for a core, {players} players need one each");
        var good = fits.Where(t => IsLand(t, Balance.StartLandPercent) && Neighbors(t).Count(n => IsLand(n, Balance.LandPercent)) >= 2).ToList();
        var pool = good.Count >= players ? good : fits;
        int middle = good.Count == 0
            ? fits.MaxBy(t => t.Grid.BuildableCount)!.Id
            : good
                .OrderBy(t => (long)(t.CenterX - w / 2) * (t.CenterX - w / 2) + (long)(t.CenterY - h / 2) * (t.CenterY - h / 2))
                .ThenBy(t => t.Id)
                .First().Id;
        if (players == 1)
            return [middle];
        if (!pool.Any(t => t.Id == middle))
            middle = pool[0].Id;

        var steps = new Dictionary<int, int[]>();
        int[] From(int id) => steps.TryGetValue(id, out var s) ? s : steps[id] = TileSteps([id]);
        var deposits = new Dictionary<int, SortedDictionary<string, int>>();
        var regional = RegionalItems().ToHashSet();
        var minable = Content.Recipes.Values.Where(r => r.Inputs.Count == 0).SelectMany(r => r.Outputs.Keys)
            .Where(i => !regional.Contains(i)).Distinct().Order(StringComparer.Ordinal).ToList();
        // A start gets every regional item excluded, so its deposits depend only on its id: lay them on a blank copy.
        SortedDictionary<string, int> StartDeposits(TileState t)
        {
            if (!deposits.TryGetValue(t.Id, out var d))
            {
                var grid = t.Grid.Blank();
                TileGenerator.GenerateDeposits(grid, Content, DepositRng(seed, t.Id), regional, richnessPercent);
                d = deposits[t.Id] = grid.DepositCounts();
            }
            return d;
        }

        var rng = new Rng(seed ^ 0x5EED5EED5EEDUL);
        List<int>? best = null;
        (int Regional, long Even, int Spread) bestScore = default;
        for (int a = 0; a < Balance.StartAttempts; a++)
        {
            var starts = new List<int> { a == 0 ? middle : pool[rng.Range(0, pool.Count)].Id };
            while (starts.Count < players)
            {
                starts.Add(pool
                    .Where(t => !starts.Contains(t.Id))
                    .OrderByDescending(t => starts.Min(s => From(s)[t.Id]))
                    .ThenBy(t => t.Id)
                    .First().Id);
            }

            int regionalOk = starts.Count(s => Neighbors(_tiles[s]).Any(n => !starts.Contains(n.Id) && IsLand(n, Balance.LandPercent)));
            long even = 0;
            foreach (var item in minable)
            {
                var counts = starts.Select(s => StartDeposits(_tiles[s]).GetValueOrDefault(item)).ToList();
                even += counts.Max() == 0 ? 1000 : counts.Min() * 1000L / counts.Max();
            }
            int spread = int.MaxValue;
            for (int i = 0; i < starts.Count; i++)
            {
                for (int j = i + 1; j < starts.Count; j++)
                    spread = Math.Min(spread, From(starts[i])[starts[j]]);
            }
            var score = (regionalOk, even, spread);
            if (best == null || score.CompareTo(bestScore) > 0)
                (best, bestScore) = (starts, score);
        }
        return best!;
    }

    /// <summary>Fewest border crossings from any of <paramref name="from"/> to each tile; int.MaxValue if unreachable.</summary>
    int[] TileSteps(List<int> from)
    {
        var steps = Enumerable.Repeat(int.MaxValue, _tiles.Count).ToArray();
        var queue = new Queue<int>();
        foreach (int id in from)
        {
            steps[id] = 0;
            queue.Enqueue(id);
        }
        while (queue.Count > 0)
        {
            int id = queue.Dequeue();
            foreach (int n in _tiles[id].Neighbors)
            {
                if (steps[n] != int.MaxValue)
                    continue;
                steps[n] = steps[id] + 1;
                queue.Enqueue(n);
            }
        }
        return steps;
    }

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

    /// <summary>
    /// Puts a core straight onto a hand-built tile, unpaid and with no checks, for tests and benchmark scenes. Real
    /// play builds cores like any building.
    /// </summary>
    public PlacedBuilding AddCore(TileState tile, int owner, int x, int y, string defId = CoreBuildingId)
    {
        var def = Content.Buildings[defId];
        return tile.Grid.Add(def, owner, x, y, def.Width, def.Height, 0, null);
    }

    /// <summary>Adds a standalone hand-built first tile: seeded deposits and a landed starting unit in the middle (WM-005).</summary>
    public TileState AddStartingTile(int x, int y, int owner, ulong seed)
    {
        var tile = AddTile(x, y);
        TileGenerator.GenerateDeposits(tile.Grid, Content, new Rng(seed), RegionalItems().ToHashSet());
        SettleStartingTile(tile, owner);
        return tile;
    }

    /// <summary>
    /// Makes an existing unowned tile a player's first tile (WM-005): the player owns it and has a landed starting unit
    /// (see <see cref="PlaceStartingUnit"/>) whose core stands in its victory circle and holds the starting items.
    /// </summary>
    public void SettleStartingTile(TileState tile, int owner)
    {
        if (tile.Owner != TileState.Unowned)
            throw new InvalidOperationException($"tile {tile.Id} is already owned");
        tile.Owner = owner;
        Player(owner);
        if (StartSpot(tile) is { } spot)
            PlaceStartingUnit(tile, owner, spot.X, spot.Y);
    }

    /// <summary>The starting unit's frame: a thruster, a 2x2 core and a thruster, two rows deep.</summary>
    const int StartWidth = 4, StartHeight = 2;

    /// <summary>
    /// The player's first core (WM-005, WM-016), landed as a scout: a thruster, a core and a thruster in a row on frame,
    /// all pushing east, with the top-left at (x, y). The core holds <see cref="Balance.StartingItems"/>. Building or
    /// landing needs presence on a tile (WM-015), so expansion starts with a unit flying to a neighbour's victory circle
    /// and landing there; its core's stock pays for building there (FB-019). Kept in code until prefabs take it over.
    /// </summary>
    void PlaceStartingUnit(TileState tile, int owner, int x, int y)
    {
        for (int cy = y; cy < y + StartHeight; cy++)
        {
            for (int cx = x; cx < x + StartWidth; cx++)
                tile.Grid.SetFrame(cx, cy, true);
        }
        var thruster = Content.Buildings["thruster"];
        tile.Grid.Add(thruster, owner, x, y, 1, 1, 0, null);
        var core = tile.Grid.Add(CoreDef, owner, x + 1, y, CoreDef.Width, CoreDef.Height, 0, null);
        tile.Grid.Add(thruster, owner, x + 1 + CoreDef.Width, y, 1, 1, 0, null);
        core.Stock!.Add(Balance.StartingItems);
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
        // Hand-built tiles keep their circle in the middle; generation moves it onto the deposits (WM-014).
        (tile.CircleX, tile.CircleY) = (tile.CenterX - ox, tile.CenterY - oy);
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

    BuildingDef CoreDef => Content.Buildings[CoreBuildingId];

    /// <summary>
    /// Where the starting unit fits nearest the tile's victory circle centre (WM-005: its core stands in the circle).
    /// Null if the tile has no room, as an all-water tile might not.
    /// </summary>
    (int X, int Y)? StartSpot(TileState tile)
    {
        if (tile.Grid.BuildableCount < StartWidth * StartHeight)
            return null;
        return FreeSpot(tile, StartWidth, StartHeight, tile.CircleX - StartWidth / 2, tile.CircleY - StartHeight / 2);
    }

    /// <summary>
    /// The top-left of a free, unframed w x h block of buildable cells nearest (sx, sy), searched in growing squares;
    /// null if none.
    /// </summary>
    static (int X, int Y)? FreeSpot(TileState tile, int w, int h, int sx, int sy)
    {
        var grid = tile.Grid;
        int reach = Math.Max(grid.Width, grid.Height);
        for (int r = 0; r <= reach; r++)
        {
            (int X, int Y)? best = null;
            long bestD = long.MaxValue;
            void Try(int x, int y)
            {
                if (!grid.InBounds(x, y, w, h) || !grid.Buildable(x, y, w, h) || !grid.IsFree(x, y, w, h) || Framed(grid, x, y, w, h))
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

    static bool Framed(TileGrid grid, int x, int y, int w, int h)
    {
        for (int cy = y; cy < y + h; cy++)
        {
            for (int cx = x; cx < x + w; cx++)
            {
                if (grid.HasFrame(cx, cy))
                    return true;
            }
        }
        return false;
    }
}
