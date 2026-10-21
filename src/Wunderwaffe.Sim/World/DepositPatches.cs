using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// One deposit patch (RS-004, OQ-49): 4-connected cells of one item, found when the tile's deposits were laid, holding
/// one amount every extractor on it draws from. When it runs out its cells stop being deposits.
/// </summary>
public sealed class DepositPatch
{
    internal DepositPatch(int id, string item, DepositDef def, int cells, long start, long amount)
    {
        Id = id;
        Item = item;
        Def = def;
        Cells = cells;
        Start = start;
        Amount = amount;
        Level = LevelFor(amount);
    }

    /// <summary>1-based, in the order the patches were found (row by row from the top-left of the tile's box).</summary>
    public int Id { get; }

    public string Item { get; }
    public DepositDef Def { get; }

    /// <summary>Cells it covered when found.</summary>
    public int Cells { get; }

    /// <summary>What it held when found.</summary>
    public long Start { get; internal set; }

    /// <summary>What is left.</summary>
    public long Amount { get; internal set; }

    /// <summary>Density level (<see cref="DepositDef.LevelOf"/>) of <see cref="Amount"/>, for drawing.</summary>
    public int Level { get; internal set; }

    public bool Empty => Amount <= 0;

    internal int LevelFor(long amount) => Def.LevelOf(amount / Math.Max(1, Cells));

    // Bounding box in the grid, so emptying it needn't scan the whole tile.
    internal int X0, Y0, X1, Y1;
}

/// <summary>
/// A tile's deposit patches (RS-004) beside the deposit layer: a patch id per cell (0 = none) and the patches' amounts.
/// Sim state: hashed (<c>StateHash.MixPatches</c>) and snapshotted (<c>PatchSnap</c>). A deposit cell outside every
/// patch (laid by hand after its tile's deposits were found: tests, benchmark scenes) never runs out.
/// </summary>
public sealed class DepositPatches(TileGrid grid)
{
    ushort[]? _cells;

    // Bumped whenever the patch map changes (Find, Restore, Detach), so cached extractor sources know to look again.
    int _cellsVersion;
    readonly List<DepositPatch> _list = [];

    /// <summary>Every patch, emptied ones too, by <see cref="DepositPatch.Id"/> - 1.</summary>
    public IReadOnlyList<DepositPatch> All => _list;

    /// <summary>
    /// Bumped when a patch crosses a density threshold or runs out, so views redraw deposits then and not on every
    /// extraction. Not sim state.
    /// </summary>
    public int Revision { get; private set; }

    public DepositPatch? ById(int id) => id > 0 && id <= _list.Count ? _list[id - 1] : null;

    /// <summary>Patch id of a cell; 0 for none (no deposit, outside the box, emptied or never in one).</summary>
    public int IdAt(int x, int y) => _cells != null && grid.InRect(x, y) ? _cells[y * grid.Width + x] : 0;

    /// <summary>The patch a cell belongs to, or null.</summary>
    public DepositPatch? At(int x, int y) => ById(IdAt(x, y));

    /// <summary>
    /// Where an extractor of <paramref name="item"/> standing at <paramref name="b"/>'s footprint draws from: the first cell
    /// of the footprint (row by row) holding the item decides; its patch id, 0 for a cell in no patch (never runs out),
    /// or -1 if no cell under it holds the item any more.
    /// </summary>
    public int SourceOf(PlacedBuilding b, string item)
    {
        for (int y = b.Y; y < b.Y + b.Height; y++)
        {
            for (int x = b.X; x < b.X + b.Width; x++)
            {
                if (grid.DepositAt(x, y) == item)
                    return IdAt(x, y);
            }
        }
        return -1;
    }

    /// <summary>
    /// <see cref="SourceOf(PlacedBuilding, string)"/> for an extractor's machine, cached on it until the grid or the
    /// patch map changes: the full sim asks on every batch, and a footprint scan per batch cost ~10% of the NF-001 tick.
    /// </summary>
    internal int SourceOf(PlacedBuilding b, MachineState m, string item)
    {
        long key = ((long)grid.Revision << 32) | (uint)_cellsVersion;
        if (m.SourceKey != key)
        {
            m.Source = SourceOf(b, item);
            m.SourceKey = key;
        }
        return m.Source;
    }

