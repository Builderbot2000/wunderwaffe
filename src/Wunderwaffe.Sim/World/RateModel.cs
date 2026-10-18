using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// Computes the steady-state item flows of tiles from their belt and building graph (FB-012, OQ-14), for tiles that run
/// in rate mode. Each building is a node; items flow along the same links the entity sim uses (a machine's perimeter,
/// a belt's front cell, export port to the partner tile's import ports). A backward pass works out how much each node
/// could take, a forward pass in topological order pushes what sources make through it, sharing each node's output
/// between its targets as the round-robin dump does, and nodes left with more than they pass on are tightened and the
/// passes rerun, which is the steady-state back-up. Tiles linked by ports are solved together, each round reading its
/// partners' last results, so a shortfall in one tile reaches the tiles that depend on it (FB-014).
/// Machines run at their boosted speed and output (FB-016) and at their power network's steady-state satisfaction
/// (FB-005): after a pass at full power, each network's supply (cores, generators as busy as their fuel lets them) is
/// set against its demand (each consumer as busy as its throughput needs at that satisfaction), the satisfaction that
/// balances them slows its consumers, and the passes rerun. Batteries only smooth peaks, so the steady state ignores them.
/// Integer fixed point only; rates are items per tick times <see cref="One"/>.
/// </summary>
public static class RateModel
{
    public const int Shift = 24;

    /// <summary>One item per tick.</summary>
    public const long One = 1L << Shift;

    /// <summary>Stands for "no limit" (a core takes anything); far below overflow when summed.</summary>
    const long Big = 1L << 50;

    const int TileRounds = 32;
    const int FixRounds = 12;
    const int PowerRounds = 4;

    /// <summary>Leftovers smaller than this are rounding, not back-up.</summary>
    const long Slack = One / 100_000;

    /// <summary>Solves a set of tiles linked by ports, from scratch. Pure: the same grids give the same rates.</summary>
    internal static Dictionary<int, TileRates> Solve(Simulation sim, IReadOnlyList<TileState> component)
    {
        var items = sim.Content.Items.Keys.Order(StringComparer.Ordinal).ToArray();
        var graphs = component.Select(t => new TileGraph(sim, t, items)).ToList();
        var byId = graphs.ToDictionary(g => g.Tile.Id);
        for (int round = 0; round < TileRounds; round++)
        {
            bool changed = false;
            foreach (var g in graphs)
                changed |= g.Solve(byId);
            if (!changed)
                break;
        }
        return graphs.ToDictionary(g => g.Tile.Id, g => g.Result());
    }

    enum NodeKind : byte { Inert, Core, Export, Import, Conveyor, Extractor, Crafter }

    /// <summary>One tile's buildings as a flow graph, with the arrays the passes work in (node * items + item).</summary>
    sealed class TileGraph
    {
        readonly string[] _items;
        readonly int _k;
        readonly PlacedBuilding[] _nodes;
        readonly NodeKind[] _kind;
        readonly (int Target, int Weight)[][] _out;
        readonly int[] _ahead;
        readonly int[] _partner;
        readonly long[] _beltCap;
        readonly long[] _batchCap;
        readonly int[] _need;
        readonly int[] _make;

        // Machines (FB-005, FB-016): recipe ticks, progress per tick at full power after speed boosters, output percent
        // after output boosters, power network index (-1 if none), boosted draw if it draws, supply while it burns.
        readonly int[] _ticks, _fullSpeed, _outPct, _net, _powerDraw, _powerMake;

        // Extractors (RS-004): the deposit patch each draws from (DepositPatches.SourceOf: 0 never runs out, -1 none left).
        readonly int[] _source;

        // Per power network with members here (local index into _nets): what its other members here supply and draw
        // whatever the machines do, in power units, and the steady-state satisfaction in permille. A network spanning
        // several tiles (FB-020) adds the other tiles' graphs' figures in Rebalance.
        readonly PowerNetwork[] _nets;
        readonly Dictionary<PowerNetwork, int> _netIndex = [];
        readonly long[] _fixedSupply, _fixedDemand;
        readonly int[] _sat;
        int[] _lastSat = [];
        Dictionary<int, TileGraph> _peers = [];

        // Per machine: batches per tick done in the last forward pass, and its busy share at full power (permille).
        readonly long[] _done;
        readonly long[] _busy;
        readonly int[] _order;
        readonly bool _cyclic;

