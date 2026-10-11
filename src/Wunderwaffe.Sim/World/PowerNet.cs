using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// One power network (FB-005): power buildings joined by touching. Its members come from the layout. The
/// per-tick figures are worked out at the start of the tile's step, from hashed state (machines working, battery
/// charge), so they need no hashing of their own.
/// </summary>
public sealed class PowerNetwork(int id)
{
    /// <summary>Index in <see cref="TilePower.Networks"/>; networks are numbered by their lowest building id.</summary>
    public int Id { get; } = id;

    /// <summary>Every member, in id order.</summary>
    public List<PlacedBuilding> Members { get; } = [];

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
/// A tile's power networks and its machines' boosted stats, rebuilt whenever the tile's layout changes
/// (<see cref="TileGrid.Revision"/>). Networks are numbered by their lowest building id, so the order is
/// deterministic.
/// </summary>
public sealed class TilePower
{
    int _revision = -1;
    readonly List<PowerNetwork> _networks = [];
    readonly Dictionary<int, PowerNetwork> _byBuilding = [];

    /// <summary>In order of their lowest building id.</summary>
    public IReadOnlyList<PowerNetwork> Networks => _networks;

    /// <summary>The network a building belongs to, or null if it has no part in power.</summary>
    public PowerNetwork? NetworkOf(PlacedBuilding b) => _byBuilding.GetValueOrDefault(b.Id);

    /// <summary>
    /// Permille of its demand a building gets (FB-005): 1000 for buildings that draw nothing, its network's
    /// satisfaction otherwise.
    /// </summary>
    public int Satisfaction(PlacedBuilding b) =>
        b.Def.Power <= 0 ? 1000 : NetworkOf(b)?.Satisfaction ?? 0;

    /// <summary>Rebuilds networks and machine boosts if the layout changed since last time.</summary>
    internal void Refresh(TileGrid grid)
    {
        if (grid.Revision == _revision)
            return;
        _revision = grid.Revision;
        _networks.Clear();
        _byBuilding.Clear();

        foreach (var start in grid.Buildings)
        {
            if (!start.IsPower || _byBuilding.ContainsKey(start.Id))
                continue;
            var net = new PowerNetwork(_networks.Count);
            _networks.Add(net);
            var stack = new Stack<PlacedBuilding>();
            stack.Push(start);
            _byBuilding[start.Id] = net;
            while (stack.Count > 0)
            {
                var b = stack.Pop();
                net.Members.Add(b);
                foreach (var n in grid.Touching(b))
                {
                    if (n.IsPower && _byBuilding.TryAdd(n.Id, net))
                        stack.Push(n);
                }
            }
            net.Members.Sort((p, q) => p.Id.CompareTo(q.Id));
            net.Capacity = net.Members.Sum(m => m.Battery?.Capacity ?? 0);
        }

        foreach (var m in grid.Machines)
            m.Machine!.Boost = Boosters.Stats(grid, m, 0);
    }
}

/// <summary>
/// Power rules (FB-005). Every building with a part in power (it draws, supplies, stores or is a wire) joins every
/// such building it touches; each connected group is a network. Each tick a network adds up its supply (cores
/// always, generators while burning) and its demand (machines while working, other consumers always, scaled by
/// power boosters). A surplus charges batteries and a shortfall drains them, each battery at most
/// <see cref="BuildingDef.Discharge"/> a tick, lowest id first. Whatever is still short slows every consumer on the
/// network by the same share.
/// </summary>
public static class PowerNet
{
    /// <summary>The tile's networks, with this tick's balance, for the HUD (UI-002).</summary>
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

    /// <summary>The power a building asks for this tick, after power boosters.</summary>
    public static int DrawOf(PlacedBuilding b) => b.Machine is { } m
        ? m.Working ? BoostStats.Scaled(b.Def.Power, m.Boost.Power) : 0
        : b.Def.Power;

    /// <summary>The power a building makes this tick.</summary>
    public static int SupplyOf(PlacedBuilding b) => b.Machine is { } m ? (m.Working ? b.Def.PowerSupply : 0) : b.Def.PowerSupply;

    /// <summary>Balances every network on the tile for this tick. Runs before machines work.</summary>
    internal static void Step(TileGrid grid)
    {
        grid.Power.Refresh(grid);
        foreach (var net in grid.Power.Networks)
        {
            long supply = 0, demand = 0;
            foreach (var b in net.Members)
            {
                supply += SupplyOf(b);
                demand += DrawOf(b);
            }

            long flow = 0;
            if (supply >= demand)
            {
                long spare = supply - demand;
                foreach (var b in net.Members)
                {
                    if (spare == 0)
                        break;
                    if (b.Battery is not { } bat)
                        continue;
                    long put = Math.Min(Math.Min(spare, b.Def.Discharge), bat.Capacity - bat.Charge);
                    bat.Charge += put;
                    spare -= put;
                    flow -= put;
                }
            }
            else
            {
                long need = demand - supply;
                foreach (var b in net.Members)
                {
                    if (need == 0)
                        break;
                    if (b.Battery is not { } bat)
                        continue;
                    long take = Math.Min(Math.Min(need, b.Def.Discharge), bat.Charge);
                    bat.Charge -= take;
                    need -= take;
                    flow += take;
                }
            }

            net.Supply = supply;
            net.Demand = demand;
            net.FromBatteries = flow;
            net.Stored = net.Members.Sum(b => b.Battery?.Charge ?? 0);
            net.Satisfaction = supply >= demand ? 1000 : (int)((supply + flow) * 1000 / demand);
            foreach (var b in net.Members)
            {
                if (b.Machine is { } m && b.Def.Power > 0)
                    m.PowerPermille = net.Satisfaction;
            }
        }
    }
}
