using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim.World.Gen;

/// <summary>
/// Phase two of world generation: cuts the terrain into irregular tiles, like the territories of a RISK board.
/// <list type="number">
/// <item>Seeds by dart throwing: no two closer than a spacing that is wider at sea, so ocean tiles are larger.</item>
/// <item>Regions grow from the seeds by cheapest path (multi-source Dijkstra). Climbing, coastlines and rivers cost
/// extra, so borders settle along ridges, coasts and rivers.</item>
/// <item>Regions smaller than a quarter of a nominal tile merge into the neighbour they share most border with.</item>
/// <item>Ids are renumbered by centre, in rows a tile high, so they read naturally on the map.</item>
/// </list>
/// Every region is connected, because each cell is reached from a cell of its own region.
/// </summary>
internal static class Partition
{
    /// <summary>Spacing between seeds, in percent of the nominal tile size, on land and at sea.</summary>
    const int LandSpacing = 78, SeaSpacing = 115;

    const int AttemptsPerTile = 60;
    const int StepCost = 10, ClimbCost = 3, CoastCost = 60, RiverCost = 40;

    public static int[] Generate(ContentDb content, byte[] terrain, short[] elevation, int w, int h, int tileSize, ulong seed)
    {
        var water = new bool[terrain.Length];
        var river = new bool[terrain.Length];
        for (int i = 0; i < terrain.Length; i++)
        {
            var def = content.TerrainList[terrain[i]];
            water[i] = def.Water && !def.River;
            river[i] = def.River;
        }

        var seeds = Seeds(water, w, h, tileSize, seed);
        var tileOf = Grow(seeds, water, river, elevation, w, h);
        int count = MergeSmall(tileOf, seeds.Count, w, h, tileSize * tileSize / 4);
        return Renumber(tileOf, count, w, tileSize);
    }

    static List<(int X, int Y)> Seeds(bool[] water, int w, int h, int tileSize, ulong seed)
    {
        var rng = new Rng(seed ^ 0x3C6EF372FE94F82BUL);
        var seeds = new List<(int X, int Y, long R)>();
        long target = Math.Max(2, (long)w * h / ((long)tileSize * tileSize));
        long attempts = target * AttemptsPerTile;
        for (long a = 0; a < attempts; a++)
        {
            int x = rng.Range(0, w), y = rng.Range(0, h);
            long r = (long)tileSize * (water[y * w + x] ? SeaSpacing : LandSpacing) / 100;
            bool clear = true;
            foreach (var s in seeds)
            {
                long dx = s.X - x, dy = s.Y - y, m = Math.Max(r, s.R);
                if (dx * dx + dy * dy < m * m)
                {
                    clear = false;
                    break;
                }
            }
            if (clear)
                seeds.Add((x, y, r));
        }
        return seeds.Select(s => (s.X, s.Y)).ToList();
    }

    static int[] Grow(List<(int X, int Y)> seeds, bool[] water, bool[] river, short[] elevation, int w, int h)
    {
        int n = w * h;
        var tileOf = new int[n];
        var cost = new long[n];
        Array.Fill(tileOf, -1);
        Array.Fill(cost, long.MaxValue);
        var queue = new PriorityQueue<int, long>();
        for (int s = 0; s < seeds.Count; s++)
        {
            int i = seeds[s].Y * w + seeds[s].X;
            tileOf[i] = s;
            cost[i] = 0;
            queue.Enqueue(i, i);
        }

        Span<int> dx = [1, -1, 0, 0], dy = [0, 0, 1, -1];
        while (queue.TryDequeue(out int c, out long pri))
        {
            if (pri >> 32 != cost[c])
                continue; // stale: reached more cheaply since
            int cx = c % w, cy = c / w;
            for (int k = 0; k < 4; k++)
            {
                int nx = cx + dx[k], ny = cy + dy[k];
                if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                    continue;
                int i = ny * w + nx;
                long step = StepCost + ClimbCost * Math.Abs(elevation[i] - elevation[c])
                    + (water[i] != water[c] ? CoastCost : 0)
                    + (river[i] && !river[c] ? RiverCost : 0);
                long next = cost[c] + step;
                // Equal costs keep the first claim, which comes from the queue's fixed order: deterministic.
                if (next >= cost[i])
                    continue;
                cost[i] = next;
                tileOf[i] = tileOf[c];
                queue.Enqueue(i, (next << 32) | (uint)i);
            }
        }
        return tileOf;
    }

    /// <summary>Merges regions under <paramref name="minArea"/> cells, smallest first. Returns the id count (with gaps).</summary>
    static int MergeSmall(int[] tileOf, int count, int w, int h, int minArea)
    {
        var area = new int[count];
        foreach (int t in tileOf)
            area[t]++;
        var shared = new Dictionary<(int, int), int>();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x, a = tileOf[i];
                if (x + 1 < w && tileOf[i + 1] != a)
                    Count(a, tileOf[i + 1]);
                if (y + 1 < h && tileOf[i + w] != a)
                    Count(a, tileOf[i + w]);
            }
        }
        void Count(int a, int b)
        {
            shared[(a, b)] = shared.GetValueOrDefault((a, b)) + 1;
            shared[(b, a)] = shared.GetValueOrDefault((b, a)) + 1;
        }

        var into = Enumerable.Range(0, count).ToArray();
        int Find(int t)
        {
            while (into[t] != t)
                t = into[t] = into[into[t]];
            return t;
        }

        foreach (int t in Enumerable.Range(0, count).OrderBy(t => area[t]).ThenBy(t => t).ToList())
        {
            int root = Find(t);
            if (root != t || area[t] >= minArea)
                continue;
            // Border shared with each surviving region, through earlier merges.
            var border = new SortedDictionary<int, int>();
            foreach (var ((a, b), edges) in shared)
            {
                if (a == t && Find(b) is int other && other != t)
                    border[other] = border.GetValueOrDefault(other) + edges;
            }
            if (border.Count == 0)
                continue;
            int best = border.MaxBy(kv => kv.Value).Key;
            into[t] = best;
            area[best] += area[t];
        }

        for (int i = 0; i < tileOf.Length; i++)
            tileOf[i] = Find(tileOf[i]);
        return count;
    }

    static int[] Renumber(int[] tileOf, int count, int w, int tileSize)
    {
        var sx = new long[count];
        var sy = new long[count];
        var area = new long[count];
        for (int i = 0; i < tileOf.Length; i++)
        {
            int t = tileOf[i];
            sx[t] += i % w;
            sy[t] += i / w;
            area[t]++;
        }
        var order = Enumerable.Range(0, count)
            .Where(t => area[t] > 0)
            .OrderBy(t => sy[t] / area[t] / tileSize)
            .ThenBy(t => sx[t] / area[t])
            .ThenBy(t => t)
            .ToList();
        var id = new int[count];
        for (int k = 0; k < order.Count; k++)
            id[order[k]] = k;
        for (int i = 0; i < tileOf.Length; i++)
            tileOf[i] = id[tileOf[i]];
        return tileOf;
    }
}
