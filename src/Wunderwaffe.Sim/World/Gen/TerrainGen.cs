using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.World.Gen;

/// <summary>
/// Phase one of world generation: one realistic terrain for the whole world, before it is cut into tiles.
/// <list type="number">
/// <item>Elevation: domain-warped fractal noise, sunk toward the world's rim so land forms continents and islands.</item>
/// <item>Sea level and land heights by rank, so every seed has the same share of land and of high ground.</item>
/// <item>Drainage: priority-flood from the sea. Deep enough depressions become lakes, and cells draining a large
/// enough catchment become rivers.</item>
/// <item>Moisture: noise plus nearness to water.</item>
/// <item>Each cell takes the first terrain in <c>terrain.json</c> whose bands contain its elevation and moisture.</item>
/// </list>
/// All integer (NF-004).
/// </summary>
internal static class TerrainGen
{
    public const int SeaLevel = 400;
    public const int MaxElevation = 1000;

    /// <summary>Elevation given to hand-built tiles.</summary>
    public const int LowlandElevation = 500;

    const int LandPercent = 55;

    /// <summary>A depression this deep (in elevation units) once filled becomes a lake.</summary>
    const int LakeDepth = 12;

    public static (byte[] Terrain, short[] Elevation) Generate(ContentDb content, int w, int h, int tileSize, ulong seed)
    {
        var elevation = Elevation(w, h, tileSize, seed);
        var river = Drain(elevation, w, h, tileSize);
        var moisture = Moisture(elevation, river, w, h, tileSize, seed);

        var terrain = new byte[w * h];
        for (int i = 0; i < terrain.Length; i++)
        {
            terrain[i] = (byte)(river[i] && content.RiverTerrain >= 0
                ? content.RiverTerrain
                : content.Classify(elevation[i], moisture[i]));
        }
        return (terrain, elevation);
    }

