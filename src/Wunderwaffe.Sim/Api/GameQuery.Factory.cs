using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Api;

/// <summary>Factory queries (silo B): power networks, boosters, sorter filters and production statistics.</summary>
public sealed partial class GameQuery
{
    /// <summary>A tile's power networks with this tick's balance (FB-005), in id order.</summary>
    public IReadOnlyList<PowerView> PowerNetworks(int tileId) =>
        Sim.TryGetTile(tileId) is { } t
            ? PowerNet.Networks(t).Select(n => new PowerView(n.Id, n.Members.Select(m => m.Id).ToList(), n.Supply, n.Demand, n.Stored, n.Capacity, n.Satisfaction)).ToList()
            : [];

    /// <summary>Permille of its demand a building gets: 1000 if it draws nothing, 0 if it is on no network.</summary>
    public int PowerSatisfaction(int tileId, int buildingId) =>
        Sim.TryGetTile(tileId) is { } t && t.Grid.BuildingById(buildingId) is { } b ? PowerNet.Satisfaction(t, b) : 0;

    /// <summary>A building's boosted stats from the boosters touching it (FB-016).</summary>
    public BoostStats Boost(int tileId, int buildingId) =>
        Sim.TryGetTile(tileId) is { } t && t.Grid.BuildingById(buildingId) is { } b ? Boosters.StatsOf(Sim, t, b) : BoostStats.Base;

    /// <summary>What <see cref="Core.SetFilterCommand"/> would do (FB-004).</summary>
    public FilterResult CheckFilter(int tileId, int x, int y, string? itemId) =>
        Sim.TryGetTile(tileId) is { } t ? Logistics.CheckFilter(Sim, t, Player, x, y, itemId) : FilterResult.NoSorter;

    /// <summary>A tile's production over the last minute (FB-011): made and used per item, sent per partner tile.</summary>
    public ProductionView? Production(int tileId)
    {
        if (Sim.TryGetTile(tileId) is not { } t)
            return null;
        var s = t.Stats;
        return new ProductionView(
            s.Items.Select(i => new ItemRate(i, s.MadePerMinute(i), s.UsedPerMinute(i))).ToList(),
            s.Partners.Select(p => new PartnerRate(p, s.SentPerMinute(p))).ToList());
    }
}