        readonly long[] _cap, _inflow, _tight, _taken, _offer;
        readonly SortedDictionary<int, long[]> _linkCap = [], _supply = [], _remaining = [];
        readonly long[] _produced, _used, _draw;
        readonly SortedDictionary<int, long[]> _outTo = [], _capTo = [];

        public TileGraph(Simulation sim, TileState tile, string[] items)
        {
            Tile = tile;
            _items = items;
            _k = items.Length;
            var grid = tile.Grid;
            // Networks and boosted machine stats as the full sim sees them; derived state only, rebuilt on Revision.
            _nets = [.. grid.Power.Networks];
            for (int i = 0; i < _nets.Length; i++)
                _netIndex[_nets[i]] = i;
            _nodes = [.. grid.Buildings];
            int n = _nodes.Length;
            var index = new Dictionary<int, int>(n);
            for (int i = 0; i < n; i++)
                index[_nodes[i].Id] = i;

            _kind = new NodeKind[n];
            _out = new (int, int)[n][];
            _ahead = new int[n];
            _partner = new int[n];
            _beltCap = new long[n];
            _batchCap = new long[n];
            _need = new int[n * _k];
            _make = new int[n * _k];
            _ticks = new int[n];
            _fullSpeed = new int[n];
            _outPct = new int[n];
            _net = new int[n];
            _powerDraw = new int[n];
            _powerMake = new int[n];
            _source = new int[n];
            _done = new long[n];
            _busy = new long[n];
            for (int i = 0; i < n; i++)
            {
                var b = _nodes[i];
                _ahead[i] = -1;
                _partner[i] = -1;
                var recipe = b.RecipeId is { } rid ? sim.Content.Recipes[rid] : null;
                _kind[i] = b.Kind switch
                {
                    BuildingKind.Core => NodeKind.Core,
                    BuildingKind.ExportPort => NodeKind.Export,
                    BuildingKind.ImportPort => NodeKind.Import,
                    BuildingKind.Conveyor => NodeKind.Conveyor,
                    BuildingKind.Machine when recipe is { Inputs.Count: 0 } => NodeKind.Extractor,
                    BuildingKind.Machine when recipe != null => NodeKind.Crafter,
                    _ => NodeKind.Inert,
                };
                if (b.Kind is BuildingKind.ExportPort or BuildingKind.ImportPort)
                    _partner[i] = PortRules.PartnerOf(grid, b);
                if (_kind[i] == NodeKind.Extractor)
                    _source[i] = grid.Patches.SourceOf(b, recipe!.Outputs.Keys.First());
                _net[i] = grid.Power.NetworkOf(b) is { } net ? _netIndex[net] : -1;
                if (recipe != null && b.Machine is { } m)
                {
                    _ticks[i] = recipe.Ticks;
                    _fullSpeed[i] = MachineState.ProgressPerTick * m.SpeedPercent / 100;
                    _outPct[i] = 100 + m.OutputBonus;
                    _powerDraw[i] = m.Draws ? m.BoostedDraw : 0;
                    _powerMake[i] = m.MaxSupply;
                    foreach (var (id, c) in recipe.Inputs)
                        _need[i * _k + Array.IndexOf(items, id)] = c;
                    foreach (var (id, c) in recipe.Outputs)
                        _make[i * _k + Array.IndexOf(items, id)] = c;
                }

                var targets = new List<(int Target, int Weight)>();
                void Link(int x, int y)
                {
                    if (grid.BuildingAt(x, y) is not { } t || t == b)
                        return;
                    int ti = index[t.Id];
                    int at = targets.FindIndex(e => e.Target == ti);
                    if (at < 0)
                        targets.Add((ti, 1));
                    else
                        targets[at] = (ti, targets[at].Weight + 1);
                }
                if (_kind[i] is NodeKind.Extractor or NodeKind.Crafter or NodeKind.Import)
                {
                    // The same perimeter walk the entity sim dumps along: a target seen from k cells gets k turns.
                    int around = 2 * (b.Width + b.Height);
                    for (int p = 0; p < around; p++)
                    {
                        var (x, y) = Perimeter(b, p);
                        Link(x, y);
                    }
                }
                else if (_kind[i] == NodeKind.Conveyor)
                {
                    _beltCap[i] = (long)b.Belt!.Speed * One / BeltState.Spacing;
                    var d = Directions.FromRotation(b.Rotation);
                    Link(b.X + d.Dx(), b.Y + d.Dy());
                    if (targets.Count > 0)
                        _ahead[i] = targets[0].Target;
                }
                _out[i] = [.. targets];
            }

            // Drop links nothing can cross (a belt facing back into its source, a drill next to a drill, two touching
            // smelters), so they don't make false loops in the order.
            for (int i = 0; i < n; i++)
                _out[i] = _out[i].Where(e => _kind[e.Target] switch
                {
                    NodeKind.Core or NodeKind.Export => true,
                    NodeKind.Crafter => Feeds(i, e.Target),
                    NodeKind.Conveyor => _ahead[e.Target] != i,
                    _ => false,
                }).ToArray();
            (_order, _cyclic) = TopologicalOrder(n);

            _fixedSupply = new long[_nets.Length];
            _fixedDemand = new long[_nets.Length];
            _sat = new int[_nets.Length];
            for (int ni = 0; ni < _nets.Length; ni++)
            {
                foreach (var b in _nets[ni].MembersOn(tile.Id))
                {
                    if (_kind[index[b.Id]] is NodeKind.Extractor or NodeKind.Crafter)
                        continue;
                    _fixedSupply[ni] += PowerNet.SupplyOf(b);
                    _fixedDemand[ni] += PowerNet.DrawOf(b);
                }
            }
            _cap = new long[n * _k];
            _inflow = new long[n * _k];
            _tight = new long[n * _k];
            _taken = new long[n * _k];
            _offer = new long[n * _k];
            _produced = new long[_k];
            _used = new long[_k];
            _draw = new long[_k];
        }

