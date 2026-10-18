using Wunderwaffe.Sim.Api;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Ai;

/// <summary>
/// A unit design the AI builds: a prefab's id and blueprint (frame cells plus blocks, one of them a core), and its total
/// cost (UD-002), where the economy's demand starts (MP-007).
/// </summary>
public sealed record AiDesign(string Name, Blueprint Blueprint, IReadOnlyDictionary<string, int> Cost);

/// <summary>
/// Where the AI's designs come from (MP-006 initialisation): the prefab library (FB-021) through
/// <see cref="GameQuery.Prefabs"/>. The seat builds the cheapest unit prefab that can move (UD-004), ties by id.
/// </summary>
public static class AiDesigns
{
    /// <summary>The unit design for a seat, or null if the content ships no unit prefab that moves.</summary>
    public static AiDesign? Pick(GameQuery q) =>
        q.Prefabs()
            .Where(p => p.IsUnit && q.BlueprintLocomotion(p.Blueprint).Result == LocomotionResult.Ok)
            .OrderBy(p => q.BlueprintStats(p.Blueprint).Cost.Values.Sum())
            .ThenBy(p => p.Id, StringComparer.Ordinal)
            .Select(p => new AiDesign(p.Id, p.Blueprint, q.BlueprintStats(p.Blueprint).Cost))
            .FirstOrDefault();
}
