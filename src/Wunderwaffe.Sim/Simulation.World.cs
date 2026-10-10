using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using Wunderwaffe.Sim.World.Gen;

namespace Wunderwaffe.Sim;

/// <summary>Building the world: generated or hand-built tiles, topology, starting tiles and cores.</summary>
public sealed partial class Simulation
{
    /// <summary>
    /// Nominal tile size in cells: hand-built tiles are this square, and generated tiles average about this area.
    /// Placeholder until OQ-2 (tile size) is decided.
    /// </summary>
    public const int DefaultTileSize = 96;

    /// <summary>
    /// Generates the world (WM-001, WM-002, WM-009): terrain over (width x height) nominal tiles, cut into irregular
    /// tiles of varying size and neighbour count, each with deposits that follow its terrain. Picks
    /// <see cref="StartTileId"/>. Regional items appear on <see cref="Balance.RegionalChancePercent"/> of tiles, never on
    /// the start, and always on at least one of its land neighbours (RS-003).
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
        var missing = RegionalExclusions(_tiles[StartTileId], seed);
        foreach (var t in _tiles)
            TileGenerator.GenerateDeposits(t.Grid, Content, new Rng(seed ^ (ulong)(t.Id + 1) * 0x9E3779B97F4A7C15UL), missing[t.Id]);
    }

    /// <summary>
    /// Per tile, the regional items it gets no deposits of (RS-003): all of them on the start, and each one on
    /// <c>100 - RegionalChancePercent</c>% of other tiles, but never on every land neighbour of the start.
    /// </summary>
    Dictionary<int, HashSet<string>> RegionalExclusions(TileState start, ulong seed)
    {
        var missing = _tiles.ToDictionary(t => t.Id, _ => new HashSet<string>(StringComparer.Ordinal));
        var rng = new Rng(seed ^ 0xD1B54A32D192ED03UL);
        foreach (var item in RegionalItems())
        {
            foreach (var t in _tiles)
            {
                if (t == start || rng.Range(0, 100) >= Balance.RegionalChancePercent)
                    missing[t.Id].Add(item);
            }
            var near = Neighbors(start).Where(n => IsLand(n, Balance.LandPercent)).ToList();
            if (near.Count == 0)
                near = Neighbors(start).ToList();
            if (near.Count > 0 && near.All(n => missing[n.Id].Contains(item)))
                missing[near[rng.Range(0, near.Count)].Id].Remove(item);
        }
        return missing;
    }

    IEnumerable<string> RegionalItems() =>
        Content.Items.Values.Where(i => i.Regional).Select(i => i.Id).Order(StringComparer.Ordinal);

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
        var good = fits.Where(t => IsLand(t, Balance.StartLandPercent) && Neighbors(t).Count(n => IsLand(n, Balance.LandPercent)) >= 2).ToList();
        if (good.Count == 0)
            return fits.MaxBy(t => t.Grid.BuildableCount)!.Id;
        return good
            .OrderBy(t => (long)(t.CenterX - w / 2) * (t.CenterX - w / 2) + (long)(t.CenterY - h / 2) * (t.CenterY - h / 2))
            .ThenBy(t => t.Id)
            .First().Id;
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

    /// <summary>Adds a standalone hand-built first tile: seeded deposits, a core in the middle and starting items (WM-005).</summary>
    public TileState AddStartingTile(int x, int y, int owner, ulong seed)
    {
        var tile = AddTile(x, y);
        TileGenerator.GenerateDeposits(tile.Grid, Content, new Rng(seed), RegionalItems().ToHashSet());
        SettleStartingTile(tile, owner);
        return tile;
    }

    /// <summary>Makes an existing unowned tile a player's first tile: a core and starting items (WM-005).</summary>
    public void SettleStartingTile(TileState tile, int owner)
    {
        if (tile.Owner != TileState.Unowned)
            throw new InvalidOperationException($"tile {tile.Id} is already owned");
        tile.Owner = owner;
        Player(owner);
        PlaceCore(tile);
        tile.Inventory.Add(Balance.StartingItems);
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

    BuildingDef CoreDef => Content.Buildings[CoreBuildingId];

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

    /// <summary>Puts a core on the tile, if there is room for one.</summary>
    void PlaceCore(TileState tile)
    {
        if (CoreSpot(tile) is { } spot)
            tile.Grid.Add(CoreDef, spot.X, spot.Y, CoreDef.Width, CoreDef.Height, 0, null);
    }
}
