namespace Wunderwaffe.Sim.World;

/// <summary>
/// A tile's production and consumption per item, and items shipped per link, over the last 10 minutes (FB-011,
/// IO-002, IO-005). Counts go into 10-second buckets in a ring. Rates are taken over the last full minute of
/// buckets, so they don't flicker with the current bucket. Rate-mode tiles report into the same buckets (OQ-11).
/// </summary>
public sealed class ProductionStats
{
    /// <summary>One history bucket: 10 seconds.</summary>
    public const int BucketTicks = 10 * Simulation.TicksPerSecond;

    /// <summary>Buckets kept: 10 minutes.</summary>
    public const int Buckets = 60;

    /// <summary>Buckets in one minute.</summary>
    public const int PerMinute = 60 * Simulation.TicksPerSecond / BucketTicks;

    long _bucket;
    readonly SortedDictionary<string, long[]> _made = new(StringComparer.Ordinal);
    readonly SortedDictionary<string, long[]> _used = new(StringComparer.Ordinal);
    readonly SortedDictionary<int, long[]> _sent = [];

    /// <summary>Everything made since the tile started, per item. Hashed.</summary>
    public SortedDictionary<string, long> TotalMade { get; } = new(StringComparer.Ordinal);

    /// <summary>Everything used up since the tile started, per item: recipe inputs and fuel. Hashed.</summary>
    public SortedDictionary<string, long> TotalUsed { get; } = new(StringComparer.Ordinal);

    /// <summary>Items sent through export ports since the tile started, per partner tile id. Hashed.</summary>
    public SortedDictionary<int, long> TotalSent { get; } = [];

    /// <summary>Items that have ever been made or used here, in id order.</summary>
    public IEnumerable<string> Items => _made.Keys.Union(_used.Keys).OrderBy(i => i, StringComparer.Ordinal);

    /// <summary>Partner tiles this tile has sent items to, in id order.</summary>
    public IEnumerable<int> Partners => _sent.Keys;

    /// <summary>Moves the current bucket up to <paramref name="tick"/>, clearing the buckets it passes.</summary>
    internal void Advance(long tick)
    {
        long target = tick / BucketTicks;
        if (target == _bucket)
            return;
        long from = Math.Max(_bucket + 1, target - Buckets + 1);
        for (long b = from; b <= target; b++)
        {
            int slot = (int)(b % Buckets);
            foreach (var ring in _made.Values.Concat(_used.Values).Concat(_sent.Values))
                ring[slot] = 0;
        }
        _bucket = target;
    }

    internal void RecordMade(string item, long n) => Record(_made, TotalMade, item, n);
    internal void RecordUsed(string item, long n) => Record(_used, TotalUsed, item, n);
    internal void RecordSent(int partner, long n) => Record(_sent, TotalSent, partner, n);

    void Record<TKey>(SortedDictionary<TKey, long[]> rings, SortedDictionary<TKey, long> totals, TKey key, long n)
        where TKey : notnull
    {
        if (!rings.TryGetValue(key, out var ring))
            rings[key] = ring = new long[Buckets];
        ring[(int)(_bucket % Buckets)] += n;
        totals[key] = totals.GetValueOrDefault(key) + n;
    }

    /// <summary>Items made in the last full minute.</summary>
    public long MadePerMinute(string item) => LastMinute(_made.GetValueOrDefault(item));

    /// <summary>Items used in the last full minute.</summary>
    public long UsedPerMinute(string item) => LastMinute(_used.GetValueOrDefault(item));

    /// <summary>Items sent to <paramref name="partner"/> through export ports in the last full minute (IO-005).</summary>
    public long SentPerMinute(int partner) => LastMinute(_sent.GetValueOrDefault(partner));

    /// <summary>Items made in each full bucket of the last 10 minutes, oldest first (<see cref="Buckets"/> values).</summary>
    public long[] MadeHistory(string item) => History(_made.GetValueOrDefault(item));

    /// <summary>Items used in each full bucket of the last 10 minutes, oldest first.</summary>
    public long[] UsedHistory(string item) => History(_used.GetValueOrDefault(item));

    long LastMinute(long[]? ring)
    {
        if (ring == null)
            return 0;
        long sum = 0;
        for (int k = 1; k <= PerMinute; k++)
            sum += Slot(ring, k);
        return sum;
    }

    long[] History(long[]? ring)
    {
        var h = new long[Buckets];
        if (ring == null)
            return h;
        // Bucket k back from the current one; the current bucket isn't finished, and its slot is the oldest's.
        for (int k = 1; k < Buckets; k++)
            h[Buckets - k] = Slot(ring, k);
        return h;
    }

    long Slot(long[] ring, int back) => _bucket - back < 0 ? 0 : ring[(int)((_bucket - back) % Buckets)];
}
