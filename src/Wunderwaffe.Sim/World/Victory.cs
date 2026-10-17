using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim.World;

public enum VictoryReason
{
    None,

    /// <summary>The winner held <see cref="GameSetup.VictoryPercent"/> of all tiles.</summary>
    Domination,

    /// <summary>Every other player was eliminated.</summary>
    LastStanding,

    /// <summary>Every player was eliminated on the same check: nobody wins.</summary>
    Draw,
}

/// <summary>
/// Domination victory and elimination (MP-001). A player is eliminated when they own no core (WM-016); a player wins
/// by owning <see cref="GameSetup.VictoryPercent"/> of all tiles, or by being the last one left in a match that started
/// with opponents. Checked twice a second, after goals; only matches from <see cref="Simulation.NewGame"/> (with a
/// <see cref="Simulation.Setup"/>) can end.
/// </summary>
public static class Victory
{
    /// <summary>Ticks between checks; scanning for cores touches every building.</summary>
    public const int CheckEvery = Simulation.TicksPerSecond / 2;

    /// <summary>
    /// True if the player has a core anywhere, standing on a tile or carried by a unit (every unit has one). Losing the
    /// last one eliminates them (WM-016).
    /// </summary>
    public static bool HasCore(Simulation sim, int player) => sim.CoreCount(player) > 0;

    /// <summary>Tiles a player needs to own to win by domination, or 0 outside a set-up match.</summary>
    public static int TilesToWin(Simulation sim) =>
        sim.Setup is { } setup ? Math.Max(1, (sim.Tiles.Count * setup.VictoryPercent + 99) / 100) : 0;

    public static bool IsEliminated(Simulation sim, int player) => sim.PlayerOrNull(player)?.EliminatedTick >= 0;

    internal static void Step(Simulation sim)
    {
        if (sim.IsOver || sim.Setup is not { } setup || sim.Tick % CheckEvery != 0)
            return;

        var alive = new List<int>();
        for (int p = 0; p < setup.Players; p++)
        {
            var state = sim.Player(p);
            if (state.EliminatedTick >= 0)
                continue;
            if (HasCore(sim, p))
            {
                alive.Add(p);
                continue;
            }
            state.EliminatedTick = sim.Tick;
            sim.Raise(GameEventKind.PlayerEliminated, p);
        }

        int need = TilesToWin(sim);
        var owned = new int[setup.Players];
        foreach (var t in sim.Tiles)
        {
            if (t.Owner >= 0 && t.Owner < owned.Length)
                owned[t.Owner]++;
        }
        foreach (int p in alive)
        {
            if (owned[p] >= need)
            {
                End(sim, p, VictoryReason.Domination);
                return;
            }
        }
        if (setup.Players > 1 && alive.Count == 1)
            End(sim, alive[0], VictoryReason.LastStanding);
        else if (alive.Count == 0)
            End(sim, -1, VictoryReason.Draw);
    }

    static void End(Simulation sim, int winner, VictoryReason reason)
    {
        sim.Winner = winner;
        sim.EndTick = sim.Tick;
        sim.EndReason = reason;
        if (winner >= 0)
            sim.Raise(GameEventKind.Victory, winner);
    }
}
