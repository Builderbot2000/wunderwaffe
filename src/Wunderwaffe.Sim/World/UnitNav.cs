using System.Runtime.CompilerServices;
using static Wunderwaffe.Sim.Core.IntMath;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// Where a unit's centre can stand, given its locomotion and size, cut into regions: connected areas inside one tile.
/// Two regions are linked when they touch across a tile border. Terrain never changes, so this is worked out once per
/// (class, clearance) and cached; it is derived from the map, so it isn't hashed.
/// </summary>
internal sealed class NavRegions(int[] region, int[] regionTile, int[][] links)
{
    /// <summary>Region of each world cell, or -1 where the unit's centre can't stand.</summary>
    public int[] Region { get; } = region;

    /// <summary>The tile each region lies in.</summary>
    public int[] RegionTile { get; } = regionTile;

    /// <summary>Linked regions in neighbouring tiles, ascending.</summary>
    public int[][] Links { get; } = links;
}

/// <summary>
/// An abstract trip (UC-009): the tiles to cross, the waypoints of the path (world cells, straightened, ending at the
/// goal), the length in sub-cells along paths around terrain and buildings, and the ticks it takes at the unit's
/// speed (-1 if it can't move).
/// </summary>
public sealed record TravelPlan(int[] Tiles, (int X, int Y)[] Waypoints, long Length, long Ticks);

/// <summary>
/// Tile-graph routing that obeys locomotion (UD-004, UC-005, UC-007, IO-003): a route is a chain of linked regions, so
/// it never relies on crossing terrain the unit can't, and big units only squeeze through gaps they fit. Also the
/// route-and-ETA function abstract travel uses (UC-009).
/// </summary>
public static class UnitNav
{
    const int S = Unit.CellScale;

    /// <summary>How far, in cells, from a start where the unit can't stand to look for ground it can.</summary>
    public const int LaunchReach = 8;

    /// <summary>Ticks added to an ETA for speeding up and braking.</summary>
    const int StartStopTicks = 3 * Unit.DragDiv;

    sealed class Cache
    {
        public (int W, int H, int Tiles) Key;
        public readonly Dictionary<(LocomotionClass, int), NavRegions> Grids = [];
    }

    static readonly ConditionalWeakTable<Simulation, Cache> Caches = new();

    internal static NavRegions Regions(Simulation sim, LocomotionClass cls, int clearance)
    {
        var cache = Caches.GetValue(sim, _ => new Cache());
        lock (cache)
        {
            var key = (sim.Map.Width, sim.Map.Height, sim.Tiles.Count);
            if (cache.Key != key)
            {
                cache.Key = key;
                cache.Grids.Clear();
            }
            if (!cache.Grids.TryGetValue((cls, clearance), out var g))
                cache.Grids[(cls, clearance)] = g = Build(sim, cls, clearance);
            return g;
        }
    }

    static NavRegions Build(Simulation sim, LocomotionClass cls, int clearance)
    {
        var map = sim.Map;
        int w = map.Width, h = map.Height;
        var crossable = sim.Content.TerrainList.Select(t => Locomotion.CanCross(cls, t)).ToArray();
        var ok = new bool[w * h];
        var tileOf = new int[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                tileOf[i] = map.TileAtCell(x, y);
                ok[i] = tileOf[i] >= 0 && crossable[map.TerrainAt(x, y)];
            }
        }
        if (clearance > 0)
            ok = Erode(ok, w, h, clearance);

        // Regions: 4-connected flood fill inside each tile, numbered in cell order.
        var region = new int[w * h];
        Array.Fill(region, -1);
        var regionTile = new List<int>();
        var stack = new Stack<int>();
        for (int i = 0; i < w * h; i++)
        {
            if (!ok[i] || region[i] >= 0)
                continue;
            int id = regionTile.Count, tile = tileOf[i];
            regionTile.Add(tile);
            region[i] = id;
            stack.Push(i);
            while (stack.Count > 0)
            {
                int c = stack.Pop(), cx = c % w, cy = c / w;
                Visit(cx > 0, c - 1);
                Visit(cx < w - 1, c + 1);
                Visit(cy > 0, c - w);
                Visit(cy < h - 1, c + w);
            }

            void Visit(bool inside, int n)
            {
                if (inside && ok[n] && region[n] < 0 && tileOf[n] == tile)
                {
                    region[n] = id;
                    stack.Push(n);
                }
            }
        }

