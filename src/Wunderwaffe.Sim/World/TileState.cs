using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// Rate: aggregated production only (FB-012). Full: entity-level sim while viewed or contested (FB-013).
/// </summary>
public enum SimMode
{
    Rate,
    Full,
}

public sealed class TileState(int id, int x, int y, int width, int height)
{
    public const int Unowned = -1;

    public int Id { get; } = id;
    public int X { get; } = x;
    public int Y { get; } = y;
    public int Owner { get; set; } = Unowned;
    public SimMode Mode { get; set; } = SimMode.Rate;
    public Inventory Inventory { get; } = new();
    public TileGrid Grid { get; } = new(width, height);

    /// <summary>
    /// Rate-mode view of the grid: one line per recipe, with a machine per building running it.
    /// Derived from the grid as buildings are placed and removed (computed, per OQ-14). Keyed by
    /// recipe id so processing order is deterministic.
    /// </summary>
    public SortedDictionary<string, ProductionLine> Lines { get; } = new(StringComparer.Ordinal);

    internal void AddMachines(string recipeId, int delta)
    {
        if (!Lines.TryGetValue(recipeId, out var line))
        {
            line = new ProductionLine(recipeId, 0);
            Lines.Add(recipeId, line);
        }
        line.Machines += delta;
        if (line.Machines == 0)
            Lines.Remove(recipeId);
    }
}

/// <summary>
/// A group of identical machines running one recipe, as seen by rate mode. It stalls when inputs run
/// short, so supply shortages propagate downstream (FB-014).
/// </summary>
public sealed class ProductionLine(string recipeId, int machines)
{
    public string RecipeId { get; } = recipeId;
    public int Machines { get; set; } = machines;

    /// <summary>Machine-ticks of work done toward the next batch.</summary>
    public long Progress { get; set; }
}
