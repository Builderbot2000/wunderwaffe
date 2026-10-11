using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

/// <summary>Boosters (FB-016, OQ-25): touching, per-type caps, chains, cooling, and the effects on machines.</summary>
public class BoosterTests
{
    const string Iron = "iron_ore";

    static long Made(TileState tile, string item) => tile.Stats.MadeTotal(item);

    static BoostStats Stats(Simulation sim, TileState tile, int x, int y) =>
        Boosters.StatsOf(sim, tile, tile.Grid.BuildingAt(x, y)!);

    [Fact]
    public void AnOverdriveCoilSpeedsUpTheDrillItTouches()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Harness.Ore(tile, Iron, 0, 0);
        Place(sim, tile, "drill", 0, 0);
        Place(sim, tile, "overdrive_coil", 2, 0);
        Place(sim, tile, "core", 0, 2);
        Run(sim, 3601);

        Assert.Equal(25, Stats(sim, tile, 0, 0).Speed);
        Assert.Equal(75, Made(tile, Iron)); // 48 ticks a batch instead of 60.
    }

    [Fact]
    public void AHostCountsAtMostTwoCoilsAndOneCoilBoostsEveryHostItTouches()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Harness.Ore(tile, Iron, 5, 5);
        Harness.Ore(tile, Iron, 8, 5);
        Place(sim, tile, "drill", 5, 5);
        Place(sim, tile, "drill", 8, 5);
        Place(sim, tile, "overdrive_coil", 4, 5);
        Place(sim, tile, "overdrive_coil", 4, 6);
        Place(sim, tile, "overdrive_coil", 7, 5); // Between the drills: boosts both.
        Run(sim, 1);

        var first = tile.Grid.BuildingAt(5, 5)!;
        Assert.Equal(50, Boosters.StatsOf(sim, tile, first).Speed);
        Assert.Equal(2, Boosters.Of(sim, tile, first).Count);
        Assert.Equal(25, Stats(sim, tile, 8, 5).Speed);
        Assert.Equal(100, Stats(sim, tile, 5, 5).Power); // +50% power draw per coil.
    }

    [Fact]
    public void AYieldSieveAddsAFifthMoreOutput()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Harness.Ore(tile, Iron, 0, 0);
        Place(sim, tile, "drill", 0, 0);
        Place(sim, tile, "yield_sieve", 2, 0);
        Place(sim, tile, "core", 0, 2);
        Run(sim, 3001); // 50 batches.

        Assert.Equal(60, Made(tile, Iron));
    }

    [Fact]
    public void BoostersOnlyWorkWithTheirHostCategories()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Place(sim, tile, "smelter", 0, 0, recipe: "smelt_iron");
        Place(sim, tile, "heat_exchanger", 2, 0);
        Place(sim, tile, "yield_sieve", 0, 2); // Drills only.
        Run(sim, 1);

        var stats = Stats(sim, tile, 0, 0);
        Assert.Equal(-20, stats.Power);
        Assert.Equal(0, stats.Output);
        Assert.Equal(24, BoostStats.Scaled(30, stats.Power));
    }

    [Fact]
    public void RemovingABoosterRevertsItsEffectAtOnce()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Harness.Ore(tile, Iron, 0, 0);
        Place(sim, tile, "drill", 0, 0);
        Place(sim, tile, "overdrive_coil", 2, 0);
        Run(sim, 2);
        var drill = tile.Grid.BuildingAt(0, 0)!.Machine!;
        Assert.Equal(25, drill.Boost.Speed);

        sim.Enqueue(new RemoveBuildingCommand(Me, tile.Id, 2, 0));
        Run(sim, 1);
        Assert.Equal(0, drill.Boost.Speed);
    }

    static (Simulation Sim, TileState Tile) FactorySim()
    {
        var sim = new Simulation(TestContent.Unpowered(TestContent.TestFactory));
        return (sim, Tile(sim, size: 40));
    }

    [Fact]
    public void GantryRailsCountOnlyInStraightChainsRunningOutward()
    {
        var (sim, tile) = FactorySim();
        Place(sim, tile, "test_factory", 10, 10);   // (10..12, 10..12)
        Place(sim, tile, "gantry_rail", 11, 7);     // Upright above, end-on: counts.
        Place(sim, tile, "gantry_rail", 11, 4);     // Continues the line: counts.
        Place(sim, tile, "gantry_rail", 12, 1);     // Off the line: doesn't.
        Place(sim, tile, "gantry_rail", 13, 10, 1); // Lying east, end-on: counts.
        Place(sim, tile, "gantry_rail", 10, 13, 1); // Lying along the bottom side: doesn't.
        Run(sim, 1);

        Assert.Equal(12, Stats(sim, tile, 10, 10).Radius);
        Assert.Equal(12, Boosters.Bonus(sim, tile, tile.Grid.BuildingAt(10, 10)!, "radius"));
    }

    [Fact]
    public void ArmsJigsAndFeederHubsBoostAFactory()
    {
        var (sim, tile) = FactorySim();
        Place(sim, tile, "test_factory", 10, 10);
        Place(sim, tile, "assembly_arm", 8, 10);
        Place(sim, tile, "assembly_arm", 8, 12);
        Place(sim, tile, "precision_jig", 13, 10);
        Place(sim, tile, "precision_jig", 13, 12); // A second jig adds nothing.
        Place(sim, tile, "feeder_hub", 10, 9, 1);  // 2x1 along the top.
        Run(sim, 1);

        var stats = Stats(sim, tile, 10, 10);
        Assert.Equal(32, stats.BlockCap);
        Assert.True(stats.Precision);
        Assert.Equal(1, stats.InputSides);
        Assert.Equal(100, stats.InputBuffer);
        Assert.Single(Boosters.Of(sim, tile, tile.Grid.BuildingAt(10, 10)!), b => b.DefId == "precision_jig");
    }

    [Fact]
    public void AServoBankGivesHalfUnlessACoolantTowerWithRoomTouchesIt()
    {
        var (sim, tile) = FactorySim();
        Place(sim, tile, "test_factory", 20, 20);    // (20..22, 20..22)
        Place(sim, tile, "servo_bank", 23, 20);      // Touches the factory.
        Run(sim, 1);
        Assert.Equal(25, Stats(sim, tile, 20, 20).BuildSpeed);

        Place(sim, tile, "coolant_tower", 25, 20);   // Cools the servo.
        Place(sim, tile, "servo_bank", 25, 22);      // Second servo on the tower.
        Place(sim, tile, "servo_bank", 27, 20);      // Third: the tower serves only two.
        Run(sim, 1);
        Assert.Equal(50, Stats(sim, tile, 20, 20).BuildSpeed);
        Assert.True(Stats(sim, tile, 25, 22).Cooling);
        Assert.False(Stats(sim, tile, 27, 20).Cooling);
    }

    [Fact]
    public void AServoBankWithoutPowerGivesNothing()
    {
        var sim = new Simulation(TestContent.Load(TestContent.TestFactory));
        var tile = Tile(sim, size: 40);
        Place(sim, tile, "test_factory", 20, 20);
        Place(sim, tile, "servo_bank", 23, 20); // Draws 10 with nothing to supply it.
        Run(sim, 2);
        Assert.Equal(0, Stats(sim, tile, 20, 20).BuildSpeed);
    }
}