        var links = new SortedSet<int>?[regionTile.Count];
        void Link(int a, int b)
        {
            if (a < 0 || b < 0 || regionTile[a] == regionTile[b])
                return;
            (links[a] ??= []).Add(b);
            (links[b] ??= []).Add(a);
        }
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (region[i] < 0)
                    continue;
                if (x + 1 < w)
                    Link(region[i], region[i + 1]);
                if (y + 1 < h)
                    Link(region[i], region[i + w]);
            }
        }
        return new NavRegions(region, [.. regionTile], links.Select(l => l?.ToArray() ?? []).ToArray());
    }

    /// <summary>Keeps only cells with no blocked cell within <paramref name="r"/> (a square); cells off the map don't count.</summary>
    static bool[] Erode(bool[] ok, int w, int h, int r)
    {
        var rows = new bool[w * h];
        var bad = new int[Math.Max(w, h) + 1];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
                bad[x + 1] = bad[x] + (ok[y * w + x] ? 0 : 1);
            for (int x = 0; x < w; x++)
                rows[y * w + x] = bad[Math.Min(w, x + r + 1)] == bad[Math.Max(0, x - r)];
        }
        var result = new bool[w * h];
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
                bad[y + 1] = bad[y] + (rows[y * w + x] ? 0 : 1);
            for (int y = 0; y < h; y++)
                result[y * w + x] = bad[Math.Min(h, y + r + 1)] == bad[Math.Max(0, y - r)];
        }
        return result;
    }

    /// <summary>The region at a world cell, or the nearest one within <paramref name="reach"/> cells; -1 if none.</summary>
    internal static int RegionNear(NavRegions g, WorldMap map, int gx, int gy, int reach)
    {
        if (map.InBounds(gx, gy) && g.Region[gy * map.Width + gx] is int here and >= 0)
            return here;
        for (int r = 1; r <= reach; r++)
        {
            int best = -1;
            long bestD = long.MaxValue;
            for (int y = gy - r; y <= gy + r; y++)
            {
                for (int x = gx - r; x <= gx + r; x++)
                {
                    if (Math.Max(Math.Abs(x - gx), Math.Abs(y - gy)) != r || !map.InBounds(x, y) || g.Region[y * map.Width + x] < 0)
                        continue;
                    long d = (long)(x - gx) * (x - gx) + (long)(y - gy) * (y - gy);
                    if (d < bestD)
                        (best, bestD) = (g.Region[y * map.Width + x], d);
                }
            }
            if (best >= 0)
                return best;
        }
        return -1;
    }

    /// <summary>
    /// A route from world cell <paramref name="from"/> to a cell of <paramref name="destTile"/>: the tiles to cross, in
    /// order, and the region entered in each (index 0 is the start region). <paramref name="goal"/> moves to the nearest
    /// cell of the tile the unit can reach if it can't reach the one asked for. False if it can reach no part of the tile.
    /// </summary>
    internal static bool FindRoute(Simulation sim, LocomotionClass cls, int clearance, (int X, int Y) from, TileState destTile,
        ref (int X, int Y) goal, out int[] tiles, out int[] regions)
    {
        tiles = regions = [];
        var g = Regions(sim, cls, clearance);
        var map = sim.Map;
        int start = RegionNear(g, map, from.X, from.Y, LaunchReach);
        if (start < 0)
            return false;

        var parent = new int[g.RegionTile.Length];
        Array.Fill(parent, -2);
        parent[start] = -1;
        var queue = new Queue<int>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            int r = queue.Dequeue();
            foreach (int n in g.Links[r])
            {
                if (parent[n] != -2)
                    continue;
                parent[n] = r;
                queue.Enqueue(n);
            }
        }

        int target = map.InBounds(goal.X, goal.Y) ? g.Region[goal.Y * map.Width + goal.X] : -1;
        if (target < 0 || parent[target] == -2 || g.RegionTile[target] != destTile.Id)
        {
            // The nearest cell of the tile in a region the unit can reach.
            target = -1;
            long bestD = long.MaxValue;
            (int X, int Y) best = goal;
            for (int y = 0; y < destTile.Grid.Height; y++)
            {
                for (int x = 0; x < destTile.Grid.Width; x++)
                {
                    int gx = destTile.OriginX + x, gy = destTile.OriginY + y;
                    int r = g.Region[gy * map.Width + gx];
                    if (r < 0 || parent[r] == -2 || g.RegionTile[r] != destTile.Id)
                        continue;
                    long d = (long)(gx - goal.X) * (gx - goal.X) + (long)(gy - goal.Y) * (gy - goal.Y);
                    if (d < bestD)
                        (target, bestD, best) = (r, d, (gx, gy));
                }
            }
            if (target < 0)
                return false;
            goal = best;
        }

        var chain = new List<int>();
        for (int r = target; r >= 0; r = parent[r])
            chain.Add(r);
        chain.Reverse();

        // If the start region is in another tile (the unit stands just off ground it can use), cross into it first.
        int here = map.TileAtCell(from.X, from.Y);
        if (g.RegionTile[start] != here)
            chain.Insert(0, -1);
        regions = [.. chain];
        tiles = chain.Skip(1).Select(r => g.RegionTile[r]).ToArray();
        return true;
    }

    /// <summary>
    /// The cell of <paramref name="next"/>, on its border with <paramref name="tile"/>, where a unit leaving region
    /// <paramref name="have"/> enters region <paramref name="want"/>, chosen for the shortest trip from (fromX, fromY)
    /// to (toX, toY) in sub-cells.
    /// </summary>
    internal static (int X, int Y) Portal(Simulation sim, NavRegions g, TileState tile, TileState next, int have, int want,
        long fromX, long fromY, long toX, long toY)
    {
        var map = sim.Map;
        int w = map.Width;
        var cells = map.BorderCells(next.Id, tile.Id);
        (int X, int Y) best = (next.CenterX, next.CenterY);
        long bestD = long.MaxValue;
        // Strictest first: a cell of the wanted region next to the region the unit is in; then any of the wanted region.
        for (int pass = 0; pass < 3 && bestD == long.MaxValue; pass++)
        {
            foreach (var (cx, cy) in cells)
            {
                int i = cy * w + cx;
                if (pass < 2 && g.Region[i] != want)
                    continue;
                if (pass == 0 && have >= 0 && !Touches(cx, cy))
                    continue;
                long mx = (long)cx * S + S / 2, my = (long)cy * S + S / 2;
                long a = ISqrt((mx - fromX) * (mx - fromX) + (my - fromY) * (my - fromY));
                long b = ISqrt((toX - mx) * (toX - mx) + (toY - my) * (toY - my));
                if (a + b < bestD)
                    (best, bestD) = ((cx, cy), a + b);
            }
        }
        return best;

        bool Touches(int cx, int cy) =>
            (cx > 0 && g.Region[cy * w + cx - 1] == have) || (cx + 1 < w && g.Region[cy * w + cx + 1] == have)
            || (cy > 0 && g.Region[(cy - 1) * w + cx] == have) || (cy + 1 < map.Height && g.Region[(cy + 1) * w + cx] == have);
    }

    /// <summary>
    /// The route and travel time for a unit of the given class, clearance (cells; see <see cref="UnitPath.Clearance"/>)
    /// and top speed (sub-cells per tick) from world sub-cell (fromX, fromY) to cell (cellX, cellY) of
    /// <paramref name="destTileId"/>, without simulating it (UC-009). Null if the unit can't get there.
    /// </summary>
    public static TravelPlan? Plan(Simulation sim, LocomotionClass cls, int clearance, int maxSpeed, long fromX, long fromY,
        int destTileId, int cellX, int cellY)
    {
        if (sim.TryGetTile(destTileId) is not { } dest || !dest.Grid.InBounds(cellX, cellY))
            return null;
        var goal = dest.ToGlobal(cellX, cellY);
        var start = ((int)FloorDiv(fromX, S), (int)FloorDiv(fromY, S));
        if (sim.TileAtCell(start.Item1, start.Item2) is not { } tile
            || !FindRoute(sim, cls, clearance, start, dest, ref goal, out var tiles, out var regions))
            return null;

        var g = Regions(sim, cls, clearance);
        long gx = (long)goal.X * S + S / 2, gy = (long)goal.Y * S + S / 2;
        var points = new List<(int X, int Y)>();
        long length = 0, x = fromX, y = fromY;
        for (int i = 0; i < tiles.Length; i++)
        {
            var next = sim.Tiles[tiles[i]];
            var p = Portal(sim, g, tile, next, regions[i], regions[i + 1], x, y, gx, gy);
            length += Leg(sim, cls, clearance, tile, next, x, y, p, points);
            (x, y, tile) = ((long)p.X * S + S / 2, (long)p.Y * S + S / 2, next);
        }
        length += Leg(sim, cls, clearance, tile, null, x, y, goal, points);
        long ticks = maxSpeed <= 0 ? -1 : length / maxSpeed + StartStopTicks;
        return new TravelPlan(tiles, [.. points], length, ticks);
    }

    /// <summary><see cref="Plan(Simulation, LocomotionClass, int, int, long, long, int, int, int)"/> for a unit as it is now.</summary>
    public static TravelPlan? Plan(Simulation sim, Unit u, int destTileId, int cellX, int cellY) =>
        Plan(sim, u.Locomotion, UnitPath.Clearance(u), u.MaxSpeed, u.X, u.Y, destTileId, cellX, cellY);

    /// <summary>
    /// Length in sub-cells of the path the unit would plan across <paramref name="tile"/> (and next) to a cell; adds its
    /// waypoints to <paramref name="points"/>.
    /// </summary>
    static long Leg(Simulation sim, LocomotionClass cls, int clearance, TileState tile, TileState? next, long x, long y, (int X, int Y) to,
        List<(int X, int Y)> points)
    {
        var win = UnitPath.Window(tile, next);
        var blocked = UnitPath.Blocked(sim, win, tile, next, clearance, cls);
        var s = (Math.Clamp((int)FloorDiv(x, S), win.X, win.X + win.W - 1) - win.X, Math.Clamp((int)FloorDiv(y, S), win.Y, win.Y + win.H - 1) - win.Y);
        var path = UnitPath.Find(win.W, win.H, blocked, s, (Math.Clamp(to.X, win.X, win.X + win.W - 1) - win.X, Math.Clamp(to.Y, win.Y, win.Y + win.H - 1) - win.Y));
        long total = 0;
        foreach (var (px, py) in path ?? [(to.X - win.X, to.Y - win.Y)])
        {
            points.Add((px + win.X, py + win.Y));
            long tx = (long)(px + win.X) * S + S / 2, ty = (long)(py + win.Y) * S + S / 2;
            total += ISqrt((tx - x) * (tx - x) + (ty - y) * (ty - y));
            (x, y) = (tx, ty);
        }
        return total;
    }
}
