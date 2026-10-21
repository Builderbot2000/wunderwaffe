using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.World;

public enum FactionResult
{
    Ok,
    NoSuchBuilding,

    /// <summary>Not in the seat's faction subset (MP-009): another faction's, or one it lacks.</summary>
    NotInFaction,

    /// <summary>Capture to build (MP-010): the seat has never owned one it didn't build.</summary>
    NotCaptured,
}

/// <summary>
/// Faction rules in play (MP-009, MP-010): what a seat may build, its capture record and its buffs. Read-only checks
/// shared by placement, blueprints, factories, the build menu, <c>GameQuery</c> and the AI's roles.
/// </summary>
public static class Factions
{
    /// <summary>Whether <paramref name="player"/>'s faction lets them build <paramref name="defId"/> now.</summary>
    public static FactionResult Check(Simulation sim, int player, string defId)
    {
        if (!sim.Content.Buildings.TryGetValue(defId, out var def))
            return FactionResult.NoSuchBuilding;
        var f = sim.FactionOf(player);
        if (!f.Has(defId))
            return FactionResult.NotInFaction;
        if (f.CaptureGated(def) && !HasAcquired(sim, player, defId))
            return FactionResult.NotCaptured;
        return FactionResult.Ok;
    }

    public static bool CanBuild(Simulation sim, int player, BuildingDef def) => Check(sim, player, def.Id) == FactionResult.Ok;

    /// <summary>The filter roles and planners search with (MP-007): only what the seat can build now.</summary>
    public static Func<BuildingDef, bool> Buildable(Simulation sim, int player) => def => CanBuild(sim, player, def);

    /// <summary>Whether the seat has ever owned a <paramref name="defId"/> it didn't build (OQ-56).</summary>
    public static bool HasAcquired(Simulation sim, int player, string defId) =>
        sim.PlayerOrNull(player)?.Acquired.Contains(defId) ?? false;

    /// <summary>
    /// Records that <paramref name="player"/> now owns a <paramref name="defId"/> they didn't build: under capture to
    /// build that exact building is theirs to build for good. Call it wherever a building changes hands (claims of
    /// unclaimed buildings, WM-019; moving one, FB-023).
    /// </summary>
    internal static void Acquired(Simulation sim, int player, string defId)
    {
        if (player >= 0)
            sim.Player(player).Acquire(defId);
    }

    /// <summary>The owner's faction buffs to one stat of a building (percent, added like a booster's).</summary>
    public static int Buff(Simulation sim, int player, BuildingDef def, string stat) =>
        player < 0 ? 0 : sim.FactionOf(player).BuffPercent(def, stat);

    /// <summary>A building's booster stats with its owner's faction buffs added.</summary>
    internal static BoostStats WithBuffs(Simulation sim, PlacedBuilding b, BoostStats stats)
    {
        foreach (var stat in FactionRule.BuffStats)
        {
            if (Buff(sim, b.Owner, b.Def, stat) is var n and not 0)
                stats = stats.Add(stat, n);
        }
        return stats;
    }

    /// <summary>
    /// A shared blueprint as <paramref name="player"/>'s faction builds it (OQ-55): every block mapped through
    /// <see cref="FactionDef.Map"/>, a crafting recipe moved to the variant's recipe with the same outputs.
    /// Blocks the faction lacks stay as they are, for the checks to refuse.
    /// </summary>
    public static Blueprint Substitute(Simulation sim, int player, Blueprint bp)
    {
        var f = sim.FactionOf(player);
        if (!bp.Blocks.Any(b => f.Map(b.DefId) != b.DefId))
            return bp;
        var content = sim.Content;
        return bp with
        {
            Blocks = [.. bp.Blocks.Select(b => f.Map(b.DefId) is var id && id == b.DefId ? b : b with
            {
                DefId = id,
                RecipeId = b.RecipeId is { } r && content.Recipes.TryGetValue(r, out var old)
                    ? content.RecipesByBuilding.GetValueOrDefault(id)?
                        .FirstOrDefault(n => n.Outputs.Keys.Order(StringComparer.Ordinal).SequenceEqual(old.Outputs.Keys.Order(StringComparer.Ordinal)))?.Id
                    : null,
            })],
        };
    }
}
