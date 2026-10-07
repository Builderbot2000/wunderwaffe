using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim.World;

/// <summary>Seeded deposit layout for a tile's local map (WM-002, WM-009).</summary>
public static class TileGenerator
{
    const int PatchesPerItem = 6;
    const int MinRadius = 3;
    const int MaxRadius = 7;

    public static void GenerateDeposits(TileGrid grid, ContentDb content, Rng rng)
    {
        // Anything some building can extract is a deposit. Sorted so the same seed gives the same map.
        var minable = content.Recipes.Values
            .Where(r => r.Inputs.Count == 0)
            .SelectMany(r => r.Outputs.Keys)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        foreach (var item in minable)
        {
            for (int p = 0; p < PatchesPerItem; p++)
            {
                int cx = rng.Range(0, grid.Width);
                int cy = rng.Range(0, grid.Height);
                int r = rng.Range(MinRadius, MaxRadius + 1);
                for (int y = cy - r; y <= cy + r; y++)
                {
                    for (int x = cx - r; x <= cx + r; x++)
                    {
                        int dx = x - cx, dy = y - cy;
                        if (grid.InBounds(x, y) && dx * dx + dy * dy <= r * r)
                            grid.SetDeposit(x, y, item);
                    }
                }
            }
        }
    }
}
