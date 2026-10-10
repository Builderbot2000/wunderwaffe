using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// Unit upkeep (UD-013). A unit's upkeep is the sum of its blocks' <c>upkeep</c> (items a minute), paid all at once
/// every <see cref="Balance.UpkeepPeriod"/> ticks from its supplier. A unit that can't pay is
/// <see cref="Unit.Unsupplied"/>: its engines stall, so it drops its move order and takes no new one, until a payment
/// succeeds. While unsupplied it retries every second. Units whose blocks declare no upkeep never pay or stall.
/// </summary>
internal static class UnitUpkeep
{
    public static void Step(Simulation sim)
    {
        foreach (var u in sim.Units)
        {
            if (u.Unsupplied && u.Route != null)
                u.Route = null;
            if (sim.Tick < u.UpkeepDue)
                continue;

            var upkeep = UnitStats.Of(sim.Content, u).Upkeep;
            bool paid = upkeep.Count == 0 || (Supplier(sim, u)?.TryTake(upkeep) ?? false);
            u.Unsupplied = !paid;
            u.UpkeepDue = sim.Tick + (paid ? Balance.UpkeepPeriod : Simulation.TicksPerSecond);
        }
    }

    /// <summary>
    /// Where the unit pays from. Seam until combat's resource radius (FB-018) lands: then onboard storage first
    /// (<c>Unit.Cargo</c>), else <c>ResourceRadius.SupplierFor(sim, unit)</c>. For now the inventory of a tile its owner
    /// holds and it is over stands in for the core's radius; elsewhere it can't pay.
    /// </summary>
    static Inventory? Supplier(Simulation sim, Unit u) =>
        sim.Tiles[u.TileId] is { } tile && tile.Owner == u.Owner ? tile.Inventory : null;
}
