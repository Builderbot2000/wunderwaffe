using Wunderwaffe.Sim.Ai;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

public class EntityTests
{
    const string Iron = "iron_ore";

    [Fact]
    public void DrillFeedsAnAdjacentCoreOneItemASecond()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Harness.Ore(tile, Iron, 10, 10);
        Place(sim, tile, "drill", 10, 10);
        Place(sim, tile, "core_large", 12, 10);
        long start = Items(tile, Iron);

        Run(sim, 59);
        Assert.Equal(start, Items(tile, Iron));
        Run(sim, 1);
        Assert.Equal(start + 1, Items(tile, Iron));
        Run(sim, 60);
        Assert.Equal(start + 2, Items(tile, Iron));
    }

    [Fact]
    public void ADrillWithNowhereToPutOutputFillsItsBufferAndStops()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Harness.Ore(tile, Iron, 10, 10);
        Place(sim, tile, "drill", 10, 10);
        Run(sim, 20 * Simulation.TicksPerSecond);

        var drill = tile.Grid.BuildingAt(10, 10)!;
        Assert.Equal(MachineState.OutputCapacity, drill.Machine!.OutputCount);
        Assert.False(drill.Machine.Working);
    }

    [Fact]
    public void ABeltTakesThirtyTwoTicksPerCell()
    {
        // Drill at x 2-3, five belt cells at x 4-8, core at x 9. The first item leaves the drill on tick 59.
        var sim = NewSim();
        var tile = Tile(sim);
        Harness.Ore(tile, Iron, 2, 10);
        Place(sim, tile, "drill", 2, 10);
        Belt(sim, tile, 4, 10, 5);
        Place(sim, tile, "core_large", 9, 10);
        long start = Items(tile, Iron);

        // The item enters the first cell on tick 59 and moves that same tick, so each cell costs 32 ticks and the
        // item reaches the core on tick 59 + 5 * 32 - 1.
        int arrival = 59 + 5 * 32;
        Run(sim, arrival - 1);
        Assert.Equal(start, Items(tile, Iron));
        Run(sim, 1);
        Assert.Equal(start + 1, Items(tile, Iron));
    }

    [Fact]
    public void ABeltRunsAtItsThroughputLimitNoMore()
    {
        // Ten drills make 10 a second into one belt that carries speed / spacing = 7.5 a second.
        var sim = NewSim();
        var tile = Tile(sim, size: 40);
        for (int i = 0; i < 10; i++)
        {
            int x = i * 2;
            Harness.Ore(tile, Iron, x, 8);
            Place(sim, tile, "drill", x, 8);
        }
        Belt(sim, tile, 0, 10, 21);
        Place(sim, tile, "core_large", 21, 10);
        Run(sim, 1200); // Fill the belt.

        long before = Items(tile, Iron);
        Run(sim, 60 * Simulation.TicksPerSecond);
        // The cap is 7.5 a second = 450. Merging from drills costs a few percent, but never beats the cap.
        Assert.InRange(Items(tile, Iron) - before, 420, 450);
    }

    [Fact]
    public void AnUnderfedBeltCarriesEverythingItIsGiven()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Harness.Ore(tile, Iron, 2, 10);
        Place(sim, tile, "drill", 2, 10);
        Belt(sim, tile, 4, 10, 15);
        Place(sim, tile, "core_large", 19, 10);
        Run(sim, 15 * 32 + 200);

        long before = Items(tile, Iron);
        Run(sim, 60 * Simulation.TicksPerSecond);
        Assert.Equal(60, Items(tile, Iron) - before);
    }

    [Fact]
    public void ItemsFollowBeltsAroundCorners()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Harness.Ore(tile, Iron, 2, 10);
        Place(sim, tile, "drill", 2, 10);
        Belt(sim, tile, 4, 10, 3);           // east: (4..6, 10)
        Belt(sim, tile, 7, 10, 3, rotation: 1); // south: (7, 10..12)
        Place(sim, tile, "core_large", 6, 13);
        long start = Items(tile, Iron);
        Run(sim, 59 + 6 * 32 + 5);

        Assert.True(Items(tile, Iron) > start);
    }

    [Fact]
    public void ABeltDoesNotFeedABeltFacingIt()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Harness.Ore(tile, Iron, 2, 10);
        Place(sim, tile, "drill", 2, 10);
        Place(sim, tile, "conveyor", 4, 10, rotation: 0);
        Place(sim, tile, "conveyor", 5, 10, rotation: 2); // faces back at the first one
        Run(sim, 1000);

        Assert.Equal(0, tile.Grid.BuildingAt(5, 10)!.Belt!.Count);
        Assert.Equal(BeltState.Capacity, tile.Grid.BuildingAt(4, 10)!.Belt!.Count);
    }

    [Fact]
    public void BeltsMergeFromTheSide()
    {
        // A drill on each of two feeder belts that join the main belt from either side of its start.
        var sim = NewSim();
        var tile = Tile(sim);
        Harness.Ore(tile, Iron, 2, 8);
        Harness.Ore(tile, Iron, 2, 12);
        Place(sim, tile, "drill", 2, 8);
        Place(sim, tile, "drill", 2, 12);
        Belt(sim, tile, 4, 8, 3);              // (4..6, 8) east
        Place(sim, tile, "conveyor", 7, 8, 1); // turns south
        Place(sim, tile, "conveyor", 7, 9, 1);
        Belt(sim, tile, 4, 12, 3);             // (4..6, 12) east
        Place(sim, tile, "conveyor", 7, 12, 3); // turns north
        Place(sim, tile, "conveyor", 7, 11, 3);
        Belt(sim, tile, 7, 10, 4);             // main belt (7..10, 10) east, fed at (7,10) from both sides
        Place(sim, tile, "core_large", 11, 10);
        Run(sim, 600);
        long before = Items(tile, Iron);
        Run(sim, 60 * Simulation.TicksPerSecond);

        Assert.Equal(120, Items(tile, Iron) - before);
    }

    [Fact]
    public void SmeltersRunTheirRecipeOnBeltDeliveredInputs()
    {
        // Iron drill -> belt -> smelter -> belt -> core. smelt_iron takes 2 ore per 120 ticks, so the drill's
        // 1 ore a second yields 1 plate every 2 seconds.
        var sim = NewSim();
        var tile = Tile(sim);
        Harness.Ore(tile, Iron, 2, 10);
        Place(sim, tile, "drill", 2, 10);
        Belt(sim, tile, 4, 10, 2);
        Place(sim, tile, "smelter", 6, 10, recipe: "smelt_iron");
        Belt(sim, tile, 8, 10, 2);
        Place(sim, tile, "core_large", 10, 10);
        Run(sim, 1000);

        long before = Items(tile, "iron_plate");
        Run(sim, 60 * Simulation.TicksPerSecond);
        Assert.InRange(Items(tile, "iron_plate") - before, 29, 31);
    }

    [Fact]
    public void MachinesOnlyAcceptTheirOwnInputs()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Harness.Ore(tile, "copper_ore", 2, 10);
        Place(sim, tile, "drill", 2, 10);
        Belt(sim, tile, 4, 10, 2);
        Place(sim, tile, "smelter", 6, 10, recipe: "smelt_iron"); // wants iron ore, is offered copper
        Run(sim, 1500);

        var smelter = tile.Grid.BuildingAt(6, 10)!.Machine!;
        Assert.Empty(smelter.Input);
        Assert.Equal(BeltState.Capacity, tile.Grid.BuildingAt(5, 10)!.Belt!.Count);
    }

    [Fact]
    public void MachineInputBuffersAreBounded()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Harness.Ore(tile, Iron, 2, 10, 2, 2);
        Place(sim, tile, "drill", 2, 10);
        // A smelter that can't output anywhere keeps taking ore only up to its buffer.
        Place(sim, tile, "smelter", 4, 10, recipe: "smelt_iron");
        Run(sim, 5000);

        var m = tile.Grid.BuildingAt(4, 10)!.Machine!;
        Assert.True(m.Input.GetValueOrDefault(Iron) <= 2 * MachineState.InputBatches);
        Assert.True(m.OutputCount <= MachineState.OutputCapacity);
    }

    [Fact]
    public void ABuildingDumpsOutputToItsNeighboursInTurn()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Harness.Ore(tile, Iron, 2, 10);
        Place(sim, tile, "drill", 2, 10);
        Place(sim, tile, "conveyor", 4, 10); // dead ends: nothing in front of either
        Place(sim, tile, "conveyor", 4, 11);
        Run(sim, 6 * 60 + 40);

        var a = tile.Grid.BuildingAt(4, 10)!.Belt!.Count;
        var b = tile.Grid.BuildingAt(4, 11)!.Belt!.Count;
        Assert.Equal(6, a + b);
        Assert.InRange(Math.Abs(a - b), 0, 1);
    }

    [Fact]
    public void BeltSpacingIsNeverViolated()
    {
        var sim = NewSim();
        var tile = Tile(sim, size: 40);
        for (int i = 0; i < 10; i++)
        {
            Harness.Ore(tile, Iron, i * 2, 8);
            Place(sim, tile, "drill", i * 2, 8);
        }
        Belt(sim, tile, 0, 10, 21);
        Place(sim, tile, "core_large", 21, 10);

        for (int t = 0; t < 1500; t++)
        {
            sim.Step();
            foreach (var b in tile.Grid.Belts)
            {
                var belt = b.Belt!;
                for (int i = 0; i < belt.Count; i++)
                {
                    Assert.InRange(belt.Positions[i], 0, BeltState.CellLength);
                    if (i > 0)
                        Assert.True(belt.Positions[i - 1] - belt.Positions[i] >= BeltState.Spacing);
                }
            }
        }
    }

    [Fact]
    public void RemovingABuildingReturnsWhatItHolds()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Harness.Ore(tile, Iron, 2, 10);
        Place(sim, tile, "drill", 2, 10);
        Belt(sim, tile, 4, 10, 3); // no core: items pile up on the belt
        Run(sim, 700);

        var belt = tile.Grid.BuildingAt(6, 10)!.Belt!;
        Assert.True(belt.Count > 0);
        long before = Items(tile, Iron);
        sim.Enqueue(new RemoveBuildingCommand(Me, tile.Id, 6, 10));
        sim.Step();
        Assert.Equal(before + belt.Count, Items(tile, Iron));
    }

    [Fact]
    public void SameCommandsGiveTheSameStateAndDifferentOnesDont()
    {
        ulong Build(int drills)
        {
            var sim = NewSim();
            var tile = Tile(sim);
            for (int i = 0; i < drills; i++)
            {
                Harness.Ore(tile, Iron, 2, 4 + i * 2);
                Place(sim, tile, "drill", 2, 4 + i * 2);
            }
            Belt(sim, tile, 4, 10, 10);
            Place(sim, tile, "core_large", 14, 10);
            Run(sim, 400);
            return sim.StateHash();
        }

        Assert.Equal(Build(3), Build(3));
        Assert.NotEqual(Build(3), Build(4));
    }

    [Fact]
    public void SleepingMachinesChangeNothing()
    {
        // NF-001: idle machines are skipped until something changes around them, but every tick must come out as if
        // all were stepped. Two AIs build, starve, power and feed machines in full sim, once with sleeping off.
        GameSetup setup = new(5, 5, 7, Players: 2, AiPlayers: 2);
        var sims = new[] { Simulation.NewGame(TestContent.Load(), setup), Simulation.NewGame(TestContent.Load(), setup) };
        foreach (var s in sims)
            s.RateModeEnabled = false;
        foreach (var tile in sims[1].Tiles)
            tile.Grid.Sleep.Off = true;
        var ais = sims.Select(s => new AiDirector(s.Setup!)).ToArray();
        int mostAsleep = 0;
        for (int t = 0; t < 3 * 60 * Simulation.TicksPerSecond; t++)
        {
            for (int k = 0; k < 2; k++)
            {
                ais[k].BeforeStep(sims[k]);
                sims[k].Step();
            }
            if (t % Simulation.TicksPerSecond == 0)
            {
                Assert.Equal(sims[1].StateHash(), sims[0].StateHash());
                mostAsleep = Math.Max(mostAsleep, sims[0].Tiles.Sum(x => x.Grid.Machines.Count(b => b.Machine!.Bed != null)));
            }
        }
        Assert.Equal(sims[1].StateHash(), sims[0].StateHash());
        Assert.True(mostAsleep > 0);
    }

    [Fact]
    public void MachinesSleepingWithoutPowerWakeWhenAGeneratorStarts()
    {
        // Drills on a fuel generator's network sit mid-batch with no power until fuel reaches it along a belt, with no
        // layout change to wake them: only the power check does. Same state every tick as stepping them all.
        var sims = new[] { PoweredSim(), PoweredSim() };
        var tiles = sims.Select(sim =>
        {
            var tile = Tile(sim);
            Place(sim, tile, "core_large", 0, 0);
            Harness.Ore(tile, "copper_ore", 3, 2);
            Place(sim, tile, "drill", 3, 2);
            Place(sim, tile, "fuel_press", 3, 0, recipe: "press_fuel");
            Belt(sim, tile, 5, 0, 5);
            Place(sim, tile, "fuel_generator", 10, 0);
            Harness.Ore(tile, Iron, 12, 0);
            Place(sim, tile, "drill", 12, 0);
            Harness.Ore(tile, Iron, 10, 2);
            Place(sim, tile, "drill", 10, 2);
            Place(sim, tile, "container", 12, 2);
            return tile;
        }).ToArray();
        tiles[1].Grid.Sleep.Off = true;
        foreach (var sim in sims)
            sim.Step();
        var drill = tiles[0].Grid.BuildingAt(12, 0)!.Machine!;
        bool slept = false;
        for (int t = 0; t < 3 * 60 * Simulation.TicksPerSecond; t++)
        {
            foreach (var sim in sims)
                sim.Step();
            slept |= drill.Bed != null && drill.Working;
            Assert.Equal(sims[1].StateHash(), sims[0].StateHash());
        }
        Assert.True(slept);
        Assert.True(tiles[0].Stats.MadeTotal(Iron) > 0);
    }
}