        public TileState Tile { get; }

        static (int X, int Y) Perimeter(PlacedBuilding b, int i)
        {
            if (i < b.Width)
                return (b.X + i, b.Y - 1);
            i -= b.Width;
            if (i < b.Height)
                return (b.X + b.Width, b.Y + i);
            i -= b.Height;
            if (i < b.Width)
                return (b.X + b.Width - 1 - i, b.Y + b.Height);
            i -= b.Width;
            return (b.X - 1, b.Y + b.Height - 1 - i);
        }

        /// <summary>Sources first. Nodes on loops of belts go last, in id order, and the passes repeat for them.</summary>
        (int[] Order, bool Cyclic) TopologicalOrder(int n)
        {
            var indegree = new int[n];
            foreach (var edges in _out)
            {
                foreach (var (t, _) in edges)
                    indegree[t]++;
            }
            var order = new List<int>(n);
            var queue = new Queue<int>();
            for (int i = 0; i < n; i++)
            {
                if (indegree[i] == 0)
                    queue.Enqueue(i);
            }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                order.Add(i);
                foreach (var (t, _) in _out[i])
                {
                    if (--indegree[t] == 0)
                        queue.Enqueue(t);
                }
            }
            bool cyclic = order.Count < n;
            for (int i = 0; i < n && cyclic; i++)
            {
                if (indegree[i] > 0)
                    order.Add(i);
            }
            return ([.. order], cyclic);
        }

        /// <summary>Whether a machine makes something crafter <paramref name="t"/> needs; belts and ports may carry anything.</summary>
        bool Feeds(int i, int t)
        {
            if (_kind[i] is not (NodeKind.Extractor or NodeKind.Crafter))
                return true;
            for (int k = 0; k < _k; k++)
            {
                if (_make[i * _k + k] > 0 && _need[t * _k + k] > 0)
                    return true;
            }
            return false;
        }

        /// <summary>Whether <paramref name="t"/> takes item <paramref name="k"/> from <paramref name="src"/>, as <c>TileEntities.TryInsert</c> decides.</summary>
        bool Accepts(int t, int k, int src) => _kind[t] switch
        {
            NodeKind.Core or NodeKind.Export => true,
            NodeKind.Conveyor => _ahead[t] != src,
            NodeKind.Crafter => _need[t * _k + k] > 0,
            _ => false,
        };

        long[] Row(SortedDictionary<int, long[]> map, int key)
        {
            if (!map.TryGetValue(key, out var row))
                map[key] = row = new long[_k];
            return row;
        }

