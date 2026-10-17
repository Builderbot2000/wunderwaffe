namespace Wunderwaffe.Sim.Core;

/// <summary>Item counts. Sorted by id so iteration (and therefore hashing) is deterministic (NF-004).</summary>
public sealed class Inventory
{
    readonly SortedDictionary<string, long> _counts = new(StringComparer.Ordinal);

    public IEnumerable<KeyValuePair<string, long>> Entries => _counts;

    public long Get(string id) => _counts.TryGetValue(id, out var n) ? n : 0;

    public void Add(string id, long amount)
    {
        long next = Get(id) + amount;
        if (next < 0)
            throw new InvalidOperationException($"inventory underflow: {id}");
        if (next == 0)
            _counts.Remove(id);
        else
            _counts[id] = next;
    }

    public void Add(IReadOnlyDictionary<string, int> items)
    {
        foreach (var (id, n) in items)
            Add(id, n);
    }

    /// <summary>Moves everything <paramref name="from"/> holds into this one, leaving it empty.</summary>
    public void TakeAll(Inventory from)
    {
        foreach (var (id, n) in from._counts)
            Add(id, n);
        from._counts.Clear();
    }

    public bool Has(IReadOnlyDictionary<string, int> items) =>
        items.All(kv => Get(kv.Key) >= kv.Value);

    /// <summary>Removes all of <paramref name="items"/> or nothing.</summary>
    public bool TryTake(IReadOnlyDictionary<string, int> items)
    {
        if (!Has(items))
            return false;
        foreach (var (id, n) in items)
            Add(id, -n);
        return true;
    }
}
