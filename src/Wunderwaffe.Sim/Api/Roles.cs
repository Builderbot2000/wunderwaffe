using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.Api;

/// <summary>
/// Buildings and recipes by what they do, never by id (MP-007, NF-003): the planner and the AI find the conveyor, the
/// wire, the ports, the extractor for an item or the recipes that make it through these, so renaming or rebalancing
/// content needs no code change. Every choice is deterministic: cheapest total cost first, ties in
/// <see cref="ContentDb.BuildingList"/> order, recipes by id.
/// </summary>
public static class Roles
{
    /// <summary>The sum of a building's cost: the measure "cheapest" uses.</summary>
    public static int CostOf(BuildingDef def) => def.Cost.Values.Sum();

    /// <summary>The cheapest building of <paramref name="kind"/>, or null if the content has none.</summary>
    public static BuildingDef? Cheapest(ContentDb content, BuildingKind kind) =>
        content.BuildingList.Where(b => b.Kind == kind).OrderBy(CostOf).FirstOrDefault();

    /// <summary>
    /// The cheapest machine with an extraction recipe (no inputs) giving <paramref name="item"/>, and that recipe; the
    /// one placing it on a deposit of the item picks (<see cref="World.Placement"/>). Null if nothing extracts it.
    /// </summary>
    public static (BuildingDef Building, RecipeDef Recipe)? Extractor(ContentDb content, string item)
    {
        foreach (var b in content.BuildingList.Where(b => b.Kind == BuildingKind.Machine).OrderBy(CostOf))
        {
            if (content.RecipesByBuilding.GetValueOrDefault(b.Id)?.Where(r => r.Inputs.Count == 0 && r.Outputs.ContainsKey(item))
                    .OrderBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault() is { } r)
                return (b, r);
        }
        return null;
    }

    /// <summary>Recipes with inputs that make <paramref name="item"/> on a machine, by id.</summary>
    public static IEnumerable<RecipeDef> Crafting(ContentDb content, string item) =>
        content.Recipes.Values
            .Where(r => r.Inputs.Count > 0 && r.Outputs.ContainsKey(item)
                && content.Buildings.TryGetValue(r.Building, out var b) && b.Kind == BuildingKind.Machine)
            .OrderBy(r => r.Id, StringComparer.Ordinal);
}