        /// <summary>One round against the partners' latest results. True if what it offers its partners changed.</summary>
        public bool Solve(Dictionary<int, TileGraph> byId)
        {
            _peers = byId;
            _linkCap.Clear();
            _supply.Clear();
            for (int i = 0; i < _nodes.Length; i++)
            {
                int p = _partner[i];
                if (p < 0 || _linkCap.ContainsKey(p))
                    continue;
                bool linked = byId.TryGetValue(p, out var other) && other.Tile.Owner == Tile.Owner && Tile.Owner != TileState.Unowned;
                _linkCap[p] = linked && other!._capTo.TryGetValue(Tile.Id, out var cap) ? cap : new long[_k];
                _supply[p] = linked && other!._outTo.TryGetValue(Tile.Id, out var sup) ? sup : new long[_k];
            }

            Array.Fill(_sat, 1000);
            SetBatchCaps();
            Settle();
            for (int round = 0; round < PowerRounds && Rebalance(round == 0); round++)
                Settle();

            var oldOut = _outTo.ToDictionary(kv => kv.Key, kv => (long[])kv.Value.Clone());
            var oldCap = _capTo.ToDictionary(kv => kv.Key, kv => (long[])kv.Value.Clone());
            Collect();
            // A network shared with another tile (FB-020) may settle differently once the partner's graph has run.
            bool satChanged = !_sat.AsSpan().SequenceEqual(_lastSat);
            _lastSat = (int[])_sat.Clone();
            return !Same(oldOut, _outTo) || !Same(oldCap, _capTo) || satChanged;
        }

        /// <summary>Backward and forward passes until nothing more backs up.</summary>
        void Settle()
        {
            Array.Fill(_tight, Big);
            for (int round = 0; round < FixRounds; round++)
            {
                Backward();
                Forward();
                if (!Tighten())
                    break;
            }
        }

        /// <summary>
        /// Batches per tick a machine can do at <paramref name="sat"/> permille power, as the full sim steps it: progress
        /// per tick rounds down, and a batch takes whole ticks.
        /// </summary>
        long CapAt(int i, int sat)
        {
            long rate = (long)_fullSpeed[i] * sat / 1000;
            return rate <= 0 ? 0 : One / ((_ticks[i] * (long)MachineState.ProgressPerTick + rate - 1) / rate);
        }

        void SetBatchCaps()
        {
            for (int i = 0; i < _nodes.Length; i++)
            {
                if (_ticks[i] > 0)
                    _batchCap[i] = _source[i] < 0 ? 0 : CapAt(i, _powerDraw[i] > 0 ? _sat[_net[i]] : 1000);
            }
        }

        /// <summary>
        /// Works out each network's steady-state satisfaction from the last forward pass. True if any changed, so the
        /// passes run again at the new speeds. A consumer busy u permille of the time at full power is busy u/s at s, so
        /// demand rises as satisfaction falls; s is the largest value with supply/demand(s) at least s, found by
        /// stepping down from 1000. How busy consumers want to be is read once, from the pass at full power
        /// (<paramref name="atFullPower"/>): a slowed consumer is busy all the time, which would hide what it wants.
        /// Generators' supply is reread every round, since a slowed fuel line burns less.
        /// </summary>
        bool Rebalance(bool atFullPower)
        {
            if (_sat.Length == 0)
                return false;
            if (atFullPower)
            {
                for (int i = 0; i < _nodes.Length; i++)
                {
                    if (_ticks[i] != 0 && _net[i] >= 0 && _powerDraw[i] > 0 && _batchCap[i] > 0)
                        _busy[i] = Math.Min(1000, _done[i] * 1000 / _batchCap[i]);
                }
            }

            bool changed = false;
            for (int n = 0; n < _sat.Length; n++)
            {
                // A network over several tiles (FB-020) counts every tile's graph, each as of its latest round.
                var net = _nets[n];
                var parts = new List<(TileGraph Graph, int Net)> { (this, n) };
                foreach (int id in net.TileIds)
                {
                    if (id != Tile.Id && _peers.TryGetValue(id, out var peer) && peer._netIndex.TryGetValue(net, out int pn))
                        parts.Add((peer, pn));
                }
                long supply = 0;
                foreach (var (g, gn) in parts)
                    supply += g.SupplyOf(gn);
                int s = 1000;
                for (int step = 0; step < 1000; step++)
                {
                    long demand = 0;
                    foreach (var (g, gn) in parts)
                        demand += g.DemandOf(gn, s);
                    int next = demand <= supply ? 1000 : (int)(supply * 1000 / demand);
                    if (next >= s)
                        break;
                    s = next;
                }
                changed |= s != _sat[n];
                _sat[n] = s;
            }
            if (changed)
                SetBatchCaps();
            return changed;
        }

