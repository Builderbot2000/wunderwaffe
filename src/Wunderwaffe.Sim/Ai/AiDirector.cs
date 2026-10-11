using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim.Ai;

/// <summary>
/// Runs the match's AI seats (<see cref="GameSetup.AiPlayers"/>, the last player ids). The host calls
/// <see cref="Collect"/> before each step and enqueues the result; in multiplayer only the host does, and sends the
/// commands as its own input. Replays never run it: the AI's commands are already in the log.
/// </summary>
public sealed class AiDirector
{
    readonly List<AiPlayer> _players = [];

    public AiDirector(GameSetup setup)
    {
        for (int p = 0; p < setup.Players; p++)
        {
            if (setup.IsAi(p))
                _players.Add(new AiPlayer(p));
        }
    }

    public IReadOnlyList<AiPlayer> Players => _players;

    /// <summary>
    /// The AI commands for the <paramref name="ticks"/> ticks about to run from <see cref="Simulation.Tick"/>, seat by
    /// seat: every AI due on one of them thinks now. Lockstep asks once per turn, single player once per tick.
    /// </summary>
    public IReadOnlyList<Command> Collect(Simulation sim, int ticks = 1)
    {
        var commands = new List<Command>();
        foreach (var ai in _players)
        {
            if (ai.DueWithin(sim.Tick, ticks))
                commands.AddRange(ai.Think(sim));
        }
        return commands;
    }

    /// <summary>Enqueues <see cref="Collect"/>'s commands: the single-player host loop.</summary>
    public void BeforeStep(Simulation sim)
    {
        foreach (var command in Collect(sim))
            sim.Enqueue(command);
    }
}
