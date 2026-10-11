using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

/// <summary>Junctions, routers, sorters, bridges (FB-004), storage (FB-007) and production stats (FB-011).</summary>
public class LogisticsTests
{
    const string Iron = "iron_ore", Copper = "copper_ore";
    const int East = 0, South = 1, North = 3;

    static SortedDictionary<string, int> Held(TileState tile, int x, int y) => tile.Grid.BuildingAt(x, y)!.Storage!.Output;

    [Fact]
    public void TwoLinesCrossAtAJunctionWithoutMixing()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Harness.Ore(tile, Iron, 0, 10);
        Place(sim, tile, "drill", 0, 10);
        Belt(sim, tile, 2, 10, 3);                // (2..4, 10) east
        Place(sim, tile, "junction", 5, 10);
        Belt(sim, tile, 6, 10, 3);                // (6..8, 10) east
        Place(sim, tile, "container", 9, 10);
        Harness.Ore(tile, Copper, 5, 5);
        Place(sim, tile, "drill", 5, 5);
        Belt(sim, tile, 5, 7, 3, South);          // (5, 7..9) south
        Belt(sim, tile, 5, 11, 3, South);         // (5, 11..13) south
        Place(sim, tile, "container", 5, 14);
        Run(sim, 30 * Simulation.TicksPerSecond);

        Assert.Equal([Iron], Held(tile, 9, 10).Keys);
        Assert.Equal([Copper], Held(tile, 5, 14).Keys);
        Assert.True(Held(tile, 9, 10)[Iron] >= 20);
        Assert.True(Held(tile, 5, 14)[Copper] >= 20);
    }

    [Fact]
    public void ARouterSharesItemsBetweenItsOtherSides()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Harness.Ore(tile, Iron, 0, 10);
        Place(sim, tile, "drill", 0, 10);
        Belt(sim, tile, 2, 10, 3);                // (2..4, 10) into the router
        Place(sim, tile, "router", 5, 10);
        // Each other side leads by belt to its own container, so containers never unload back into the router.
        Belt(sim, tile, 5, 9, 2, North);          // (5, 9..8)
        Place(sim, tile, "container", 5, 6);
        Belt(sim, tile, 5, 11, 2, South);         // (5, 11..12)
        Place(sim, tile, "container", 5, 13);
        Belt(sim, tile, 6, 10, 2);                // (6..7, 10)
        Place(sim, tile, "container", 8, 10);
        Run(sim, 60 * Simulation.TicksPerSecond);

        int north = Held(tile, 5, 6).GetValueOrDefault(Iron);
        int south = Held(tile, 5, 13).GetValueOrDefault(Iron);
        int east = Held(tile, 8, 10).GetValueOrDefault(Iron);
        Assert.InRange(north, 15, 25);
        Assert.InRange(south, 15, 25);
        Assert.InRange(east, 15, 25);
    }

    [Fact]
    public void ASorterLetsItsItemThroughAndTurnsTheRestAside()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Harness.Ore(tile, Iron, 0, 10);
        Place(sim, tile, "drill", 0, 10);
        Harness.Ore(tile, Copper, 3, 8);
        Place(sim, tile, "drill", 3, 8);          // Side-loads copper onto (3..4, 10).
        Belt(sim, tile, 2, 10, 7);                // (2..8, 10) east
        Place(sim, tile, "sorter", 9, 10);
        Place(sim, tile, "container", 10, 10);    // Straight on
        Place(sim, tile, "container", 9, 8);      // To the left (north)
        sim.Enqueue(new SetFilterCommand(Me, tile.Id, 9, 10, Iron));
        Run(sim, 30 * Simulation.TicksPerSecond);

        Assert.Equal(Iron, tile.Grid.BuildingAt(9, 10)!.Sorter!.Filter);
        Assert.Equal([Iron], Held(tile, 10, 10).Keys);
        Assert.Equal([Copper], Held(tile, 9, 8).Keys);
    }

    [Fact]
    public void SetFilterIsCheckedLikeEveryCommand()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        var theirs = Tile(sim, 1, 0, owner: Them);
        Place(sim, tile, "sorter", 1, 1);
        Run(sim, 1);
        Assert.Equal(FilterResult.Ok, Logistics.CheckFilter(sim, tile, Me, 1, 1, Iron));
        Assert.Equal(FilterResult.Ok, Logistics.CheckFilter(sim, tile, Me, 1, 1, null));
        Assert.Equal(FilterResult.NoSuchItem, Logistics.CheckFilter(sim, tile, Me, 1, 1, "gold"));
        Assert.Equal(FilterResult.NoSorter, Logistics.CheckFilter(sim, tile, Me, 2, 2, Iron));
        Assert.Equal(FilterResult.NotOwner, Logistics.CheckFilter(sim, theirs, Me, 1, 1, Iron));
    }

    [Fact]
    public void ABridgeCarriesItemsOverWhateverIsInTheWay()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Harness.Ore(tile, Iron, 0, 10);
        Place(sim, tile, "drill", 0, 10);
        Belt(sim, tile, 2, 10, 1);
        Place(sim, tile, "bridge", 3, 10, East);
        Place(sim, tile, "wire", 4, 10);
        Place(sim, tile, "wire", 5, 10);
        Place(sim, tile, "bridge", 6, 10, East);
        Place(sim, tile, "container", 7, 10);
        Run(sim, 30 * Simulation.TicksPerSecond);

        Assert.InRange(Held(tile, 7, 10).GetValueOrDefault(Iron), 25, 30);
    }

    [Fact]
    public void StorageHoldsItemsUnloadsOntoBeltsAndGivesThemBackWhenRemoved()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Harness.Ore(tile, Iron, 0, 10);
        Place(sim, tile, "drill", 0, 10);
        Place(sim, tile, "container", 2, 10);
        Run(sim, 20 * Simulation.TicksPerSecond);
        Assert.InRange(Held(tile, 2, 10)[Iron], 19, 20);

        long before = Items(tile, Iron);
        sim.Enqueue(new RemoveBuildingCommand(Me, tile.Id, 2, 10));
        Run(sim, 1);
        Assert.InRange(Items(tile, Iron) - before, 19, 20);

        // Container -> belt leading away -> core.
        Place(sim, tile, "container", 2, 10);
        Belt(sim, tile, 4, 10, 3);
        Place(sim, tile, "core", 7, 9);
        Run(sim, 1);
        long start = Items(tile, Iron);
        Run(sim, 30 * Simulation.TicksPerSecond);
        Assert.InRange(Items(tile, Iron) - start, 25, 30);
    }

    [Fact]
    public void ProductionStatsGiveRatesPerMinuteAndTenMinutesOfHistory()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Harness.Ore(tile, Iron, 0, 0);
        Place(sim, tile, "drill", 0, 0);
        Place(sim, tile, "smelter", 2, 0, recipe: "smelt_iron"); // Drill feeds it directly: 1 ore/s in, 0.5 plate/s out.
        Place(sim, tile, "core", 4, 0);
        Run(sim, 2 * 60 * Simulation.TicksPerSecond + 1);

        var stats = tile.Stats;
        Assert.InRange(stats.MadePerMinute(Iron), 59, 61);
        Assert.InRange(stats.UsedPerMinute(Iron), 58, 62);
        Assert.InRange(stats.MadePerMinute("iron_plate"), 29, 31);
        var history = stats.MadeHistory(Iron);
        Assert.Equal(ProductionStats.Buckets, history.Length);
        Assert.InRange(history[^1], 9, 11);
        Assert.Equal(0, history[0]); // Ten minutes ago, before the game started.
        Assert.Contains(Iron, stats.Items);
    }
}
