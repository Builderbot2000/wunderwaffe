using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// One power network (FB-005, FB-020): power buildings joined by touching, by cables, by power nodes and, across tiles,
/// by linked port pairs. Its members come from the layout. The per-tick figures are worked out when its first full-sim
/// tile steps, from hashed state (machines working, battery charge), so they need no hashing of their own.
/// </summary>
public sealed class PowerNetwork(int id)
{
    /// <summary>
    /// Index among every network of the world, numbered by their first member, tile by tile in id order and then by
    /// building id; derived, so it can change whenever any layout changes.
    /// </summary>
    public int Id { get; } = id;

    /// <summary>Every member, tile by tile in id order, each tile's in building id order.</summary>
    public List<PlacedBuilding> Members { get; } = [];

    /// <summary>Tiles it has members on, ascending: more than one when port links join it across tiles (FB-020).</summary>
    public List<int> TileIds { get; } = [];

    readonly SortedDictionary<int, List<PlacedBuilding>> _byTile = [];

    /// <summary>Its members on one tile, in id order.</summary>
    public IReadOnlyList<PlacedBuilding> MembersOn(int tileId) => _byTile.TryGetValue(tileId, out var list) ? list : [];

    internal void Add(int tileId, PlacedBuilding b)
    {
        Members.Add(b);
        if (!_byTile.TryGetValue(tileId, out var list))
        {
            _byTile[tileId] = list = [];
            TileIds.Add(tileId);
        }
        list.Add(b);
    }

    /// <summary>Battery members, in member order: charged and drained first to last.</summary>
    internal List<PlacedBuilding> Batteries { get; } = [];

    /// <summary>The tick it was last balanced: a network over several tiles balances once, when the first of them steps.</summary>
    internal long BalancedTick = -1;

    // Supply and demand as of now. Summed when the network is built; afterwards machines report only their changes
    // as they work, so the balance needn't visit every consumer each tick (NF-001).
    internal long LiveSupply, LiveDemand;

    /// <summary>
    /// Updates the live totals with a machine's supply and draw after its work this tick; the same figures as
    /// <see cref="PowerNet.SupplyOf"/> and <see cref="PowerNet.DrawOf"/>, from values cached on the machine.
    /// </summary>
    internal void Report(MachineState m)
    {
        // Writes only on a change: most machines report the same figures tick after tick.
        int draw = m.Working || m.IdleTicks == 0 ? m.BoostedDraw : 0;
        if (draw != m.ReportedDraw)
        {
            LiveDemand += draw - m.ReportedDraw;
            m.ReportedDraw = draw;
        }
        int supply = m.Working ? m.MaxSupply : 0;
        if (supply != m.ReportedSupply)
        {
            LiveSupply += supply - m.ReportedSupply;
            m.ReportedSupply = supply;
        }
    }

    /// <summary>Power made this tick by cores and burning generators.</summary>
    public long Supply { get; internal set; }

    /// <summary>Power asked for this tick by working machines and always-on consumers, after boosters.</summary>
    public long Demand { get; internal set; }

    /// <summary>Power drawn from batteries this tick (negative: power put into them).</summary>
    public long FromBatteries { get; internal set; }

    /// <summary>Battery charge after this tick, and its total capacity, in power-ticks.</summary>
    public long Stored { get; internal set; }
    public long Capacity { get; internal set; }

    /// <summary>Share of the demand met this tick, in permille. Consumers run at this share of full speed.</summary>
    public int Satisfaction { get; internal set; } = 1000;
}

/// <summary>
/// A tile's view of the power networks (FB-005, FB-020) and its machines' boosted stats. Its parts (conductors and
/// cable runs joined on this tile) are rebuilt whenever the tile's layout changes (<see cref="TileGrid.Revision"/>);
/// the <see cref="PowerWorld"/> then joins parts across port links into networks. Every read refreshes first, so it is
/// never stale.
/// </summary>
public sealed class TilePower(TileGrid grid)
{
    PowerWorld? _world;
    internal int TileId;

    /// <summary>Forces a rebuild even if the revision matches (a snapshot restore puts an old revision back).</summary>
    internal bool Dirty;

    // Local parts: building id (power buildings and ports) -> part; cable cell -> part, -1 for none.
    readonly Dictionary<int, int> _part = [];
    int[]? _cablePart;
    internal int PartCount;
    internal PowerNetwork?[] PartNet = [];

