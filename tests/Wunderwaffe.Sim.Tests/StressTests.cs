using System.Diagnostics;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using Xunit.Abstractions;

namespace Wunderwaffe.Sim.Tests;

/// <summary>
/// NF-001 budget checks: viewed tiles run the full entity sim, the rest rate mode (FB-012). Wall time is noisy when
/// other work shares the machine, so each check warms up and keeps the best of several windows.
/// </summary>
[Collection(TimingCollection.Name)]
public class StressTests(ITestOutputHelper output)
{
    const int Tiles = StressScene.FullTiles;
    const int BuildingsPerTile = StressScene.BuildingsPerTile;

    /// <summary>Starts a measurement from a clean heap, so garbage from earlier tests (generated worlds are large) isn't counted.</summary>
    static void Settle()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>
    /// Milliseconds per tick: the best of <paramref name="windows"/> runs of <paramref name="ticks"/> ticks, after a
    /// warm-up, so a burst of load from elsewhere (parallel builds, other test runs) doesn't fail the budget.
    /// </summary>
    static double MsPerTick(Simulation sim, int ticks = 120, int windows = 5)
    {
        for (int i = 0; i < ticks; i++)
            sim.Step();
        Settle();
        double best = double.MaxValue;
        for (int w = 0; w < windows; w++)
        {
            var run = Stopwatch.StartNew();
            for (int i = 0; i < ticks; i++)
                sim.Step();
            best = Math.Min(best, run.Elapsed.TotalMilliseconds / ticks);
        }
        return best;
    }

    /// <summary>Covers each tile with <see cref="StressScene.BuildingsPerTile"/> producers and applies them.</summary>
    static void Fill(Simulation sim, IEnumerable<TileState> tiles)
    {
        foreach (var tile in tiles)
            StressScene.Fill(sim, tile);
        sim.Step();
    }

    [Fact]
    public void EightTilesOfTwoThousandBuildingsStayWithinTickBudget()
    {
        var sim = new Simulation(TestContent.Load());
        for (int t = 0; t < Tiles; t++)
        {
            var tile = sim.AddStartingTile(t, 0, owner: 0, seed: (ulong)t);
            sim.Enqueue(new DebugGrantCommand(0, tile.Id, "copper_ore", 1_000_000));
            sim.Enqueue(new DebugGrantCommand(0, tile.Id, "iron_ore", 1_000_000));
        }
        sim.Step();

        var place = Stopwatch.StartNew();
        Fill(sim, sim.Tiles);
        place.Stop();

        Assert.All(sim.Tiles, t => Assert.True(t.Grid.BuildingCount > BuildingsPerTile, $"tile {t.Id}: {t.Grid.BuildingCount}"));

        double msPerTick = MsPerTick(sim);
        output.WriteLine($"placed {Tiles}x{BuildingsPerTile} in {place.ElapsedMilliseconds} ms; {msPerTick:F4} ms/tick");

        // 60 UPS leaves 16.7 ms per tick for everything; the rate sim should use a sliver of it.
        Assert.True(msPerTick < 1.0, $"{msPerTick:F4} ms/tick");
    }

    [Fact]
    public void EightBusyFactoriesWithBeltsFullOfItemsStayWithinTickBudget()
    {
        // Each tile: 30 rows of drill -> 39 belt cells -> core, all running. About 1,200 buildings and a
        // thousand-plus belt cells carrying items, per tile.
        const int rows = 30;
        var sim = new Simulation(TestContent.Unpowered());
        for (int t = 0; t < Tiles; t++)
        {
            var tile = sim.AddTile(t, 0, owner: 0);
            tile.Inventory.Add("copper_ore", 1_000_000);
            for (int r = 0; r < rows; r++)
            {
                int y = r * 3;
                Harness.Ore(tile, "iron_ore", 0, y);
                Harness.Place(sim, tile, "drill", 0, y);
                Harness.Belt(sim, tile, 2, y, 39);
                Harness.Place(sim, tile, "core", 41, y);
            }
        }
        sim.Step();
        Assert.All(sim.Tiles, t => Assert.True(t.Grid.BuildingCount >= rows * 41, $"tile {t.Id}: {t.Grid.BuildingCount}"));
        for (int i = 0; i < 2400; i++) // Long enough for items to fill the belts.
            sim.Step();

        double msPerTick = MsPerTick(sim);
        long carried = sim.Tiles.Sum(t => t.Grid.Belts.Sum(b => (long)b.Belt!.Count));
        output.WriteLine($"{carried} items on belts; {msPerTick:F4} ms/tick");
        Assert.True(carried > 4000, $"only {carried} items on belts");
        Assert.True(msPerTick < 2.0, $"{msPerTick:F4} ms/tick");
    }

    [Fact]
    public void EightFullTilesAndTwoHundredRateTilesHoldSixtyUps()
    {
        // NF-001: 8 full-sim tiles x 2,000 buildings plus 200 rate-mode tiles at 60 UPS or better.
        var build = Stopwatch.StartNew();
        var (sim, big, small) = StressScene.Build(TestContent.Load());
        build.Stop();

        Assert.All(big, t => Assert.True(t.Grid.BuildingCount > BuildingsPerTile, $"tile {t.Id}: {t.Grid.BuildingCount}"));
        Assert.All(big, t => Assert.True(RateMode.IsFullSim(sim, t)));
        Assert.All(small, t => Assert.False(RateMode.IsFullSim(sim, t)));
        Assert.All(small, t => Assert.True(t.Grid.BuildingCount >= 6, $"tile {t.Id}: {t.Grid.BuildingCount}"));
        // The links work: every importer gets its neighbour's ore.
        Assert.All(small.Where((_, i) => i % 2 == 1), t => Assert.True(RateMode.RatesOf(sim, t).RateOf(FlowKind.Core, "iron_ore") > 0, $"tile {t.Id}"));

        long before = small.Sum(t => t.Inventory.Get("iron_plate"));
        double msPerTick = MsPerTick(sim);
        output.WriteLine($"built in {build.ElapsedMilliseconds} ms; {msPerTick:F4} ms/tick");
        Assert.True(small.Sum(t => t.Inventory.Get("iron_plate")) > before, "rate tiles made nothing");
        // 60 UPS gives the whole frame 16.7 ms; the sim keeps under a quarter of it.
        Assert.True(msPerTick < 4.0, $"{msPerTick:F4} ms/tick");
    }
}
