using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

/// <summary>Unit factories (UD-006), megafactories (WW-002) and staged assembly (WW-004).</summary>
public class FactoryTests
{
    /// <summary>
    /// Content where thrusters cost iron ore (the core already costs ores), so drills next to a factory can feed it
    /// without a whole plate and steel chain. No power draws, so no network is needed; ore-priced parts need no
    /// Precision Jig.
    /// </summary>
    static Simulation OreSim() => new(TestContent.Load(json => json
        .Replace("\"cost\": { \"iron_plate\": 10, \"steel\": 5 }", "\"cost\": { \"iron_ore\": 4 }"), power: false));

    /// <summary>The standard scout: thruster, 2x2 core, thruster in a row on a 4x2 frame.</summary>
    static readonly Blueprint Scout = new(
        [new BlueprintBlock("thruster", 0, 0, 0), new BlueprintBlock("core", 1, 0, 0), new BlueprintBlock("thruster", 3, 0, 0)],
        [.. Enumerable.Range(0, 8).Select(i => new BlueprintCell(i % 4, i / 4))]);

    /// <summary>
    /// A factory at (10, 10)-(12, 12) fed copper ore by a drill and a two-cell belt from the west, and iron ore by two
    /// drills that touch it.
    /// </summary>
    static PlacedBuilding FedFactory(Simulation sim, TileState tile)
    {
        Place(sim, tile, "unit_factory", 10, 10);
        Harness.Ore(tile, "copper_ore", 6, 10);
        Place(sim, tile, "drill", 6, 10);
        Belt(sim, tile, 8, 10, 2);
        Harness.Ore(tile, "iron_ore", 8, 12);
        Place(sim, tile, "drill", 8, 12);
        Harness.Ore(tile, "iron_ore", 10, 13);
        Place(sim, tile, "drill", 10, 13);
        sim.Step();
        return tile.Grid.BuildingAt(10, 10)!;
    }

    static void Project(Simulation sim, TileState tile, Blueprint? bp, int ox, int oy, int player = Me) =>
        sim.Enqueue(new SetFactoryBlueprintCommand(player, tile.Id, 11, 11, bp, ox, oy));

    [Fact]
    public void AFactoryBuildsAUnitBlockByBlockFromItsBeltsAndLiftsItOff()
    {
        var sim = OreSim();
        var tile = Tile(sim, size: 40);
        var factory = FedFactory(sim, tile);
        var f = factory.Factory!;
        Project(sim, tile, Scout, 14, 11);
        sim.Step();
        Assert.Equal(11, f.Steps.Count);
        Assert.Equal([.. Enumerable.Repeat(BuildStage.Frame, 8), .. Enumerable.Repeat(BuildStage.Systems, 3)], f.Steps.Select(s => s.Stage));
        // 8 frame cells at 2, the core's 5 and the thrusters' 4 each.
        Assert.Equal(29, UnitFactory.Missing(f)["iron_ore"]);

        // Blocks appear one at a time as they are paid for: frame first, then the rest.
        var seen = new List<int>();
        for (int i = 0; i < 6000 && sim.Units.Count == 0; i++)
        {
            int placed = tile.Grid.FrameCount * 10 + tile.Grid.Buildings.Count(b => b.X >= 14 && b.DefId != "bank");
            if (seen.Count == 0 || seen[^1] != placed)
                seen.Add(placed);
            sim.Step();
        }
        Assert.Equal([0, 10, 20, 30, 40, 50, 60, 70, 80, 81, 82, 83], seen);

        var unit = Assert.Single(sim.Units);
        Assert.Equal(Me, unit.Owner);
        Assert.Equal(2, unit.Thrusters.Count);
        Assert.Equal(8, unit.Cells.Count);
        Assert.Equal(1, f.Built);
        Assert.Equal(0, tile.Grid.FrameCount);
        Assert.Equal(1, sim.PlayerOrNull(Me)!.Launched);
        // And it starts on the next copy.
        Assert.Equal(0, f.Next);
        Assert.NotEqual(FactoryStatus.Stopped, f.Status);
    }

