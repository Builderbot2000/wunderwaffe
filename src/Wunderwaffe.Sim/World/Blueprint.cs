using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// One building of a blueprint. (<paramref name="X"/>, <paramref name="Y"/>) is the top-left of its footprint after
/// <paramref name="Rotation"/>, in the blueprint's own grid.
/// </summary>
public sealed record BlueprintBlock(string DefId, int X, int Y, int Rotation, string? RecipeId = null);

/// <summary>A frame cell of a blueprint, in the blueprint's own grid.</summary>
public readonly record struct BlueprintCell(int X, int Y);

/// <summary>
/// A saved set of buildings and frame (FB-009, UD-001): a plain list the library stores outside the sim and the
/// place-blueprint and factory commands carry in. Coordinates start at (0, 0) at the top-left of what it covers.
/// </summary>
public sealed record Blueprint(BlueprintBlock[] Blocks, BlueprintCell[] Frames)
{
    /// <summary>Every cell a block or frame covers, block cells first. Frames under blocks appear twice.</summary>
    public IEnumerable<(int X, int Y)> Cells(IReadOnlyDictionary<string, BuildingDef> defs)
    {
        foreach (var b in Blocks)
        {
            var (w, h) = defs.TryGetValue(b.DefId, out var def) ? def.Footprint(b.Rotation) : (1, 1);
            for (int y = b.Y; y < b.Y + h; y++)
            {
                for (int x = b.X; x < b.X + w; x++)
                    yield return (x, y);
            }
        }
        foreach (var f in Frames)
            yield return (f.X, f.Y);
    }

    /// <summary>Size of the box the blueprint covers.</summary>
    public (int W, int H) Size(IReadOnlyDictionary<string, BuildingDef> defs)
    {
        int w = 0, h = 0;
        foreach (var (x, y) in Cells(defs))
        {
            w = Math.Max(w, x + 1);
            h = Math.Max(h, y + 1);
        }
        return (w, h);
    }

    /// <summary>Has a core on frame, so building it ends in a lift-off.</summary>
    public bool IsUnit(IReadOnlyDictionary<string, BuildingDef> defs) =>
        Frames.Length > 0 && Blocks.Any(b => defs.TryGetValue(b.DefId, out var d) && d.Kind == BuildingKind.Core);
}
