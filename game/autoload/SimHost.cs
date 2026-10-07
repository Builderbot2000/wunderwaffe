using System.Diagnostics;
using System.Linq;
using Godot;
using Wunderwaffe.Game.Input;
using Wunderwaffe.Sim;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game;

/// <summary>
/// Owns the simulation and advances it at a fixed tick rate, independent of frame rate.
/// Frame delta is only used to decide how many ticks to run; it never enters sim state.
/// </summary>
public partial class SimHost : Node
{
    const double TickSeconds = 1.0 / Simulation.TicksPerSecond;
    const int MaxTicksPerFrame = 8;
    const ulong WorldSeed = 3037;
    const int StressBuildings = 2000;
    const int BenchTicks = 600;

    double _accumulator;
    long _benchStartTick = -1;
    double _benchStepMs;

    public Simulation Sim { get; private set; } = null!;
    public int LocalPlayer { get; } = 0;

    /// <summary>The tile the local player is looking at. Fixed to the start tile until the world map exists.</summary>
    public TileState ViewedTile => Sim.Tiles[0];

    /// <summary>Smoothed wall time of one <see cref="Simulation.Step"/>, for the NF-001 budget.</summary>
    public double StepMs { get; private set; }

    public override void _EnterTree() => InputActions.Register();

    public override void _Ready()
    {
        Sim = new Simulation(LoadContent());
        var start = Sim.AddStartingTile(0, 0, LocalPlayer, WorldSeed);
        start.Mode = SimMode.Full; // Viewed by the local player (FB-013).

        // `-- --stress` fills the tile with buildings and reports tick cost after BenchTicks.
        if (OS.GetCmdlineUserArgs().Contains("--stress"))
            StressFill();
    }

    public override void _Process(double delta)
    {
        _accumulator += delta;
        int steps = 0;
        while (_accumulator >= TickSeconds && steps < MaxTicksPerFrame)
        {
            long t0 = Stopwatch.GetTimestamp();
            Sim.Step();
            double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            StepMs = StepMs == 0 ? ms : StepMs * 0.95 + ms * 0.05;
            if (_benchStartTick >= 0)
                _benchStepMs += ms;

            _accumulator -= TickSeconds;
            steps++;
        }
        if (steps == MaxTicksPerFrame)
            _accumulator = 0; // Falling behind: drop time rather than spiral.

        if (_benchStartTick >= 0 && Sim.Tick - _benchStartTick >= BenchTicks)
            ReportBench();
    }

    /// <summary>Debug: covers the viewed tile with ~2,000 producing buildings (NF-001 stress test).</summary>
    public void StressFill()
    {
        var tile = ViewedTile;
        Sim.Enqueue(new DebugGrantCommand(LocalPlayer, tile.Id, "copper_ore", 1_000_000));
        Sim.Enqueue(new DebugGrantCommand(LocalPlayer, tile.Id, "iron_ore", 1_000_000));

        int placed = 0;
        for (int y = 0; y < tile.Grid.Height - 1 && placed < StressBuildings; y += 2)
        {
            for (int x = 0; x < tile.Grid.Width - 1 && placed < StressBuildings; x += 2)
            {
                if (!tile.Grid.IsFree(x, y, 2, 2))
                    continue;
                bool onOre = Placement.Check(Sim.Content, tile, LocalPlayer, "drill", x, y, 0, null, out _)
                    != PlaceResult.NeedsDeposit;
                Sim.Enqueue(onOre
                    ? new PlaceBuildingCommand(LocalPlayer, tile.Id, "drill", x, y)
                    : new PlaceBuildingCommand(LocalPlayer, tile.Id, "smelter", x, y, RecipeId: "smelt_iron"));
                placed++;
            }
        }
        _benchStartTick = Sim.Tick + 1;
        _benchStepMs = 0;
    }

    void ReportBench()
    {
        GD.Print($"stress: {ViewedTile.Grid.BuildingCount} buildings, " +
                 $"{_benchStepMs / BenchTicks:F4} ms/tick over {BenchTicks} ticks, " +
                 $"{Engine.GetFramesPerSecond()} fps");
        _benchStartTick = -1;
        if (DisplayServer.GetName() == "headless")
            GetTree().Quit();
    }

    static ContentDb LoadContent()
    {
        static string Read(string name) => FileAccess.GetFileAsString($"res://data/{name}");
        return ContentDb.FromJson(Read("items.json"), Read("recipes.json"), Read("buildings.json"), Read("tech.json"));
    }
}
