using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// The guided goal chain. There is no tech tree (TT-001): every building and recipe is available from the start.
/// Read-only checks are public so the UI shows the same progress the sim applies.
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

    /// <summary>The goal <paramref name="player"/> is working on, or null once the chain is done.</summary>
    public static GoalDef? CurrentGoal(Simulation sim, int player)
    {
        int done = sim.PlayerOrNull(player)?.GoalsDone ?? 0;
        return done < sim.Content.Goals.Count ? sim.Content.Goals[done] : null;
    }

    /// <summary>Completes any goal whose condition is now met. Runs at the end of each tick.</summary>
    internal static void Step(Simulation sim)
    {
        var goals = sim.Content.Goals;
        foreach (var p in sim.Players)
        {
            while (p.GoalsDone < goals.Count && IsMet(sim, p.Id, goals[p.GoalsDone].Condition))
            {
                p.CompleteGoal(sim.Tick);
                sim.Raise(GameEventKind.GoalCompleted, p.Id, itemId: goals[p.GoalsDone - 1].Id);
            }
        }
    }
}