    [Fact]
    public void AFactoryWaitsForItemsAndShowsWhatIsMissing()
    {
        var sim = OreSim();
        var tile = Tile(sim, size: 40);
        Place(sim, tile, "unit_factory", 10, 10);
        sim.Step();
        var f = tile.Grid.BuildingAt(10, 10)!.Factory!;
        Assert.Equal(FactoryStatus.Idle, f.Status);

        Project(sim, tile, Scout, 14, 11);
        Run(sim, 100);
        Assert.Equal(FactoryStatus.Waiting, f.Status);
        Assert.Equal(0, tile.Grid.FrameCount);
        Assert.Equal(new SortedDictionary<string, int> { ["copper_ore"] = 10, ["iron_ore"] = 29 }, UnitFactory.Missing(f));
        Assert.Equal([(BuildStage.Frame, 0, 8), (BuildStage.Systems, 0, 3), (BuildStage.Armament, 0, 0)], UnitFactory.StageProgress(f));
    }

    [Fact]
    public void AFactoryRefusesBlueprintsOverItsCapOrOutsideItsRadius()
    {
        var sim = NewSim();
        var tile = Tile(sim, size: 40);
        Place(sim, tile, "unit_factory", 10, 10);
        Place(sim, tile, "conveyor", 16, 16);
        sim.Step();
        var defs = sim.Content.Buildings;
        var factory = tile.Grid.BuildingAt(10, 10)!;
        Assert.Equal(6, UnitFactory.Radius(sim, tile, factory));
        Assert.Equal(16, UnitFactory.BlockCap(sim, tile, factory));

        ProjectResult Check(Blueprint? bp, int ox, int oy, int player = Me, int x = 11, int y = 11) =>
            UnitFactory.CheckProject(sim, tile, player, x, y, bp, ox, oy, 0, false, out _);

        // The scout's parts cost steel (tier 3): the factory needs a Precision Jig touching it (FB-016, OQ-25).
        Assert.Equal(ProjectResult.NeedsPrecision, Check(Scout, 14, 11));
        Place(sim, tile, "precision_jig", 8, 10);
        sim.Step();

        Assert.Equal(ProjectResult.Ok, Check(Scout, 14, 11));
        Assert.Equal(ProjectResult.Ok, Check(Scout, 15, 4)); // corner of the radius: (18, 4) is 6 cells out
        Assert.Equal(ProjectResult.OutOfRange, Check(Scout, 16, 4));
        Assert.Equal(ProjectResult.Blocked, Check(Scout, 14, 16)); // over the conveyor
        Assert.Equal(ProjectResult.Blocked, Check(Scout, 9, 11));  // over the factory itself
        Assert.Equal(ProjectResult.NotOwner, Check(Scout, 14, 11, player: Them));
        Assert.Equal(ProjectResult.NoFactory, Check(Scout, 14, 11, x: 20, y: 20));
        Assert.Equal(ProjectResult.Ok, Check(null, 0, 0));

        // A unit with 17 frame cells is one over the base cap.
        var big = new Blueprint([new BlueprintBlock("core", 0, 0, 0), new BlueprintBlock("thruster", 2, 0, 0)],
            [.. Enumerable.Range(0, 17).Select(i => new BlueprintCell(i % 9, i / 9))]);
        Assert.Equal(17, UnitFactory.SizeOf(defs, big));
        Assert.Equal(ProjectResult.TooLarge, Check(big, 14, 11));

        // A refused projection leaves the factory as it was.
        sim.Enqueue(new SetFactoryBlueprintCommand(Me, tile.Id, 11, 11, big, 14, 11));
        sim.Step();
        Assert.Null(factory.Factory!.Projected);
    }

    [Fact]
    public void AWunderwaffeSizedBlueprintNeedsABoostedMegafactory()
    {
        // WW-002: there is no megafactory building; a base factory's cap is far below a Wunderwaffe's size, so only
        // boosters (FB-016) that raise the cap let a factory take one.
        var sim = NewSim();
        var tile = Tile(sim, size: 60);
        Place(sim, tile, "unit_factory", 25, 25);
        sim.Step();
        var factory = tile.Grid.BuildingAt(25, 25)!;
        int cells = Balance.WunderwaffeCells + 1;
        var mega = new Blueprint([new BlueprintBlock("core", 0, 0, 0), new BlueprintBlock("thruster", 2, 0, 0)],
            [.. Enumerable.Range(0, cells).Select(i => new BlueprintCell(i % 7, i / 7))]);
        Assert.True(UnitStats.Of(sim.Content, mega).IsWunderwaffe);
        Assert.True(UnitFactory.BlockCap(sim, tile, factory) < cells);
        Assert.Equal(ProjectResult.TooLarge, UnitFactory.CheckProject(sim, tile, Me, 25, 25, mega, 29, 24, 0, false, out _));
    }

