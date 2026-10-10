using System.Diagnostics;
using Wunderwaffe.Sim.Core;
using Xunit.Abstractions;

namespace Wunderwaffe.Sim.Tests;

/// <summary>NF-001 budget checks. Every tile runs the entity sim for now; rate mode (FB-012) will cut this later.</summary>
public class StressTests(ITestOutputHelper output)
{
    const int Tiles = 8;
    const int BuildingsPerTile = 2000;

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
        foreach (var tile in sim.Tiles)
        {
            int placed = 0;
            for (int y = 0; y < tile.Grid.Height - 1 && placed < BuildingsPerTile; y += 2)
            {
                for (int x = 0; x < tile.Grid.Width - 1 && placed < BuildingsPerTile; x += 2)
                {
                    if (!tile.Grid.IsFree(x, y, 2, 2))
                        continue;
                    // Drills on ore, iron smelters elsewhere: every building is a producer.
                    var drill = World.Placement.Check(sim, tile, 0, "drill", x, y, 0, null, out _);
                    sim.Enqueue(drill == World.PlaceResult.Ok
                        ? new PlaceBuildingCommand(0, tile.Id, "drill", x, y)
                        : new PlaceBuildingCommand(0, tile.Id, "smelter", x, y, RecipeId: "smelt_iron"));
                    placed++;
                }
            }
        }
        sim.Step();
        place.Stop();

        Assert.All(sim.Tiles, t => Assert.True(t.Grid.BuildingCount > BuildingsPerTile, $"tile {t.Id}: {t.Grid.BuildingCount}"));

        const int ticks = 600;
        var run = Stopwatch.StartNew();
        for (int i = 0; i < ticks; i++)
            sim.Step();
        run.Stop();

        double msPerTick = run.Elapsed.TotalMilliseconds / ticks;
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
        var sim = new Simulation(TestContent.Load());
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

        const int ticks = 600;
        var run = Stopwatch.StartNew();
        for (int i = 0; i < ticks; i++)
            sim.Step();
        run.Stop();

        double msPerTick = run.Elapsed.TotalMilliseconds / ticks;
        long carried = sim.Tiles.Sum(t => t.Grid.Belts.Sum(b => (long)b.Belt!.Count));
        output.WriteLine($"{carried} items on belts; {msPerTick:F4} ms/tick");
        Assert.True(carried > 4000, $"only {carried} items on belts");
        Assert.True(msPerTick < 2.0, $"{msPerTick:F4} ms/tick");
    }
}
