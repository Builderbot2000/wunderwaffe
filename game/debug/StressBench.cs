using System.Linq;
using Godot;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Debug;

/// <summary>Prints the average sim cost per tick over a run of ticks, then frees itself. A child of <see cref="SimHost"/>.</summary>
public partial class StressBench : Node
{
    const int BenchTicks = 600;

    /// <summary>The first tick measured: the one after the stress fill lands, so placing it isn't counted.</summary>
    public long StartTick { get; init; }

    double _startMs = -1;

    public override void _Process(double delta)
    {
        var host = SimHost.Instance;
        if (host.Sim.Tick < StartTick)
            return;
        if (_startMs < 0)
            _startMs = host.TotalStepMs;
        if (host.Sim.Tick - StartTick < BenchTicks)
            return;

        var sim = host.Sim;
        int full = sim.Tiles.Count(t => t.Grid.BuildingCount > 0 && RateMode.IsFullSim(sim, t));
        int rate = sim.Tiles.Count(t => t.Grid.BuildingCount > 0 && !RateMode.IsFullSim(sim, t));
        GD.Print($"stress: {host.ViewedTile.Grid.BuildingCount} buildings on the viewed tile, " +
                 $"{full} full-sim and {rate} rate-mode tiles with buildings, {sim.Units.Count} units, " +
                 $"{(host.TotalStepMs - _startMs) / BenchTicks:F4} ms/tick over {BenchTicks} ticks, " +
                 $"{Engine.GetFramesPerSecond()} fps");
        QueueFree();
        if (DisplayServer.GetName() == "headless")
            GetTree().Quit();
    }
}