    [Fact]
    public void ABuildingBlueprintStaysAndBlocksTheFactoryUntilRemoved()
    {
        var sim = OreSim();
        var tile = Tile(sim, size: 40);
        var factory = FedFactory(sim, tile);
        var f = factory.Factory!;
        var belts = new Blueprint([new BlueprintBlock("conveyor", 0, 0, 1), new BlueprintBlock("conveyor", 0, 1, 1)], []);
        Project(sim, tile, belts, 15, 11);
        Assert.True(RunUntil(sim, () => f.Built == 1, 3000));
        Run(sim, 10);
        Assert.Equal("conveyor", tile.Grid.BuildingAt(15, 12)?.DefId);
        Assert.Empty(sim.Units);
        Assert.Equal(FactoryStatus.Blocked, f.Status);

        sim.Enqueue(new RemoveBuildingCommand(Me, tile.Id, 15, 11));
        sim.Enqueue(new RemoveBuildingCommand(Me, tile.Id, 15, 12));
        Assert.True(RunUntil(sim, () => f.Built == 2, 3000));
        Assert.Equal("conveyor", tile.Grid.BuildingAt(15, 11)?.DefId);
    }

    [Fact]
    public void RemovingAFactoryReturnsItsItems()
    {
        var sim = OreSim();
        var tile = Tile(sim, size: 40);
        var factory = FedFactory(sim, tile);
        var f = factory.Factory!;
        Project(sim, tile, Scout, 14, 11);
        Run(sim, 400);
        long held = f.Buffer.Values.Sum() + (f.Working ? f.Current!.Value.Cost.Values.Sum() : 0);
        Assert.True(held > 0);

        long before = Funds.Total(tile, Me).Sum(kv => kv.Value);
        long refund = sim.Content.Buildings["unit_factory"].Cost.Values.Sum();
        sim.Enqueue(new RemoveBuildingCommand(Me, tile.Id, 11, 11));
        sim.Step();
        // Drills keep feeding the belt in the meantime, so compare at least.
        Assert.True(Funds.Total(tile, Me).Sum(kv => kv.Value) >= before + refund + held);
    }

    [Fact]
    public void FactoryRunsAreDeterministic()
    {
        ulong Run1()
        {
            var sim = OreSim();
            var tile = Tile(sim, size: 40);
            FedFactory(sim, tile);
            Project(sim, tile, Scout, 14, 11);
            Run(sim, 1500);
            return sim.StateHash();
        }
        Assert.Equal(Run1(), Run1());
    }

    [Fact]
    public void FactoryAndUpkeepContentIsValidated()
    {
        var e = Assert.Throws<Wunderwaffe.Sim.Content.ContentException>(() => TestContent.Load(json => json
            .Replace("\"radius\": 6", "\"radius\": 0")
            .Replace("\"kind\": \"thruster\",", "\"kind\": \"thruster\", \"upkeep\": { \"unobtainium\": 1 },")));
        Assert.Contains(e.Errors, m => m.Contains("unit_factory") && m.Contains("radius"));
        Assert.Contains(e.Errors, m => m.Contains("thruster") && m.Contains("upkeep"));
    }

