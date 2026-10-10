namespace Wunderwaffe.Sim.World;

/// <summary>A tech node completing, for unlock notifications.</summary>
public readonly record struct TechUnlock(long Tick, string TechId);

/// <summary>
/// One player's empire-wide progress (TT-005): what they have made, which tech they hold and how far through the
/// goal chain they are. Counters only ever grow.
/// </summary>
public sealed class PlayerState(int id)
{
    readonly SortedDictionary<string, long> _produced = new(StringComparer.Ordinal);
    readonly SortedSet<string> _tech = new(StringComparer.Ordinal);
    readonly List<TechUnlock> _unlockLog = [];
    readonly List<long> _goalTicks = [];

    public int Id { get; } = id;

    /// <summary>Items made by this player's machines, extraction included, by item id.</summary>
    public IReadOnlyDictionary<string, long> Produced => _produced;

    public long ProducedOf(string itemId) => _produced.GetValueOrDefault(itemId);

    /// <summary>Items that arrived on this player's tiles through import ports.</summary>
    public long Shipped { get; internal set; }

    /// <summary>Units this player has lifted off.</summary>
    public long Launched { get; internal set; }

    public bool HasTech(string techId) => _tech.Contains(techId);

    /// <summary>Tech nodes in the order they completed.</summary>
    public IReadOnlyList<TechUnlock> UnlockLog => _unlockLog;

    /// <summary>How many goals of <see cref="Content.ContentDb.Goals"/> are complete; the next one is the current goal.</summary>
    public int GoalsDone => _goalTicks.Count;

    /// <summary>The tick each completed goal finished on.</summary>
    public IReadOnlyList<long> GoalTicks => _goalTicks;

    internal void AddProduced(IReadOnlyDictionary<string, int> items)
    {
        foreach (var (item, n) in items)
            _produced[item] = _produced.GetValueOrDefault(item) + n;
    }

    internal bool Unlock(string techId, long tick)
    {
        if (!_tech.Add(techId))
            return false;
        _unlockLog.Add(new TechUnlock(tick, techId));
        return true;
    }

    internal void CompleteGoal(long tick) => _goalTicks.Add(tick);
}
