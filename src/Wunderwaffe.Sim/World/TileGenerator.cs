using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim.World;

/// <summary>Seeded deposit layout for a tile's local map (WM-002, WM-009).</summary>
public static class TileGenerator
{
    /// <summary>Patches of each item on a tile of <see cref="Simulation.DefaultTileSize"/> squared buildable cells: fewer, larger
    /// patches per area than a 96-cell tile had (6 of radius 3-7), so a 256-cell tile (OQ-35) reads as a few ore fields.</summary>
    const int PatchesPerItem = 16;
    const int MinRadius = 4;
    const int MaxRadius = 10;

    /// <summary>
    /// Lays circular deposit patches on the tile's buildable ground. The number of patches follows the buildable
    /// area, and each patch's centre is drawn in proportion to the terrain's weight for the item in
    /// <c>terrain.json</c>, falling back to any buildable cell when no terrain on the tile carries it. Then finds the
    /// tile's patches and their amounts (RS-004).
    /// </summary>
    /// <param name="exclude">Minable items this tile has no deposits of (RS-003).</param>
    /// <param name="richnessPercent">Scales the number of patches (match setting; 100 is normal), never below one.</param>
    public static void GenerateDeposits(TileGrid grid, ContentDb content, Rng rng, IReadOnlySet<string>? exclude = null, int richnessPercent = 100)
    {
        if (grid.BuildableCount == 0)
            return;

        // Anything some building can extract is a deposit. Sorted so the same seed gives the same map.
        var minable = content.Recipes.Values
            .Where(r => r.Inputs.Count == 0)
            .SelectMany(r => r.Outputs.Keys)
            .Where(item => exclude == null || !exclude.Contains(item))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        const long nominal = (long)Simulation.DefaultTileSize * Simulation.DefaultTileSize;
        int patches = (int)Math.Max(1, (PatchesPerItem * grid.BuildableCount * richnessPercent / 100 + nominal / 2) / nominal);
        var weights = new long[grid.Width * grid.Height];

        foreach (var item in minable)
        {
            long total = 0;
            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    long w = grid.Buildable(x, y) ? content.TerrainList[grid.TerrainAt(x, y)].DepositWeight(item) : 0;
                    total += w;
                    weights[y * grid.Width + x] = total;
                }
            }
            bool uniform = total == 0;
            if (uniform)
            {
                for (int y = 0; y < grid.Height; y++)
                {
                    for (int x = 0; x < grid.Width; x++)
                    {
                        total += grid.Buildable(x, y) ? 1 : 0;
                        weights[y * grid.Width + x] = total;
                    }
                }
            }

            for (int p = 0; p < patches; p++)
            {
                long pick = (long)(rng.Next() % (ulong)total);
                int c = UpperBound(weights, pick);
                int cx = c % grid.Width, cy = c / grid.Width;
                int r = rng.Range(MinRadius, MaxRadius + 1);
                for (int y = cy - r; y <= cy + r; y++)
                {
                    for (int x = cx - r; x <= cx + r; x++)
                    {
                        int dx = x - cx, dy = y - cy;
                        if (grid.Buildable(x, y) && dx * dx + dy * dy <= r * r)
                            grid.SetDeposit(x, y, item);
                    }
                }
            }
        }
        grid.Patches.Find(content, richnessPercent);
    }

    /// <summary>The first index whose running total is above <paramref name="pick"/>.</summary>
    static int UpperBound(long[] running, long pick)
    {
        int lo = 0, hi = running.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (running[mid] > pick)
                hi = mid;
            else
                lo = mid + 1;
        }
        return lo;
    }
}
