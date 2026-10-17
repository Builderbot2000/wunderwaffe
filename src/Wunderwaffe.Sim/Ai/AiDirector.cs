using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim.Ai;

/// <summary>
/// Runs the match's AI seats: <see cref="GameSetup.AiPlayers"/> (the last player ids) from the start, plus any seat
/// handed over with <see cref="TakeOver"/> (a leaver, OQ-46). The host calls <see cref="Collect"/> before each step and
/// enqueues the result; in multiplayer only the host does, and sends the commands as its own input. Replays never run
/// it: the AI's commands are already in the log. Seats are kept in player order, so their commands are too.
/// </summary>
public sealed class AiDirector
{
    readonly SortedDictionary<int, AiPlayer> _players = [];

    public AiDirector(GameSetup setup, AiConfig? config = null)
    {
        for (int p = 0; p < setup.Players; p++)
        {
            if (setup.IsAi(p))
                _players[p] = new AiPlayer(p, config);
        }
    }

    public IReadOnlyList<AiPlayer> Players => [.. _players.Values];

    /// <summary>The AI on <paramref name="seat"/>, or null if a person plays it.</summary>
    public AiPlayer? For(int seat) => _players.GetValueOrDefault(seat);

    /// <summary>
    /// An AI takes over <paramref name="seat"/> from now (MP-003, OQ-46): it initialises from the current state and
    /// thinks from its next due tick. No-op if the seat already has one.
    /// </summary>
    public AiPlayer TakeOver(int seat, Simulation sim, AiConfig? config = null)
    {
        if (!_players.TryGetValue(seat, out var ai))
        {
            _players[seat] = ai = new AiPlayer(seat, config);
            ai.Init(sim);
        }
        return ai;
    }

    /// <summary>
    /// The AI commands for the <paramref name="ticks"/> ticks about to run from <see cref="Simulation.Tick"/>, seat by
    /// seat: every AI due on one of them thinks now. Lockstep asks once per turn, single player once per tick.
    /// </summary>
    public IReadOnlyList<Command> Collect(Simulation sim, int ticks = 1)
    {
        var commands = new List<Command>();
        foreach (var ai in _players.Values)
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
