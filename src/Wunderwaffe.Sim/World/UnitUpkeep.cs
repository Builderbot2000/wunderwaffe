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
            bool paid = upkeep.Count == 0 || (Supplier(sim, u, upkeep)?.TryTake(upkeep) ?? false);
            u.Unsupplied = !paid;
            u.UpkeepDue = sim.Tick + (paid ? Balance.UpkeepPeriod : Simulation.TicksPerSecond);
        }
    }

    /// <summary>
    /// Where the unit pays from: its onboard storage (<see cref="Unit.Cargo"/>) when that holds the whole upkeep, else
    /// the friendly supplier whose resource radius covers it (FB-018, <see cref="ResourceRadius.SupplierFor"/>).
    /// </summary>
    static Inventory? Supplier(Simulation sim, Unit u, IReadOnlyDictionary<string, int> upkeep) =>
        u.Cargo.Has(upkeep) ? u.Cargo : ResourceRadius.SupplierFor(sim, u);
}
