using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim;

/// <summary>Match outcome state (MP-001), set by <see cref="Victory.Step"/>.</summary>
public sealed partial class Simulation
{
    /// <summary>The player who won, or -1: no winner yet, or a draw once <see cref="IsOver"/>.</summary>
    public int Winner { get; internal set; } = -1;

    /// <summary>The tick the match was decided on, or -1 while it runs. The sim keeps running after it.</summary>
    public long EndTick { get; internal set; } = -1;

    /// <summary>Why the match ended, once <see cref="IsOver"/>.</summary>
    public VictoryReason EndReason { get; internal set; }

    public bool IsOver => EndTick >= 0;
}
