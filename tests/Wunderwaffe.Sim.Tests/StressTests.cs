using System.Diagnostics;
using Wunderwaffe.Sim.Core;
using Xunit.Abstractions;

namespace Wunderwaffe.Sim.Tests;

/// <summary>NF-001 budget check. Only covers rate mode until the full-mode entity sim exists.</summary>
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
                    var drill = World.Placement.Check(sim.Content, tile, 0, "drill", x, y, 0, null, out _);
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
}
