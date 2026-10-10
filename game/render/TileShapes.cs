using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Render;

/// <summary>
/// The shapes of an irregular tile, worked out once per tile and cached: its border as line segments, and its I/O
/// zones as runs of cells, outlines and label points (IO-001). Segments are pairs of points in cells; callers scale
/// them to pixels.
/// </summary>
public static class TileShapes
{
    /// <summary>A horizontal run of cells of one zone, in local cells.</summary>
    public readonly record struct Run(int Zone, int X, int Y, int Length);

    /// <summary>A zone's cells: where to label it (the zone cell nearest its centre) and how big it is.</summary>
    public readonly record struct ZoneInfo(int Neighbor, Vector2 Label, int Cells);

    sealed class Shapes
    {
        public Vector2[] Outline = [];
        public Run[] Runs = [];
        public Dictionary<int, Vector2[]> ZoneOutlines = [];
        public List<ZoneInfo> Zones = [];
    }

    static readonly ConditionalWeakTable<TileState, Shapes> Cache = [];

    /// <summary>The tile's border: edges between its cells and any other, in local cells.</summary>
    public static Vector2[] Outline(TileState tile) => Get(tile).Outline;

    /// <summary>The tile's I/O zones as runs of cells, row by row.</summary>
    public static IReadOnlyList<Run> ZoneRuns(TileState tile) => Get(tile).Runs;

    /// <summary>Each zone's outline, by the neighbour it links to, in local cells.</summary>
    public static IReadOnlyDictionary<int, Vector2[]> ZoneOutlines(TileState tile) => Get(tile).ZoneOutlines;

    public static IReadOnlyList<ZoneInfo> Zones(TileState tile) => Get(tile).Zones;

    static Shapes Get(TileState tile)
    {
        if (!Cache.TryGetValue(tile, out var s))
        {
            s = Build(tile);
            Cache.AddOrUpdate(tile, s);
        }
        return s;
    }

    static Shapes Build(TileState tile)
    {
        var g = tile.Grid;
        var outline = new List<Vector2>();
        var runs = new List<Run>();
        var zoneLines = new Dictionary<int, List<Vector2>>();
        var sums = new Dictionary<int, (long X, long Y, int N)>();

        for (int y = 0; y < g.Height; y++)
        {
            int runZone = -1, runStart = 0;
            for (int x = 0; x <= g.Width; x++)
            {
                int z = g.ZoneAt(x, y);
                if (z != runZone)
                {
                    if (runZone >= 0)
                        runs.Add(new Run(runZone, runStart, y, x - runStart));
                    runZone = z;
                    runStart = x;
                }
                if (!g.InBounds(x, y))
                    continue;

                Side(outline, x, y, g.InBounds);
                if (z >= 0)
                {
                    if (!zoneLines.TryGetValue(z, out var lines))
                        zoneLines[z] = lines = [];
                    Side(lines, x, y, (nx, ny) => g.ZoneAt(nx, ny) == z);
                    var (sx, sy, n) = sums.GetValueOrDefault(z);
                    sums[z] = (sx + x, sy + y, n + 1);
                }
            }
        }

        var zones = new List<ZoneInfo>();
        foreach (var (z, (sx, sy, n)) in sums)
        {
            // The centroid of a zone along a bent border can fall outside it; label the zone cell nearest to it.
            float cx = (float)sx / n, cy = (float)sy / n;
            var best = Vector2.Zero;
            float bestD = float.MaxValue;
            foreach (var r in runs)
            {
                if (r.Zone != z)
                    continue;
                float x = Mathf.Clamp(cx, r.X, r.X + r.Length - 1);
                float d = (x - cx) * (x - cx) + (r.Y - cy) * (r.Y - cy);
                if (d < bestD)
                    (best, bestD) = (new Vector2(x + 0.5f, r.Y + 0.5f), d);
            }
            zones.Add(new ZoneInfo(z, best, n));
        }
        zones.Sort((a, b) => a.Neighbor.CompareTo(b.Neighbor));

        return new Shapes
        {
            Outline = [.. outline],
            Runs = [.. runs],
            ZoneOutlines = Finish(zoneLines),
            Zones = zones,
        };
    }

    /// <summary>Adds the sides of cell (x, y) whose neighbour isn't <paramref name="inside"/>, as point pairs.</summary>
    static void Side(List<Vector2> into, int x, int y, System.Func<int, int, bool> inside)
    {
        if (!inside(x, y - 1))
            into.AddRange([new Vector2(x, y), new Vector2(x + 1, y)]);
        if (!inside(x, y + 1))
            into.AddRange([new Vector2(x, y + 1), new Vector2(x + 1, y + 1)]);
        if (!inside(x - 1, y))
            into.AddRange([new Vector2(x, y), new Vector2(x, y + 1)]);
        if (!inside(x + 1, y))
            into.AddRange([new Vector2(x + 1, y), new Vector2(x + 1, y + 1)]);
    }

    static Dictionary<int, Vector2[]> Finish(Dictionary<int, List<Vector2>> lists)
    {
        var result = new Dictionary<int, Vector2[]>();
        foreach (var (k, v) in lists)
            result[k] = [.. v];
        return result;
    }

    /// <summary>Scales cell-unit points to pixels, offset by <paramref name="origin"/> cells.</summary>
    public static Vector2[] ToPixels(Vector2[] cells, float px, Vector2 origin = default)
    {
        var result = new Vector2[cells.Length];
        for (int i = 0; i < cells.Length; i++)
            result[i] = (cells[i] + origin) * px;
        return result;
    }
}