    readonly List<PowerNetwork> _networks = [];
    readonly Dictionary<int, PowerNetwork> _byBuilding = [];

    internal TileGrid Grid => grid;

    /// <summary>Called when the tile joins a simulation, so port links can join its networks to its neighbours'.</summary>
    internal void Join(PowerWorld world, int tileId)
    {
        _world = world;
        TileId = tileId;
    }

    /// <summary>Networks with members on this tile, in id order.</summary>
    public IReadOnlyList<PowerNetwork> Networks
    {
        get
        {
            Refresh();
            return _networks;
        }
    }

    /// <summary>The network a building belongs to, or null if it has no part in power.</summary>
    public PowerNetwork? NetworkOf(PlacedBuilding b)
    {
        Refresh();
        return _byBuilding.GetValueOrDefault(b.Id);
    }

    /// <summary>The network the cable on this cell feeds, or null if there is none or its run joins no power building.</summary>
    public PowerNetwork? NetworkAtCable(int x, int y)
    {
        Refresh();
        return grid.Has(BuildingKind.Wire, x, y) && _cablePart is { } cells && cells[y * grid.Width + x] is >= 0 and var p ? PartNet[p] : null;
    }

    /// <summary>
    /// Permille of its demand a building gets (FB-005): 1000 for buildings that draw nothing, its network's
    /// satisfaction otherwise.
    /// </summary>
    public int Satisfaction(PlacedBuilding b) =>
        b.Def.Power <= 0 ? 1000 : NetworkOf(b)?.Satisfaction ?? 0;

    /// <summary>Brings the networks up to date with every tile's layout.</summary>
    internal void Refresh() => (_world ??= new PowerWorld(this)).Refresh();

    internal bool TryPart(PlacedBuilding b, out int part) => _part.TryGetValue(b.Id, out part);

    /// <summary>Recomputes machine boosts and this tile's parts. The world joins them into networks afterwards.</summary>
    internal void RebuildParts(Simulation? sim = null)
    {
        Dirty = false;
        _networks.Clear();
        _byBuilding.Clear();
        // Boosts first: a machine's draw depends on its power boosters.
        foreach (var m in grid.Machines)
            m.Machine!.Boost = sim == null ? Boosters.Stats(grid, m, 0) : Factions.WithBuffs(sim, m, Boosters.Stats(grid, m, 0));

        var parent = new List<int>();
        int Find(int i)
        {
            while (parent[i] != i)
                i = parent[i] = parent[parent[i]];
            return i;
        }
        void Union(int a, int b)
        {
            a = Find(a);
            b = Find(b);
            if (a != b)
                parent[Math.Max(a, b)] = Math.Min(a, b);
        }

        // Cable runs: cables beside each other join.
        int w = grid.Width;
        var cables = grid.CountOn(BuildingKind.Wire) > 0 ? new int[w * grid.Height] : null;
        if (cables != null)
        {
            Array.Fill(cables, -1);
            var stack = new Stack<int>();
            for (int i = 0; i < cables.Length; i++)
            {
                if (cables[i] >= 0 || !grid.Has(BuildingKind.Wire, i % w, i / w))
                    continue;
                int run = parent.Count;
                parent.Add(run);
                cables[i] = run;
                stack.Push(i);
                while (stack.Count > 0)
                {
                    int c = stack.Pop();
                    foreach (var d in Directions.All)
                    {
                        int nx = c % w + d.Dx(), ny = c / w + d.Dy();
                        if (grid.Has(BuildingKind.Wire, nx, ny) && cables[ny * w + nx] < 0)
                        {
                            cables[ny * w + nx] = run;
                            stack.Push(ny * w + nx);
                        }
                    }
                }
            }
        }

        // Conductors: power buildings, and ports (which carry power to a linked partner tile).
        var node = new Dictionary<int, int>();
        var conductors = grid.Buildings.Where(Conducts).ToList();
        foreach (var b in conductors)
        {
            node[b.Id] = parent.Count;
            parent.Add(parent.Count);
        }
        foreach (var b in conductors)
        {
            int me = node[b.Id];
            // A cable under the footprint or beside an edge (not a corner) joins it.
            for (int y = b.Y - 1; cables != null && y <= b.Y + b.Height; y++)
            {
                for (int x = b.X - 1; x <= b.X + b.Width; x++)
                {
                    bool corner = (x < b.X || x >= b.X + b.Width) && (y < b.Y || y >= b.Y + b.Height);
                    if (!corner && grid.Has(BuildingKind.Wire, x, y))
                        Union(me, cables[y * w + x]);
                }
            }
            foreach (var n in grid.Touching(b))
            {
                if (node.TryGetValue(n.Id, out int other))
                    Union(me, other);
            }
            if (b.Kind == BuildingKind.PowerNode)
            {
                foreach (var t in InRadius(grid, b))
                    Union(me, node[t.Id]);
            }
        }

        // Parts numbered by their first conductor in id order; cable runs touching no conductor belong to none.
        _part.Clear();
        var partOf = new Dictionary<int, int>();
        foreach (var b in conductors)
        {
            int root = Find(node[b.Id]);
            if (!partOf.TryGetValue(root, out int part))
                partOf[root] = part = partOf.Count;
            _part[b.Id] = part;
        }
        PartCount = partOf.Count;
        for (int i = 0; cables != null && i < cables.Length; i++)
        {
            if (cables[i] >= 0)
                cables[i] = partOf.TryGetValue(Find(cables[i]), out int part) ? part : -1;
        }
        _cablePart = cables;
    }