        /// <summary>This tile's steady supply to local network <paramref name="n"/>, times 1000: generators as busy as their fuel lets them.</summary>
        long SupplyOf(int n)
        {
            long supply = _fixedSupply[n] * 1000;
            for (int i = 0; i < _nodes.Length; i++)
            {
                if (_ticks[i] != 0 && _net[i] == n && _powerMake[i] > 0 && _batchCap[i] > 0)
                    supply += _powerMake[i] * Math.Min(1000, _done[i] * 1000 / _batchCap[i]);
            }
            return supply;
        }

        /// <summary>This tile's demand on local network <paramref name="n"/> at satisfaction <paramref name="s"/>, times 1000.</summary>
        long DemandOf(int n, int s)
        {
            long demand = _fixedDemand[n] * 1000;
            for (int i = 0; i < _nodes.Length; i++)
            {
                if (_net[i] == n && _powerDraw[i] > 0 && _busy[i] > 0)
                    demand += _powerDraw[i] * (s == 0 ? 1000 : Math.Min(1000, _busy[i] * 1000 / s));
            }
            return demand;
        }

        static bool Same(Dictionary<int, long[]> a, SortedDictionary<int, long[]> b) =>
            a.Count == b.Count && b.All(kv => a.TryGetValue(kv.Key, out var row) && row.AsSpan().SequenceEqual(kv.Value));

        /// <summary>What a node could take of each item, from the nodes it feeds: last first.</summary>
        void Backward()
        {
            int passes = _cyclic ? 4 : 1;
            for (int pass = 0; pass < passes; pass++)
            {
                for (int o = _order.Length - 1; o >= 0; o--)
                {
                    int i = _order[o];
                    for (int k = 0; k < _k; k++)
                        _cap[i * _k + k] = Math.Min(_tight[i * _k + k], CapOf(i, k));
                }
            }
        }

        long CapOf(int i, int k)
        {
            switch (_kind[i])
            {
                case NodeKind.Core:
                    return Big;
                case NodeKind.Export:
                    return _linkCap.TryGetValue(_partner[i], out var link) ? link[k] : 0;
                case NodeKind.Conveyor:
                    return _ahead[i] >= 0 && Accepts(_ahead[i], k, i) ? Math.Min(_beltCap[i], _cap[_ahead[i] * _k + k]) : 0;
                case NodeKind.Crafter:
                    int need = _need[i * _k + k];
                    return need == 0 ? 0 : need * Math.Min(_batchCap[i], OutBatches(i));
                case NodeKind.Import:
                    // Not a target inside the tile; this is what it could pass on, which its partner reads.
                    return Math.Min(One, Outlet(i, k));
                default:
                    return 0;
            }
        }

        /// <summary>Total that the targets of node <paramref name="i"/> could take of item <paramref name="k"/>.</summary>
        long Outlet(int i, int k)
        {
            long sum = 0;
            foreach (var (t, _) in _out[i])
            {
                if (Accepts(t, k, i))
                    sum = Math.Min(Big, sum + _cap[t * _k + k]);
            }
            return sum;
        }

        /// <summary>Batches per tick (times One) a machine's outputs could get rid of.</summary>
        long OutBatches(int i)
        {
            long best = Big;
            for (int k = 0; k < _k; k++)
            {
                int make = _make[i * _k + k];
                if (make > 0)
                    best = Math.Min(best, Outlet(i, k) * 100 / (make * _outPct[i]));
            }
            return best;
        }