    static short[] Elevation(int w, int h, int tileSize, ulong seed)
    {
        int n = w * h;
        var raw = new int[n];
        int warp = Math.Max(1, tileSize / 2), half = Noise.Max / 2;
        long rim = Math.Max(1, Math.Min(w, h) / 6);
        // Each cell depends only on its coordinates, so rows can run in parallel without changing the result.
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int wx = x + (Noise.Value(x, y, tileSize * 2, seed ^ 0x51ED270B27D3F1A1UL) - half) * warp / half;
                int wy = y + (Noise.Value(x, y, tileSize * 2, seed ^ 0x2545F4914F6CDD1DUL) - half) * warp / half;
                int v = Noise.Fractal(wx, wy, tileSize * 3, 6, seed ^ 0x9E6C63D0676A9A99UL);
                long d = Math.Min(Math.Min(x, y), Math.Min(w - 1 - x, h - 1 - y));
                if (d < rim)
                    v -= (int)((rim - d) * (rim - d) * 40000 / (rim * rim));
                raw[y * w + x] = v;
            }
        });
        int min = raw.Min(), max = raw.Max();

        // below[v - min] = how many cells are lower than v.
        var below = new int[max - min + 2];
        foreach (int v in raw)
            below[v - min + 1]++;
        for (int i = 1; i < below.Length; i++)
            below[i] += below[i - 1];

        int water = (int)((long)n * (100 - LandPercent) / 100);
        int sea = 0;
        while (sea + 1 < below.Length && below[sea + 1] <= water)
            sea++;
        long wet = Math.Max(1, below[sea]), land = Math.Max(1, n - below[sea]);

        var elevation = new short[n];
        for (int i = 0; i < n; i++)
        {
            int r = raw[i] - min;
            if (r < sea)
            {
                elevation[i] = (short)(SeaLevel * below[r] / wet);
            }
            else
            {
                // Squared rank, so most land is low and peaks are rare.
                long c = below[r] - below[sea];
                elevation[i] = (short)(SeaLevel + (MaxElevation - SeaLevel) * c * c / (land * land));
            }
        }
        return elevation;
    }

    /// <summary>
    /// Priority-flood from the sea and the world's rim: each land cell drains to the cell that reached it, so every
    /// cell has a path to the sea. Fills deep depressions as lakes (lowering them below sea level) and returns the
    /// river cells.
    /// </summary>
    static bool[] Drain(short[] elevation, int w, int h, int tileSize)
    {
        int n = w * h;
        var filled = new int[n];
        var down = new int[n];
        var done = new bool[n];
        var order = new List<int>(n);
        var queue = new PriorityQueue<int, long>();
        Array.Fill(down, -1);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                filled[i] = elevation[i];
                bool rimCell = x == 0 || y == 0 || x == w - 1 || y == h - 1;
                bool wet = elevation[i] < SeaLevel;
                if (!wet && !rimCell)
                    continue;
                done[i] = true;
                // Water only matters where it meets land.
                if (rimCell || (x > 0 && elevation[i - 1] >= SeaLevel) || (x < w - 1 && elevation[i + 1] >= SeaLevel)
                    || (y > 0 && elevation[i - w] >= SeaLevel) || (y < h - 1 && elevation[i + w] >= SeaLevel))
                    queue.Enqueue(i, ((long)filled[i] << 32) | (uint)i);
            }
        }

        Span<int> dx = [1, -1, 0, 0], dy = [0, 0, 1, -1];
        while (queue.TryDequeue(out int c, out _))
        {
            if (elevation[c] >= SeaLevel)
                order.Add(c);
            int cx = c % w, cy = c / w;
            for (int k = 0; k < 4; k++)
            {
                int nx = cx + dx[k], ny = cy + dy[k];
                if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                    continue;
                int i = ny * w + nx;
                if (done[i])
                    continue;
                done[i] = true;
                filled[i] = Math.Max(elevation[i], filled[c]);
                down[i] = c;
                queue.Enqueue(i, ((long)filled[i] << 32) | (uint)i);
            }
        }

        var lake = new bool[n];
        foreach (int i in order)
        {
            if (filled[i] - elevation[i] >= LakeDepth)
                lake[i] = true;
        }

        // Flow: every land cell adds itself and everything upstream to the cell it drains to.
        var flow = new int[n];
        for (int k = order.Count - 1; k >= 0; k--)
        {
            int c = order[k];
            flow[c]++;
            if (down[c] >= 0 && elevation[down[c]] >= SeaLevel)
                flow[down[c]] += flow[c];
        }

        int riverFlow = Math.Max(16, tileSize * tileSize / 5);
        var river = new bool[n];
        foreach (int i in order)
        {
            if (lake[i])
                elevation[i] = SeaLevel - 1;
            else if (flow[i] >= riverFlow)
                river[i] = true;
        }
        return river;
    }

    static int[] Moisture(short[] elevation, bool[] river, int w, int h, int tileSize, ulong seed)
    {
        int n = w * h;
        // Distance to the nearest water, in cells, capped at a tile's width.
        var dist = new int[n];
        Array.Fill(dist, int.MaxValue);
        var queue = new Queue<int>();
        for (int i = 0; i < n; i++)
        {
            if (elevation[i] < SeaLevel || river[i])
            {
                dist[i] = 0;
                queue.Enqueue(i);
            }
        }
        Span<int> dx = [1, -1, 0, 0], dy = [0, 0, 1, -1];
        while (queue.TryDequeue(out int c))
        {
            if (dist[c] >= tileSize)
                continue;
            int cx = c % w, cy = c / w;
            for (int k = 0; k < 4; k++)
            {
                int nx = cx + dx[k], ny = cy + dy[k];
                if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                    continue;
                int i = ny * w + nx;
                if (dist[i] != int.MaxValue)
                    continue;
                dist[i] = dist[c] + 1;
                queue.Enqueue(i);
            }
        }

        // Raw wetness, then ranked over the land so every seed has the same spread of dry and wet ground.
        const int RawMax = 1000;
        var raw = new int[n];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                long noise = (long)Noise.Fractal(x, y, tileSize * 2, 4, seed ^ 0x8CB92BA72F3D8DD7UL) * RawMax / Noise.Max;
                long near = Math.Max(0, tileSize - Math.Min(dist[i], tileSize)) * (long)RawMax / tileSize;
                raw[i] = (int)(noise * 65 / 100 + near * 35 / 100);
            }
        });
        var below = new int[RawMax + 2];
        int land = 0;
        for (int i = 0; i < n; i++)
        {
            if (elevation[i] >= SeaLevel)
            {
                below[raw[i] + 1]++;
                land++;
            }
        }
        for (int i = 1; i < below.Length; i++)
            below[i] += below[i - 1];

        var moisture = new int[n];
        for (int i = 0; i < n; i++)
            moisture[i] = elevation[i] < SeaLevel ? 1000 : (int)(1000L * below[raw[i]] / Math.Max(1, land));
        return moisture;
    }
}