    static bool Conducts(PlacedBuilding b) => b.IsPower || b.Kind is BuildingKind.ExportPort or BuildingKind.ImportPort;

    /// <summary>
    /// Generators, batteries and nodes a power node reaches (FB-020), in id order: the nearest cells of the two
    /// footprints at most <see cref="BuildingDef.PowerRadius"/> apart, straight-line. Consumers join only by touching or
    /// cable. The build tool shows the same set while placing.
    /// </summary>
    public static List<PlacedBuilding> InRadius(TileGrid grid, PlacedBuilding node) =>
        InRadius(grid, node.X, node.Y, node.Width, node.Height, node.Def.PowerRadius, node);

    /// <inheritdoc cref="InRadius(TileGrid, PlacedBuilding)"/>
    public static List<PlacedBuilding> InRadius(TileGrid grid, int x0, int y0, int w, int h, int r, PlacedBuilding? self = null)
    {
        var found = new List<PlacedBuilding>();
        for (int y = y0 - r; y < y0 + h + r; y++)
        {
            for (int x = x0 - r; x < x0 + w + r; x++)
            {
                if (grid.BuildingAt(x, y) is not { } t || t == self || found.Contains(t) || !Joinable(t))
                    continue;
                long dx = Math.Max(0, Math.Max(t.X - (x0 + w - 1), x0 - (t.X + t.Width - 1)));
                long dy = Math.Max(0, Math.Max(t.Y - (y0 + h - 1), y0 - (t.Y + t.Height - 1)));
                if (dx * dx + dy * dy <= (long)r * r)
                    found.Add(t);
            }
        }
        found.Sort((p, q) => p.Id.CompareTo(q.Id));
        return found;
    }

    /// <summary>What a power node joins by radius: generators (anything that makes power), batteries and other nodes.</summary>
    public static bool Joinable(PlacedBuilding b) =>
        b.IsPower && (b.Def.PowerSupply > 0 || b.Kind is BuildingKind.Battery or BuildingKind.PowerNode);

    internal void Clear()
    {
        _networks.Clear();
        _byBuilding.Clear();
        PartNet = new PowerNetwork?[PartCount];
    }

    internal void Assign(PlacedBuilding b, PowerNetwork net) => _byBuilding[b.Id] = net;

    internal void Collect()
    {
        foreach (var net in PartNet)
        {
            if (net != null && !_networks.Contains(net))
                _networks.Add(net);
        }
        _networks.Sort((p, q) => p.Id.CompareTo(q.Id));
    }
}

/// <summary>
/// Joins every tile's power parts into networks (FB-020). Parts on one tile are joined already; a linked port pair (an
/// export port in the partner's zone and an import port on the partner in this tile's zone, whoever owns either) joins
/// the parts the two ports belong to, with no limit on what it carries. Rebuilt whenever any tile's layout changes.
/// </summary>
public sealed class PowerWorld
{
    readonly Simulation? _sim;
    readonly TilePower? _alone;
    int[] _seen = [];
    bool _refreshing;
    readonly List<PowerNetwork> _networks = [];

    internal PowerWorld(Simulation sim) => _sim = sim;