        /// <summary>Pushes what the sources make through the graph, in topological order.</summary>
        void Forward()
        {
            Array.Clear(_inflow);
            Array.Clear(_taken);
            Array.Clear(_offer);
            Array.Clear(_produced);
            Array.Clear(_used);
            _remaining.Clear();
            foreach (var (p, cap) in _linkCap)
                _remaining[p] = (long[])cap.Clone();

            // Each partner's supply, shared between this tile's import ports in its zone like round-robin delivery.
            foreach (var (p, supply) in _supply)
            {
                var ports = Enumerable.Range(0, _nodes.Length).Where(i => _kind[i] == NodeKind.Import && _partner[i] == p).ToList();
                for (int k = 0; k < _k; k++)
                {
                    var room = ports.Select(i => _cap[i * _k + k]).ToArray();
                    var got = WaterFill(supply[k], ports.Select(_ => 1).ToArray(), room);
                    for (int j = 0; j < ports.Count; j++)
                        _offer[ports[j] * _k + k] = got[j];
                }
            }

            foreach (int i in _order)
            {
                switch (_kind[i])
                {
                    case NodeKind.Extractor:
                    case NodeKind.Crafter:
                    {
                        long batches = _batchCap[i];
                        for (int k = 0; k < _k; k++)
                        {
                            int need = _need[i * _k + k];
                            if (need > 0)
                                batches = Math.Min(batches, _inflow[i * _k + k] / need);
                        }
                        if (_kind[i] == NodeKind.Extractor)
                            batches = Math.Min(batches, OutBatches(i));
                        long done = batches;
                        for (int k = 0; k < _k; k++)
                        {
                            long made = Made(i, k, batches);
                            if (made > 0 && Distribute(i, k, made) is var got && got < made)
                                done = Math.Min(done, got * 100 / (_make[i * _k + k] * _outPct[i]));
                        }
                        _done[i] = done;
                        for (int k = 0; k < _k; k++)
                        {
                            _taken[i * _k + k] = done * _need[i * _k + k];
                            _used[k] += done * _need[i * _k + k];
                            _produced[k] += Made(i, k, done);
                        }
                        break;
                    }
                    case NodeKind.Import:
                        for (int k = 0; k < _k; k++)
                            _taken[i * _k + k] = Distribute(i, k, _offer[i * _k + k]);
                        break;
                    case NodeKind.Conveyor:
                        for (int k = 0; k < _k; k++)
                            _taken[i * _k + k] = Distribute(i, k, _inflow[i * _k + k]);
                        break;
                    case NodeKind.Core:
                    case NodeKind.Export:
                        for (int k = 0; k < _k; k++)
                            _taken[i * _k + k] = _inflow[i * _k + k];
                        break;
                }
            }
        }

        /// <summary>Items of k a machine makes in <paramref name="batches"/>, with its output boost (FB-016).</summary>
        long Made(int i, int k, long batches) => batches * _make[i * _k + k] * _outPct[i] / 100;

        /// <summary>Offers <paramref name="amount"/> of item k to node i's targets; returns what they took.</summary>
        long Distribute(int i, int k, long amount)
        {
            var edges = _out[i];
            if (amount <= 0 || edges.Length == 0)
                return 0;
            var weights = new int[edges.Length];
            var room = new long[edges.Length];
            for (int e = 0; e < edges.Length; e++)
            {
                int t = edges[e].Target;
                if (!Accepts(t, k, i))
                    continue;
                weights[e] = edges[e].Weight;
                room[e] = Room(t, k);
            }
            var got = WaterFill(amount, weights, room);
            long total = 0;
            for (int e = 0; e < edges.Length; e++)
            {
                if (got[e] == 0)
                    continue;
                int t = edges[e].Target;
                _inflow[t * _k + k] += got[e];
                if (_kind[t] == NodeKind.Export)
                    _remaining[_partner[t]][k] -= got[e];
                total += got[e];
            }
            return total;
        }

        long Room(int t, int k)
        {
            long have = _inflow[t * _k + k];
            switch (_kind[t])
            {
                case NodeKind.Core:
                    return Big;
                case NodeKind.Export:
                    return _remaining.TryGetValue(_partner[t], out var left) ? Math.Max(0, left[k]) : 0;
                case NodeKind.Conveyor:
                    long carried = 0;
                    for (int j = 0; j < _k; j++)
                        carried += _inflow[t * _k + j];
                    return Math.Max(0, Math.Min(_cap[t * _k + k] - have, _beltCap[t] - carried));
                default:
                    return Math.Max(0, _cap[t * _k + k] - have);
            }
        }