    [Fact]
    public void AManualFactoryBuildsOneUnitPerTriggerAndTakesNothingInBetween()
    {
        // OQ-43: manual production; projecting counts as the first trigger.
        var sim = OreSim();
        var tile = Tile(sim, size: 40);
        var factory = FedFactory(sim, tile);
        var f = factory.Factory!;
        sim.Enqueue(new SetFactoryModeCommand(Me, tile.Id, 11, 11, FactoryMode.Manual));
        Project(sim, tile, Scout, 14, 11);
        Assert.True(RunUntil(sim, () => sim.Units.Count == 1, 6000));
        sim.Step();
        Assert.Equal(FactoryStatus.AwaitingTrigger, f.Status);
        Assert.Equal(FactoryControlResult.Ok, UnitFactory.CheckTrigger(tile, Me, 11, 11));
        Assert.Equal(FactoryControlResult.NotOwner, UnitFactory.CheckTrigger(tile, Them, 11, 11));
        Assert.Equal(FactoryControlResult.NotStopped, UnitFactory.CheckResume(tile, Me, 11, 11));

        // Idle until triggered: the drills keep running but the factory takes nothing.
        long buffered = f.Buffer.Values.Sum();
        Run(sim, 900);
        Assert.Single(sim.Units);
        Assert.Equal(buffered, f.Buffer.Values.Sum());
        Assert.Equal(0, f.Next);

        // Move the first unit off the build site, then trigger one more.
        sim.Enqueue(new MoveUnitsCommand(Me, [sim.Units[0].Id], tile.Id, 30, 30));
        Run(sim, 300);
        sim.Enqueue(new TriggerFactoryCommand(Me, tile.Id, 11, 11));
        sim.Step();
        Assert.Equal(FactoryControlResult.NotAwaiting, UnitFactory.CheckTrigger(tile, Me, 11, 11));
        Assert.True(RunUntil(sim, () => sim.Units.Count == 2, 6000));
        sim.Step();
        Assert.Equal(FactoryStatus.AwaitingTrigger, f.Status);

        // Switching to continuous starts the next copy at once.
        sim.Enqueue(new SetFactoryModeCommand(Me, tile.Id, 11, 11, FactoryMode.Continuous));
        Run(sim, 2);
        Assert.Equal(FactoryMode.Continuous, f.Mode);
        Assert.NotEqual(FactoryStatus.AwaitingTrigger, f.Status);
    }

    [Fact]
    public void AFailedLiftOffStopsTheFactoryWithAnAlertUntilResumed()
    {
        // OQ-44: no endless retry; the factory stops, says why, raises an alert and waits for the player.
        var sim = OreSim();
        var tile = Tile(sim, size: 40);
        var factory = FedFactory(sim, tile);
        var f = factory.Factory!;
        Project(sim, tile, Scout, 14, 11);
        Assert.True(RunUntil(sim, () => tile.Grid.FrameCount == 8, 6000));
        // A second core half on the frame: the finished scout can't lift off.
        Place(sim, tile, "core", 17, 12);
        Assert.True(RunUntil(sim, () => f.Status == FactoryStatus.Stopped, 6000));
        Assert.Empty(sim.Units);
        Assert.NotEqual(AssembleResult.Ok, f.LiftOff);
        var alert = Assert.Single(sim.Events, e => e.Kind == GameEventKind.FactoryStopped);
        Assert.Equal((Me, tile.Id, f.LiftOff.ToString()), (alert.Player, alert.TileId, alert.ItemId));

        // Stopped: nothing changes and it takes no items, however long it waits.
        var buffer = f.Buffer.ToList();
        Run(sim, 600);
        Assert.Equal(FactoryStatus.Stopped, f.Status);
        Assert.Equal(buffer, f.Buffer.ToList());
        Assert.Single(sim.Events, e => e.Kind == GameEventKind.FactoryStopped);
        Assert.Equal(FactoryControlResult.NotAwaiting, UnitFactory.CheckTrigger(tile, Me, 11, 11));

        // Resuming with the cause still there stops it again, with a fresh alert.
        sim.Enqueue(new ResumeFactoryCommand(Me, tile.Id, 11, 11));
        Run(sim, 2);
        Assert.Equal(FactoryStatus.Stopped, f.Status);
        Assert.Equal(2, sim.Events.Count(e => e.Kind == GameEventKind.FactoryStopped));

        // Clear the cause and resume: it lifts off and carries on.
        sim.Enqueue(new RemoveBuildingCommand(Me, tile.Id, 17, 12));
        sim.Step();
        Assert.Equal(FactoryControlResult.Ok, UnitFactory.CheckResume(tile, Me, 11, 11));
        sim.Enqueue(new ResumeFactoryCommand(Me, tile.Id, 11, 11));
        Run(sim, 2);
        Assert.Single(sim.Units);
        Assert.Equal(1, f.Built);
        Assert.Equal(FactoryStatus.Waiting, f.Status);
    }

    static bool RunUntil(Simulation sim, Func<bool> done, int maxTicks)
    {
        for (int i = 0; i < maxTicks; i++)
        {
            sim.Step();
            if (done())
                return true;
        }
        return false;
    }
}
