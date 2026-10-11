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

    /// <summary>One series: its bucket ring and its all-time total.</summary>
    sealed class Series
    {
        public readonly long[] Ring = new long[Buckets];
        public long Total;
    }

    /// <summary>Series in key order, for hashing and listing, with an unordered index for the per-batch lookup (NF-001).</summary>
    sealed class Table<TKey>(IComparer<TKey> order) where TKey : notnull
    {
        public readonly SortedDictionary<TKey, Series> Sorted = new(order);
        readonly Dictionary<TKey, Series> _index = [];

        public Series? Find(TKey key) => _index.GetValueOrDefault(key);

        public Series Get(TKey key)
        {
            if (!_index.TryGetValue(key, out var series))
                _index[key] = Sorted[key] = series = new Series();
            return series;
        }
    }

    long _bucket;
    readonly Table<string> _made = new(StringComparer.Ordinal);
    readonly Table<string> _used = new(StringComparer.Ordinal);
    readonly Table<int> _sent = new(Comparer<int>.Default);

    /// <summary>Everything made since the tile started, per item, in id order. Hashed.</summary>
    public IEnumerable<(string Item, long Total)> TotalMade => _made.Sorted.Select(kv => (kv.Key, kv.Value.Total));

    /// <summary>Everything used up since the tile started, per item, in id order: recipe inputs and fuel. Hashed.</summary>
    public IEnumerable<(string Item, long Total)> TotalUsed => _used.Sorted.Select(kv => (kv.Key, kv.Value.Total));

    /// <summary>Items sent through export ports since the tile started, per partner tile id, in id order. Hashed.</summary>
    public IEnumerable<(int Partner, long Total)> TotalSent => _sent.Sorted.Select(kv => (kv.Key, kv.Value.Total));

    /// <summary>All-time totals of one item.</summary>
    public long MadeTotal(string item) => _made.Find(item)?.Total ?? 0;
    public long UsedTotal(string item) => _used.Find(item)?.Total ?? 0;

    /// <summary>Items that have ever been made or used here, in id order.</summary>
    public IEnumerable<string> Items => _made.Sorted.Keys.Union(_used.Sorted.Keys).OrderBy(i => i, StringComparer.Ordinal);

    /// <summary>Partner tiles this tile has sent items to, in id order.</summary>
    public IEnumerable<int> Partners => _sent.Sorted.Keys;

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
            foreach (var series in _made.Sorted.Values.Concat(_used.Sorted.Values).Concat(_sent.Sorted.Values))
                series.Ring[slot] = 0;
        }
        _bucket = target;
    }

    internal void RecordMade(string item, long n) => Record(_made.Get(item), n);
    internal void RecordUsed(string item, long n) => Record(_used.Get(item), n);
    internal void RecordSent(int partner, long n) => Record(_sent.Get(partner), n);

    void Record(Series series, long n)
    {
        series.Ring[(int)(_bucket % Buckets)] += n;
        series.Total += n;
    }

    /// <summary>Items made in the last full minute.</summary>
    public long MadePerMinute(string item) => LastMinute(_made.Find(item)?.Ring);

    /// <summary>Items used in the last full minute.</summary>
    public long UsedPerMinute(string item) => LastMinute(_used.Find(item)?.Ring);

    /// <summary>Items sent to <paramref name="partner"/> through export ports in the last full minute (IO-005).</summary>
    public long SentPerMinute(int partner) => LastMinute(_sent.Find(partner)?.Ring);

    /// <summary>Items made in each full bucket of the last 10 minutes, oldest first (<see cref="Buckets"/> values).</summary>
    public long[] MadeHistory(string item) => History(_made.Find(item)?.Ring);

    /// <summary>Items used in each full bucket of the last 10 minutes, oldest first.</summary>
    public long[] UsedHistory(string item) => History(_used.Find(item)?.Ring);

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
