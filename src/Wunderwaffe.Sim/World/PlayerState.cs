namespace Wunderwaffe.Sim.World;

/// <summary>
/// One player's empire-wide progress (TT-005): what they have made and how far through the
/// goal chain they are. Counters only ever grow.
/// </summary>
public sealed class PlayerState(int id)
{
    readonly SortedDictionary<string, long> _produced = new(StringComparer.Ordinal);
    readonly List<long> _goalTicks = [];

    public int Id { get; } = id;

    /// <summary>Items made by this player's machines, extraction included, by item id.</summary>
    public IReadOnlyDictionary<string, long> Produced => _produced;

    public long ProducedOf(string itemId) => _produced.GetValueOrDefault(itemId);

    /// <summary>Items that arrived on this player's tiles through import ports.</summary>
    public long Shipped { get; internal set; }

    /// <summary>Units this player has lifted off.</summary>
    public long Launched { get; internal set; }

    /// <summary>How many goals of <see cref="Content.ContentDb.Goals"/> are complete; the next one is the current goal.</summary>
    public int GoalsDone => _goalTicks.Count;

    /// <summary>The tick each completed goal finished on.</summary>
    public IReadOnlyList<long> GoalTicks => _goalTicks;

    internal void AddProduced(IReadOnlyDictionary<string, int> items)
    {
        foreach (var (item, n) in items)
            _produced[item] = _produced.GetValueOrDefault(item) + n;
    }

    internal void CompleteGoal(long tick) => _goalTicks.Add(tick);

    /// <summary>Tile this player is looking at (SetViewedTileCommand), or -1 for the world map. Tiles nobody views run in rate mode.</summary>
    public int ViewedTile { get; internal set; } = -1;
}
