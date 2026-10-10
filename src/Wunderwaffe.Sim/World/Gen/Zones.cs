namespace Wunderwaffe.Sim.World.Gen;

/// <summary>
/// Phase three: each tile's I/O zones (IO-001). Every cell of the tile within <see cref="PortRules.ZoneDepth"/> steps
/// of its border with a neighbour belongs to that neighbour's zone. A breadth-first search from all border cells at
/// once gives each cell to the neighbour that reaches it first (the lower id on ties), so there is exactly one zone per
/// neighbour, on the side facing it, and zones never overlap.
/// </summary>
internal static class Zones
{
    /// <summary>The neighbour each cell of the tile's bounding box is a port zone for, or -1.</summary>
    public static int[] Compute(WorldMap map, int tileId, int originX, int originY, int width, int height)
    {
        var zone = new int[width * height];
        Array.Fill(zone, -1);
        var neighbors = map.NeighborsOf(tileId);
        if (neighbors.Count == 0)
            return zone;

        bool Mine(int x, int y) => x >= 0 && y >= 0 && x < width && y < height && map.TileAtCell(originX + x, originY + y) == tileId;
        (int, int)[] steps = [(1, 0), (-1, 0), (0, 1), (0, -1)];

        var frontier = new List<int>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!Mine(x, y))
                    continue;
                int best = -1;
                foreach (var (dx, dy) in steps)
                {
                    int t = map.TileAtCell(originX + x + dx, originY + y + dy);
                    if (t >= 0 && t != tileId && (best < 0 || t < best) && Contains(neighbors, t))
                        best = t;
                }
                if (best < 0)
                    continue;
                zone[y * width + x] = best;
                frontier.Add(y * width + x);
            }
        }

        for (int depth = 1; depth < PortRules.ZoneDepth; depth++)
        {
            var next = new List<int>();
            var claim = new Dictionary<int, int>();
            foreach (int c in frontier)
            {
                int cx = c % width, cy = c / width;
                foreach (var (dx, dy) in steps)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (!Mine(nx, ny) || zone[ny * width + nx] >= 0)
                        continue;
                    int i = ny * width + nx;
                    if (!claim.TryGetValue(i, out int had))
                    {
                        claim[i] = zone[c];
                        next.Add(i);
                    }
                    else if (zone[c] < had)
                    {
                        claim[i] = zone[c];
                    }
                }
            }
            foreach (int i in next)
                zone[i] = claim[i];
            frontier = next;
        }
        return zone;
    }

    static bool Contains(IReadOnlyList<int> sorted, int v)
    {
        foreach (int x in sorted)
        {
            if (x == v)
                return true;
        }
        return false;
    }
}
