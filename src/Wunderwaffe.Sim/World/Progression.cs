using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// Resource-gated tech (TT-001, TT-002, TT-004) and the guided goal chain. Read-only checks are public so the UI shows
/// the same progress and locks the sim applies.
/// </summary>
public static class Progression
{
    /// <summary>How far <paramref name="player"/> is toward <paramref name="condition"/>'s amount.</summary>
    public static long Progress(Simulation sim, int player, UnlockCondition condition)
    {
        var p = sim.PlayerOrNull(player);
        return condition.Type switch
        {
            "produce" => p?.ProducedOf(condition.Item!) ?? 0,
            "claim" => sim.Tiles.Count(t => t.Owner == player),
            "ship" => p?.Shipped ?? 0,
            "launch" => p?.Launched ?? 0,
            _ => 0,
        };
    }

    public static bool IsMet(Simulation sim, int player, UnlockCondition condition) =>
        Progress(sim, player, condition) >= condition.Amount;

    /// <summary>
    /// True if <paramref name="player"/> may use a building or recipe: it is starter content or its tech node is
    /// complete.
    /// </summary>
    public static bool IsUnlocked(Simulation sim, int player, string contentId) =>
        !sim.Content.UnlockedBy.TryGetValue(contentId, out var tech) || sim.PlayerOrNull(player)?.HasTech(tech) == true;

    /// <summary>The tech node that unlocks <paramref name="contentId"/>, or null for starter content.</summary>
    public static TechDef? UnlockingTech(ContentDb content, string contentId) =>
        content.UnlockedBy.TryGetValue(contentId, out var tech) ? content.Tech[tech] : null;

    /// <summary>The goal <paramref name="player"/> is working on, or null once the chain is done.</summary>
    public static GoalDef? CurrentGoal(Simulation sim, int player)
    {
        int done = sim.PlayerOrNull(player)?.GoalsDone ?? 0;
        return done < sim.Content.Goals.Count ? sim.Content.Goals[done] : null;
    }

    /// <summary>Completes any tech node or goal whose condition is now met. Runs at the end of each tick.</summary>
    internal static void Step(Simulation sim)
    {
        var goals = sim.Content.Goals;
        foreach (var p in sim.Players)
        {
            foreach (var tech in sim.Content.TechList)
            {
                if (!p.HasTech(tech.Id) && IsMet(sim, p.Id, tech.Condition))
                    p.Unlock(tech.Id, sim.Tick);
            }
            while (p.GoalsDone < goals.Count && IsMet(sim, p.Id, goals[p.GoalsDone].Condition))
                p.CompleteGoal(sim.Tick);
        }
    }
}