    /// <summary>For a grid outside any simulation: its networks stay on it.</summary>
    internal PowerWorld(TilePower alone) => _alone = alone;

    /// <summary>Every network of the world, in id order.</summary>
    public IReadOnlyList<PowerNetwork> Networks
    {
        get
        {
            Refresh();
            return _networks;
        }
    }

    TilePower PowerOf(int i) => _sim != null ? _sim.Tiles[i].Grid.Power : _alone!;

    internal void Refresh()
    {
        // Rebuilding parts works out boosts, which read satisfaction: those reads see the networks as they are.
        if (_refreshing)
            return;
        int count = _sim?.Tiles.Count ?? 1;
        if (_seen.Length != count)
        {
            _seen = new int[count];
            Array.Fill(_seen, int.MinValue);
        }
        bool changed = false;
        _refreshing = true;
        try
        {
            for (int i = 0; i < count; i++)
            {
                var power = PowerOf(i);
                if (power.Grid.Revision == _seen[i] && !power.Dirty)
                    continue;
                power.RebuildParts(_sim);
                _seen[i] = power.Grid.Revision;
                changed = true;
            }
            if (changed)
                Join(count);
        }
        finally
        {
            _refreshing = false;
        }
    }

    void Join(int count)
    {
        var first = new int[count + 1];
        for (int i = 0; i < count; i++)
            first[i + 1] = first[i] + PowerOf(i).PartCount;
        var parent = new int[first[count]];
        for (int i = 0; i < parent.Length; i++)
            parent[i] = i;
        int Find(int i)
        {
            while (parent[i] != i)
                i = parent[i] = parent[parent[i]];
            return i;
        }

        // Port links. In a simulation, tile ids are indices into its tile list.
        for (int a = 0; _sim != null && a < count; a++)
        {
            var from = _sim.Tiles[a];
            foreach (var export in from.Grid.ExportPorts)
            {
                if (!from.Grid.Power.TryPart(export, out int pa) || _sim.TryGetTile(PortRules.PartnerOf(from.Grid, export)) is not { } to)
                    continue;
                foreach (var import in to.Grid.ImportPorts)
                {
                    if (PortRules.PartnerOf(to.Grid, import) != from.Id || !to.Grid.Power.TryPart(import, out int pb))
                        continue;
                    int x = Find(first[a] + pa), y = Find(first[to.Id] + pb);
                    if (x != y)
                        parent[Math.Max(x, y)] = Math.Min(x, y);
                }
            }
        }

        _networks.Clear();
        var netOf = new PowerNetwork?[parent.Length];
        for (int i = 0; i < count; i++)
        {
            var power = PowerOf(i);
            power.Clear();
            foreach (var b in power.Grid.Buildings)
            {
                if (!b.IsPower || !power.TryPart(b, out int part))
                    continue;
                int root = Find(first[i] + part);
                if (netOf[root] is not { } net)
                {
                    netOf[root] = net = new PowerNetwork(_networks.Count);
                    _networks.Add(net);
                }
                net.Add(power.TileId, b);
                power.Assign(b, net);
            }
        }
        for (int i = 0; i < count; i++)
        {
            var power = PowerOf(i);
            for (int k = 0; k < power.PartCount; k++)
                power.PartNet[k] = netOf[Find(first[i] + k)];
            power.Collect();
        }

        foreach (var net in _networks)
        {
            net.Batteries.AddRange(net.Members.Where(m => m.Battery != null));
            net.Capacity = net.Batteries.Sum(m => m.Battery!.Capacity);
            foreach (var m in net.Members)
            {
                // Only machines change their figures tick to tick, and they report them as they work.
                if (m is { Kind: BuildingKind.Machine, Machine: { } state })
                {
                    state.Net = net;
                    state.Draws = m.Def.Power > 0;
                    state.BoostedDraw = BoostStats.Scaled(m.Def.Power, state.Boost.Power);
                    state.MaxSupply = m.Def.PowerSupply;
                    state.ReportedDraw = state.ReportedSupply = 0;
                    net.Report(state);
                }
                else
                {
                    net.LiveSupply += PowerNet.SupplyOf(m);
                    net.LiveDemand += PowerNet.DrawOf(m);
                }
            }
        }
    }
}