    /// <summary>
    /// Groups the grid's deposit cells into patches, replacing any found before: connected cells of one item that has a
    /// <see cref="ItemDef.Deposit"/>, each holding its per-cell amount times its cells times <paramref name="richnessPercent"/>.
    /// </summary>
    internal void Find(ContentDb content, int richnessPercent = 100)
    {
        _list.Clear();
        _cells = new ushort[grid.Width * grid.Height];
        _cellsVersion++;
        var queue = new Queue<int>();
        for (int start = 0; start < _cells.Length; start++)
        {
            int sx = start % grid.Width, sy = start / grid.Width;
            if (_cells[start] != 0 || grid.DepositAt(sx, sy) is not { } item || content.Items[item].Deposit is not { } def)
                continue;
            if (_list.Count == ushort.MaxValue)
                throw new InvalidOperationException("too many deposit patches on one tile");
            int id = _list.Count + 1;
            int cells = 0, x0 = sx, y0 = sy, x1 = sx, y1 = sy;
            _cells[start] = (ushort)id;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                int x = c % grid.Width, y = c / grid.Width;
                cells++;
                (x0, y0, x1, y1) = (Math.Min(x0, x), Math.Min(y0, y), Math.Max(x1, x), Math.Max(y1, y));
                foreach (var (nx, ny) in (ReadOnlySpan<(int, int)>)[(x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)])
                {
                    if (!grid.InRect(nx, ny) || _cells[ny * grid.Width + nx] != 0 || grid.DepositAt(nx, ny) != item)
                        continue;
                    _cells[ny * grid.Width + nx] = (ushort)id;
                    queue.Enqueue(ny * grid.Width + nx);
                }
            }
            long amount = Math.Max(1, (long)def.PerCell * cells * richnessPercent / 100);
            _list.Add(new DepositPatch(id, item, def, cells, amount, amount) { X0 = x0, Y0 = y0, X1 = x1, Y1 = y1 });
        }
        Revision++;
    }

    /// <summary>Takes up to <paramref name="n"/> from a patch; at zero its cells stop being deposits. Returns what it took.</summary>
    internal long Take(DepositPatch p, long n)
    {
        long took = Math.Min(n, p.Amount);
        SetAmount(p, p.Amount - took);
        return took;
    }

    /// <summary>
    /// An extractor's finished batch (<paramref name="made"/>) comes out of the patch under it. Returns what it really
    /// makes when the patch (or every deposit under it) ran out first, so a drill never mines more than was there; null
    /// when it all came out.
    /// </summary>
    internal IReadOnlyDictionary<string, int>? Extract(PlacedBuilding b, MachineState m, string item, IReadOnlyDictionary<string, int> made)
    {
        int want = made.GetValueOrDefault(item), source = SourceOf(b, m, item);
        if (source == 0)
            return null;
        long got = ById(source) is { } p ? Take(p, want) : 0;
        if (got == want)
            return null;
        var less = made.ToDictionary(kv => kv.Key, kv => kv.Value);
        if (got == 0)
            less.Remove(item);
        else
            less[item] = (int)got;
        return less;
    }

    /// <summary>
    /// Sets what a patch holds (scenarios, tests); 0 or less empties it, which clears its cells from the deposit layer
    /// and from the patch map (that bumps the grid's <see cref="TileGrid.Revision"/>, so rates are solved again).
    /// </summary>
    internal void SetAmount(DepositPatch p, long amount)
    {
        p.Amount = Math.Max(0, amount);
        int level = p.LevelFor(p.Amount);
        if (level != p.Level)
        {
            p.Level = level;
            Revision++;
        }
        if (p.Amount > 0 || _cells == null)
            return;
        for (int y = p.Y0; y <= p.Y1; y++)
        {
            for (int x = p.X0; x <= p.X1; x++)
            {
                int i = y * grid.Width + x;
                if (_cells[i] != p.Id)
                    continue;
                _cells[i] = 0;
                grid.SetDeposit(x, y, null);
            }
        }
        Revision++;
    }

    /// <summary>Multiplies what a patch held and holds by <paramref name="percent"/>: a neutral site's richer deposit (WM-018).</summary>
    internal void Enrich(DepositPatch p, int percent)
    {
        p.Start = Math.Max(1, p.Start * percent / 100);
        SetAmount(p, Math.Max(1, p.Amount * percent / 100));
    }

    /// <summary>Takes a cell out of its patch (the patch keeps its amount and starting size).</summary>
    internal void Detach(int x, int y)
    {
        if (_cells != null && grid.InRect(x, y))
        {
            _cells[y * grid.Width + x] = 0;
            _cellsVersion++;
        }
    }

    // ---- Hashing and snapshots (NF-008). ----

    /// <summary>The patch id of every cell, row by row (empty before <see cref="Find"/>).</summary>
    internal ReadOnlySpan<ushort> CellIds => _cells;

    /// <summary>Puts patches back as a snapshot or scenario has them: per patch (item, cells, start, amount), cells as run triples.</summary>
    internal void Restore(ContentDb content, IReadOnlyList<(string Item, int Cells, long Start, long Amount)> patches, IReadOnlyList<int> runs)
    {
        _list.Clear();
        _cells = patches.Count == 0 ? null : new ushort[grid.Width * grid.Height];
        _cellsVersion++;
        foreach (var (item, cells, start, amount) in patches)
        {
            var p = new DepositPatch(_list.Count + 1, item, content.Items[item].Deposit!, cells, start, amount)
                { X0 = int.MaxValue, Y0 = int.MaxValue, X1 = -1, Y1 = -1 };
            _list.Add(p);
        }
        for (int r = 0; r + 2 < runs.Count; r += 3)
        {
            var p = _list[runs[r + 2] - 1];
            for (int i = runs[r]; i < runs[r] + runs[r + 1]; i++)
            {
                _cells![i] = (ushort)p.Id;
                int x = i % grid.Width, y = i / grid.Width;
                (p.X0, p.Y0, p.X1, p.Y1) = (Math.Min(p.X0, x), Math.Min(p.Y0, y), Math.Max(p.X1, x), Math.Max(p.Y1, y));
            }
        }
        Revision++;
    }
}
