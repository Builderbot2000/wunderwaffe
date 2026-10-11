using System.Diagnostics;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using Xunit.Abstractions;

namespace Wunderwaffe.Sim.Tests;

/// <summary>NF-001 budget for combat: two armies of gunships trading fire across a border, with turrets behind them.</summary>
[Collection(TimingCollection.Name)]
public class CombatStressTests(ITestOutputHelper output)
{
    const int Size = 96;
    const int Me = Harness.Me, Them = Harness.Them;

    static void Gunship(Simulation sim, TileState tile, int player, int x, int y)
    {
        for (int i = -1; i <= 1; i++)
            sim.Enqueue(new PlaceFrameCommand(player, tile.Id, x + i, y));
        sim.Enqueue(new PlaceBuildingCommand(player, tile.Id, "thruster", x - 1, y));
        sim.Enqueue(new PlaceBuildingCommand(player, tile.Id, "unit_core", x, y));
        sim.Enqueue(new PlaceBuildingCommand(player, tile.Id, "gun_turret", x + 1, y));
    }

    [Fact]
    public void TwoArmiesInAFirefightStayWithinTickBudget()
    {
        var sim = Harness.NewSim();
        var mine = Harness.Tile(sim, 0, 0, Size, Me);
        var theirs = Harness.Tile(sim, 1, 0, Size, Them);

        // Three columns of gunships each side of the border, a row every other cell; turrets behind them.
        var cores = new List<(TileState Tile, int Player, int X, int Y)>();
        for (int y = 2; y < Size - 2; y += 2)
        {
            foreach (int x in new[] { 84, 88, 92 })
            {
                Gunship(sim, mine, Me, x, y);
                cores.Add((mine, Me, x, y));
            }
            foreach (int x in new[] { 3, 7, 11 })
            {
                Gunship(sim, theirs, Them, x, y);
                cores.Add((theirs, Them, x, y));
            }
            sim.Enqueue(new PlaceBuildingCommand(Me, mine.Id, "laser_turret", 80, y));
            sim.Enqueue(new PlaceBuildingCommand(Them, theirs.Id, "laser_turret", 15, y));
        }
        sim.Step();
        foreach (var (tile, player, x, y) in cores)
            sim.Enqueue(new ActivateUnitCommand(player, tile.Id, x, y));
        sim.Step();
        // Plenty of shells loaded, so nobody runs dry during the measurement.
        foreach (var u in sim.Units)
        {
            foreach (var c in u.BlockCombat)
            {
                if (c != null)
                    c.Ammo = 10_000;
            }
        }
        int units = sim.Units.Count;
        Assert.True(units > 250, $"{units} units");

        Timing.PreferFastCores();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        const int ticks = 600;
        int peak = 0;
        var run = Stopwatch.StartNew();
        for (int i = 0; i < ticks; i++)
        {
            sim.Step();
            peak = Math.Max(peak, sim.Projectiles.Count);
        }
        run.Stop();

        double msPerTick = run.Elapsed.TotalMilliseconds / ticks;
        int deadBlocks = sim.Units.Sum(u => u.Blocks.Count(b => b.Hp <= 0));
        output.WriteLine($"{units} units, peak {peak} shots, {units - sim.Units.Count} destroyed, {deadBlocks} dead blocks; {msPerTick:F3} ms/tick");
        Assert.True(peak > 100, $"peak {peak} shots");
        Assert.True(deadBlocks > 0 || units > sim.Units.Count, "the fight should do damage");
        // 16.7 ms is the whole frame at 60 UPS; a large battle should leave most of it for everything else.
        Assert.True(msPerTick < 5.0, $"{msPerTick:F3} ms/tick");
    }
}