        /// <summary>
        /// Shares <paramref name="amount"/> in proportion to <paramref name="weights"/>, never giving a slot more than its
        /// room; what a full slot can't take goes to the others. Integer remainders go to the first slots with room.
        /// </summary>
        static long[] WaterFill(long amount, int[] weights, long[] room)
        {
            var got = new long[weights.Length];
            var active = Enumerable.Range(0, weights.Length).Where(e => weights[e] > 0 && room[e] > 0).ToList();
            while (amount > 0 && active.Count > 0)
            {
                long w = active.Sum(e => (long)weights[e]);
                var full = active.Where(e => room[e] - got[e] <= amount / w * weights[e] + amount % w * weights[e] / w).ToList();
                if (full.Count == 0)
                {
                    long given = 0;
                    foreach (int e in active)
                    {
                        long share = amount / w * weights[e] + amount % w * weights[e] / w;
                        got[e] += share;
                        given += share;
                    }
                    amount -= given;
                    foreach (int e in active)
                    {
                        if (amount == 0)
                            break;
                        long more = Math.Min(amount, room[e] - got[e]);
                        got[e] += more;
                        amount -= more;
                    }
                    break;
                }
                foreach (int e in full)
                {
                    long more = room[e] - got[e];
                    got[e] += more;
                    amount -= more;
                    active.Remove(e);
                }
            }
            return got;
        }

        /// <summary>
        /// Nodes that were offered more than they passed on back up in the full sim, so they take less: their limit
        /// becomes what they passed on. True if any changed, so the passes run again.
        /// </summary>
        bool Tighten()
        {
            bool changed = false;
            for (int i = 0; i < _nodes.Length; i++)
            {
                if (_kind[i] is not (NodeKind.Conveyor or NodeKind.Crafter or NodeKind.Import))
                    continue;
                for (int k = 0; k < _k; k++)
                {
                    int at = i * _k + k;
                    long offered = _kind[i] == NodeKind.Import ? _offer[at] : _inflow[at];
                    if (offered - _taken[at] <= Slack || _taken[at] >= _tight[at])
                        continue;
                    _tight[at] = _taken[at];
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>Totals of the last forward pass.</summary>
        void Collect()
        {
            Array.Clear(_draw);
            _outTo.Clear();
            _capTo.Clear();
            for (int i = 0; i < _nodes.Length; i++)
            {
                for (int k = 0; k < _k; k++)
                {
                    long v = _taken[i * _k + k];
                    switch (_kind[i])
                    {
                        case NodeKind.Import:
                            _draw[k] += v;
                            Row(_capTo, _partner[i])[k] += _cap[i * _k + k];
                            break;
                        case NodeKind.Export:
                            Row(_outTo, _partner[i])[k] += v;
                            break;
                    }
                }
            }
        }

        public TileRates Result()
        {
            var flows = new List<Flow>();
            void Add(FlowKind kind, int port, long[] rates, int offset = 0)
            {
                for (int k = 0; k < _k; k++)
                {
                    if (rates[offset + k] > 0)
                        flows.Add(new Flow(kind, port, _items[k], rates[offset + k]));
                }
            }
            Add(FlowKind.Produced, 0, _produced);
            Add(FlowKind.Draw, 0, _draw);
            Add(FlowKind.Used, 0, _used);
            for (int i = 0; i < _nodes.Length; i++)
            {
                if (_kind[i] is NodeKind.Export or NodeKind.Core)
                    Add(_kind[i] == NodeKind.Core ? FlowKind.Core : FlowKind.Export, _nodes[i].Id, _taken, i * _k);
            }
            // What the extractors take out of each deposit patch (RS-004): everything they make of the patch's item.
            var mined = new SortedDictionary<int, (int K, long Rate)>();
            for (int i = 0; i < _nodes.Length; i++)
            {
                if (_kind[i] != NodeKind.Extractor || _source[i] <= 0)
                    continue;
                int k = Array.IndexOf(_items, Tile.Grid.Patches.ById(_source[i])!.Item);
                var (_, rate) = mined.GetValueOrDefault(_source[i]);
                mined[_source[i]] = (k, rate + Made(i, k, _done[i]));
            }
            foreach (var (patch, (k, rate)) in mined)
            {
                if (rate > 0)
                    flows.Add(new Flow(FlowKind.Mined, patch, _items[k], rate));
            }
            flows.Sort();
            var rates = new TileRates { Flows = flows };
            foreach (var (p, row) in _outTo)
                rates.LinkOut[p] = row;
            foreach (var (p, row) in _capTo)
                rates.LinkCap[p] = row;
            return rates;
        }
    }
}