/// <summary>
/// Power rules (FB-005, FB-020). Every building with a part in power (it draws, supplies, stores or is a node) joins
/// every such building it touches or shares a cable run with; a power node also joins the generators, batteries and
/// nodes within its radius, and a linked port pair joins the two tiles' parts; each connected group is a network. Each
/// tick a network adds up its supply (cores always, generators while burning) and its demand (machines while busy,
/// other consumers always, scaled by power boosters). A surplus charges batteries and a shortfall drains them, each
/// battery at most <see cref="BuildingDef.Discharge"/> a tick, in member order. Whatever is still short slows every
/// consumer on the network by the same share.
/// </summary>
public static class PowerNet
{
    /// <summary>Networks with members on the tile, with this tick's balance, for the HUD (UI-002).</summary>
    public static IReadOnlyList<PowerNetwork> Networks(TileState tile) => tile.Grid.Power.Networks;

    /// <summary>Permille of its demand <paramref name="b"/> gets this tick: 1000 if it draws nothing.</summary>
    public static int Satisfaction(TileState tile, PlacedBuilding b) => tile.Grid.Power.Satisfaction(b);

    /// <summary>The building gets some power this tick (or needs none).</summary>
    public static bool IsPowered(TileState tile, PlacedBuilding b) => Satisfaction(tile, b) > 0;

    /// <summary>
    /// Power a block can provide on a unit, where it runs on its own (UD-003): a generator's supply plus a
    /// battery's discharge rate. Compare the sum with the sum of <see cref="BuildingDef.Power"/>.
    /// </summary>
    public static int OnboardSupply(BuildingDef def) => def.PowerSupply + def.Discharge;

    /// <summary>
    /// A machine's recipe speed relative to base, in permille: booster speed times power satisfaction. 1000 = one
    /// batch per <c>Recipe.Ticks</c>. Its output per batch is <c>100 + Boost.Output</c> percent of the recipe's.
    /// For rate mode (FB-012, FB-014); valid after this tick's <see cref="Step"/>.
    /// </summary>
    public static int SpeedPermille(PlacedBuilding b) => b.Machine is { } m
        ? (int)((long)(100 + m.Boost.Speed) * m.PowerPermille / 100)
        : 0;

    /// <summary>
    /// The power a building asks for this tick, after power boosters. A machine asks while it is working or was
    /// working last tick (so one finishing a batch and starting the next keeps its share); a starved or blocked
    /// one asks for nothing.
    /// </summary>
    public static int DrawOf(PlacedBuilding b) => b.Machine is { } m
        ? m.Working || m.IdleTicks == 0 ? BoostStats.Scaled(b.Def.Power, m.Boost.Power) : 0
        : b.Def.Power;

    /// <summary>The power a building makes this tick.</summary>
    public static int SupplyOf(PlacedBuilding b) => b.Machine is { } m ? (m.Working ? b.Def.PowerSupply : 0) : b.Def.PowerSupply;

    /// <summary>
    /// Balances every network on the tile for this tick, unless another tile it spans did already this tick. Runs before
    /// the tile's machines work.
    /// </summary>
    internal static void Step(Simulation sim, TileState tile)
    {
        foreach (var net in tile.Grid.Power.Networks)
        {
            if (net.BalancedTick == sim.Tick)
                continue;
            net.BalancedTick = sim.Tick;
            long supply = net.LiveSupply, demand = net.LiveDemand;

            long flow = 0;
            if (supply >= demand)
            {
                long spare = supply - demand;
                foreach (var b in net.Batteries)
                {
                    if (spare == 0)
                        break;
                    var bat = b.Battery!;
                    long put = Math.Min(Math.Min(spare, b.Def.Discharge), bat.Capacity - bat.Charge);
                    bat.Charge += put;
                    spare -= put;
                    flow -= put;
                }
            }
            else
            {
                long need = demand - supply;
                foreach (var b in net.Batteries)
                {
                    if (need == 0)
                        break;
                    var bat = b.Battery!;
                    long take = Math.Min(Math.Min(need, b.Def.Discharge), bat.Charge);
                    bat.Charge -= take;
                    need -= take;
                    flow += take;
                }
            }

            net.Supply = supply;
            net.Demand = demand;
            net.FromBatteries = flow;
            long stored = 0;
            foreach (var b in net.Batteries)
                stored += b.Battery!.Charge;
            net.Stored = stored;
            net.Satisfaction = supply >= demand ? 1000 : (int)((supply + flow) * 1000 / demand);
        }
    }
}
