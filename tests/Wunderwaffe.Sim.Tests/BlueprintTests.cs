using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

/// <summary>Blueprints (FB-009, UD-001), lift-off validation (UD-003), derived stats (UD-002) and upkeep (UD-013).</summary>
public class BlueprintTests
{
    static void Frame(Simulation sim, TileState tile, int x, int y, int w = 1, int h = 1)
    {
        for (int cy = y; cy < y + h; cy++)
            for (int cx = x; cx < x + w; cx++)
                sim.Enqueue(new PlaceFrameCommand(Me, tile.Id, cx, cy));
    }

    /// <summary>
    /// A 4x2 design with its core at (x, y): engines facing three ways and a smelter running a chosen recipe.
    /// </summary>
    static void LayDesign(Simulation sim, TileState tile, int x, int y)
    {
        Frame(sim, tile, x, y, 4, 2);
        Place(sim, tile, "unit_core", x, y);
        Place(sim, tile, "thruster", x, y + 1, 1);
        Place(sim, tile, "smelter", x + 1, y, recipe: "smelt_iron");
        Place(sim, tile, "thruster", x + 3, y, 0);
        Place(sim, tile, "thruster", x + 3, y + 1, 2);
        sim.Step();
    }

    static Unit LiftOff(Simulation sim, TileState tile, int x, int y)
    {
        int before = sim.Units.Count;
        sim.Enqueue(new ActivateUnitCommand(Me, tile.Id, x, y));
        sim.Step();
        Assert.Equal(before + 1, sim.Units.Count);
        return sim.Units[^1];
    }

    static string Shape(Unit u) =>
        string.Join(";", u.Blocks.Select(b => $"{b.DefId}@{b.GX},{b.GY} {b.Width}x{b.Height} r{b.Rotation} {b.RecipeId}"))
        + "|" + string.Join(";", u.Cells.Select(c => $"{c.GX},{c.GY},{c.Mass}"));

    // --- recording and placing (FB-009, UD-001) ---

    [Fact]
    public void ARecordedUnitRebuildsAsTheIdenticalUnit()
    {
        var sim = NewSim();
        var tile = Tile(sim, size: 40);
        LayDesign(sim, tile, 5, 5);
        var bp = Blueprints.FromUnitCore(sim, tile, Me, 5, 5, out var recorded);
        Assert.Equal(AssembleResult.Ok, recorded);
        Assert.NotNull(bp);
        Assert.Equal(5, bp.Blocks.Length);
        Assert.Equal(8, bp.Frames.Length);

        sim.Enqueue(new PlaceBlueprintCommand(Me, tile.Id, bp, 20, 20));
        sim.Step();
        var original = LiftOff(sim, tile, 5, 5);
        var copy = LiftOff(sim, tile, 20, 20);

        Assert.Equal(Shape(original), Shape(copy));
        Assert.Equal(original.Mass, copy.Mass);
        Assert.Equal(original.Inertia, copy.Inertia);
        Assert.Equal(original.MaxSpeed, copy.MaxSpeed);
        // And recording the flying unit gives the same blueprint back.
        var again = Blueprints.FromUnit(copy);
        Assert.Equal(bp.Blocks, again.Blocks);
        Assert.Equal(bp.Frames, again.Frames);
    }

    [Fact]
    public void PlacingABlueprintPaysTheTotalInOneAction()
    {
        var sim = NewSim();
        var tile = Tile(sim, size: 40);
        LayDesign(sim, tile, 5, 5);
        var bp = Blueprints.FromUnitCore(sim, tile, Me, 5, 5, out _)!;
        var cost = UnitStats.Of(sim.Content, bp).Cost;
        var before = tile.Inventory.Entries.ToDictionary(kv => kv.Key, kv => kv.Value);

        Assert.Equal(BlueprintResult.Ok, Blueprints.Check(sim, tile, Me, bp, 20, 20, 0, false, out var plan));
        Assert.Equal(cost, plan.Cost);
        sim.Enqueue(new PlaceBlueprintCommand(Me, tile.Id, bp, 20, 20));
        sim.Step();
        foreach (var (id, n) in cost)
            Assert.Equal(before[id] - n, Items(tile, id));
        Assert.Equal("smelt_iron", tile.Grid.BuildingAt(21, 20)!.RecipeId);
        Assert.Equal(16, tile.Grid.FrameCount);
    }

    [Fact]
    public void ABlueprintIsRefusedWhollyIfAnyBlockIsBlocked()
    {
        var sim = NewSim();
        var tile = Tile(sim, size: 40);
        LayDesign(sim, tile, 5, 5);
        var bp = Blueprints.FromUnitCore(sim, tile, Me, 5, 5, out _)!;
        Place(sim, tile, "conveyor", 23, 21); // where the last thruster would go
        sim.Step();
        int buildings = tile.Grid.BuildingCount, frames = tile.Grid.FrameCount;
        var inventory = tile.Inventory.Entries.ToList();

        Assert.Equal(BlueprintResult.Blocked, Blueprints.Check(sim, tile, Me, bp, 20, 20, 0, false, out var plan));
        var problem = Assert.Single(plan.Problems);
        Assert.Equal((23, 21, PlaceResult.Blocked), (problem.X, problem.Y, problem.Result));
        Assert.Equal("thruster", plan.Placed.Blocks[problem.Block].DefId);

        sim.Enqueue(new PlaceBlueprintCommand(Me, tile.Id, bp, 20, 20));
        sim.Step();
        Assert.Equal((buildings, frames), (tile.Grid.BuildingCount, tile.Grid.FrameCount));
        Assert.Equal(inventory, tile.Inventory.Entries.ToList());

        // Off the tile's edge counts too.
        Assert.Equal(BlueprintResult.Blocked, Blueprints.Check(sim, tile, Me, bp, 38, 38, 0, false, out _));
    }

    [Fact]
    public void ABlueprintThatCantBeAffordedIsRefused()
    {
        var sim = NewSim();
        var tile = sim.AddTile(0, 0, Me, 30);
        tile.Inventory.Add("copper_ore", 5);
        var belts = new Blueprint([.. Enumerable.Range(0, 6).Select(i => new BlueprintBlock("conveyor", i, 0, 0))], []);
        Assert.Equal(BlueprintResult.CannotAfford, Blueprints.Check(sim, tile, Me, belts, 2, 2, 0, false, out var plan));
        Assert.Equal(6, plan.Cost["copper_ore"]);
        sim.Enqueue(new PlaceBlueprintCommand(Me, tile.Id, belts, 2, 2));
        sim.Step();
        Assert.Equal(0, tile.Grid.BuildingCount);
        Assert.Equal(5, Items(tile, "copper_ore"));
    }

    [Fact]
    public void AreaRecordingTakesWholeBuildingsAndFrameButNotTheTileCore()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Place(sim, tile, "core", 2, 2);
        Belt(sim, tile, 6, 3, 3);
        Place(sim, tile, "smelter", 9, 3, recipe: "smelt_copper"); // half in the box: left out
        Frame(sim, tile, 6, 5, 2);
        sim.Step();

        var bp = Blueprints.FromArea(tile, 2, 2, 8, 5)!;
        Assert.Equal(["conveyor", "conveyor", "conveyor"], bp.Blocks.Select(b => b.DefId));
        Assert.Equal(new BlueprintBlock("conveyor", 0, 0, 0), bp.Blocks[0]);
        Assert.Equal([new BlueprintCell(0, 2), new BlueprintCell(1, 2)], bp.Frames);
        Assert.Null(Blueprints.FromArea(tile, 20, 20, 3, 3));
    }

    [Fact]
    public void RotatingAndFlippingTurnBlocksWithTheBlueprint()
    {
        var defs = TestContent.Load().Buildings;
        // A belt running east, then a 2x2 smelter beside it, and a frame cell.
        var bp = new Blueprint([new BlueprintBlock("conveyor", 0, 0, 0), new BlueprintBlock("smelter", 1, 0, 0)], [new BlueprintCell(0, 1)]);

        var turned = Blueprints.Transform(defs, bp, 1, false);
        Assert.Equal((2, 3), turned.Size(defs));
        Assert.Contains(new BlueprintBlock("conveyor", 1, 0, 1), turned.Blocks); // now runs south
        Assert.Contains(new BlueprintBlock("smelter", 0, 1, 1), turned.Blocks);
        Assert.Equal([new BlueprintCell(0, 0)], turned.Frames);

        var flipped = Blueprints.Transform(defs, bp, 0, true);
        Assert.Contains(new BlueprintBlock("conveyor", 2, 0, 2), flipped.Blocks); // now runs west, on the right
        Assert.Contains(new BlueprintBlock("smelter", 0, 0, 2), flipped.Blocks);

        // Four quarter turns, or two flips, give the original back.
        var round = bp;
        for (int i = 0; i < 4; i++)
            round = Blueprints.Transform(defs, round, 1, false);
        Assert.Equal(bp.Blocks, round.Blocks);
        Assert.Equal(bp.Frames, round.Frames);
        var twice = Blueprints.Transform(defs, Blueprints.Transform(defs, bp, 0, true), 0, true);
        Assert.Equal(bp.Blocks, twice.Blocks);
    }

    [Fact]
    public void ATurnedUnitBlueprintLiftsOffAsTheSameUnitTurned()
    {
        var sim = NewSim();
        var tile = Tile(sim, size: 40);
        LayDesign(sim, tile, 5, 5);
        var bp = Blueprints.FromUnitCore(sim, tile, Me, 5, 5, out _)!;
        var original = LiftOff(sim, tile, 5, 5);

        sim.Enqueue(new PlaceBlueprintCommand(Me, tile.Id, bp, 20, 20, Rotation: 2));
        sim.Step();
        // Half a turn puts the core at the far corner of the 4x2 box.
        var copy = LiftOff(sim, tile, 23, 21);
        Assert.Equal(original.Mass, copy.Mass);
        Assert.Equal(original.MaxSpeed, copy.MaxSpeed);
        Assert.Equal(original.Blocks.Select(b => (b.DefId, -b.GX - b.Width + 1, -b.GY - b.Height + 1, (b.Rotation + 2) & 3)).OrderBy(x => x),
            copy.Blocks.Select(b => (b.DefId, b.GX, b.GY, b.Rotation)).OrderBy(x => x));
    }

    // --- lift-off validation (UD-003) ---

    [Fact]
    public void ADesignWithoutEnoughOnboardPowerIsRefusedNamingTheBlocks()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Frame(sim, tile, 5, 5, 5, 3);
        Place(sim, tile, "unit_core", 5, 5);
        Place(sim, tile, "thruster", 5, 6);
        Place(sim, tile, "steel_mill", 6, 5); // draws 30, and nothing on board supplies it
        sim.Step();

        var result = UnitAssembly.CheckActivate(sim, tile, Me, 5, 5, UnitAssembly.MaxCells, out var plan, out var offending);
        Assert.Equal(AssembleResult.NoPower, result);
        Assert.Null(plan);
        Assert.Equal("steel_mill", Assert.Single(offending).DefId);
        // An invalid design can't be recorded for production either.
        Assert.Null(Blueprints.FromUnitCore(sim, tile, Me, 5, 5, out var recorded));
        Assert.Equal(AssembleResult.NoPower, recorded);
    }

    [Fact]
    public void RefusalsListEveryOffendingBlock()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Frame(sim, tile, 3, 3, 5);
        Place(sim, tile, "unit_core", 3, 3);
        Place(sim, tile, "unit_core", 5, 3);
        Place(sim, tile, "unit_core", 7, 3);
        sim.Step();
        var result = UnitAssembly.CheckActivate(sim, tile, Me, 3, 3, UnitAssembly.MaxCells, out _, out var offending);
        Assert.Equal(AssembleResult.MultipleCores, result);
        Assert.Equal([5, 7], offending.Select(b => b.X));
    }

    [Fact]
    public void AFactoryCapReplacesTheHandBuiltLimit()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Frame(sim, tile, 3, 3, 5);
        Place(sim, tile, "unit_core", 3, 3);
        Place(sim, tile, "thruster", 4, 3);
        sim.Step();
        Assert.Equal(AssembleResult.TooLarge, UnitAssembly.CheckActivate(sim, tile, Me, 3, 3, 4, out _, out _));
        Assert.Equal(AssembleResult.Ok, UnitAssembly.CheckActivate(sim, tile, Me, 3, 3, 5, out _, out _));
    }

    // --- derived stats (UD-002, WW-001) ---

    [Fact]
    public void StatsAreDerivedFromTheBlocks()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Frame(sim, tile, 9, 10, 3);
        Place(sim, tile, "thruster", 9, 10);
        Place(sim, tile, "unit_core", 10, 10);
        Place(sim, tile, "thruster", 11, 10);
        sim.Step();
        var bp = Blueprints.FromUnitCore(sim, tile, Me, 10, 10, out _)!;
        var unit = LiftOff(sim, tile, 10, 10);
        var defs = sim.Content.Buildings;

        var stats = UnitStats.Of(sim.Content, unit);
        Assert.Equal(stats, UnitStats.Of(sim.Content, bp) with { Cost = stats.Cost, Upkeep = stats.Upkeep });
        Assert.Equal((3, 3), (stats.Cells, stats.Blocks));
        Assert.Equal(unit.Mass, stats.Mass);
        Assert.Equal(2 * defs["thruster"].Hp + defs["unit_core"].Hp, stats.MaxHp);
        Assert.Equal(stats.MaxHp, stats.Hp);
        // Thrusters cost 15 each, the core 15, and 3 frame cells at 2 iron ore.
        Assert.Equal((15 * 3 + 2 * 3) * Balance.BuildTicksPerItem, stats.BuildTicks);
        Assert.Equal(20, stats.Cost["iron_plate"]);
        Assert.Equal(6, stats.Cost["iron_ore"]);
        Assert.Equal(15, stats.Cost["steel"]);
        Assert.Equal(0, stats.Dps);
        Assert.Empty(stats.Upkeep);
        Assert.False(stats.IsWunderwaffe);
    }

    [Fact]
    public void BigUnitsAreWunderwaffeClass()
    {
        var content = TestContent.Load();
        var small = UnitStats.Of(content, [("unit_core", 1)], Balance.WunderwaffeCells);
        var big = UnitStats.Of(content, [("unit_core", 1)], Balance.WunderwaffeCells + 1);
        Assert.False(small.IsWunderwaffe);
        Assert.True(big.IsWunderwaffe);
    }

    // --- upkeep (UD-013) ---

    /// <summary>Content where each thruster costs 1 iron ore a minute.</summary>
    static Simulation UpkeepSim() =>
        new(TestContent.Load(json => json.Replace("\"kind\": \"thruster\",", "\"kind\": \"thruster\", \"upkeep\": { \"iron_ore\": 1 },")));

    /// <summary>A scout next to a core, whose resource radius (FB-018) pays its upkeep from the tile.</summary>
    static Unit Scout(Simulation sim, TileState tile)
    {
        Place(sim, tile, "core", 4, 14);
        Frame(sim, tile, 9, 10, 3);
        Place(sim, tile, "thruster", 9, 10);
        Place(sim, tile, "unit_core", 10, 10);
        Place(sim, tile, "thruster", 11, 10);
        sim.Step();
        return LiftOff(sim, tile, 10, 10);
    }

    [Fact]
    public void UpkeepIsPaidEachMinuteFromTheSupplierInReach()
    {
        var sim = UpkeepSim();
        var tile = Tile(sim);
        var unit = Scout(sim, tile);
        Assert.Equal(2, UnitStats.Of(sim.Content, unit).Upkeep["iron_ore"]);
        long iron = Items(tile, "iron_ore");

        Run(sim, Balance.UpkeepPeriod - 2);
        Assert.Equal(iron, Items(tile, "iron_ore"));
        Run(sim, 2);
        Assert.Equal(iron - 2, Items(tile, "iron_ore"));
        Assert.False(unit.Unsupplied);
    }

    [Fact]
    public void UpkeepComesFromOnboardStorageFirst()
    {
        var sim = UpkeepSim();
        var tile = Tile(sim);
        var unit = Scout(sim, tile);
        unit.Cargo.Add("iron_ore", 5);
        long iron = Items(tile, "iron_ore");
        Run(sim, Balance.UpkeepPeriod);
        Assert.Equal(3, unit.Cargo.Get("iron_ore"));
        Assert.Equal(iron, Items(tile, "iron_ore"));
    }

    [Fact]
    public void AUnitThatCantPayUpkeepStallsUntilItCan()
    {
        var sim = UpkeepSim();
        var tile = sim.AddTile(0, 0, Me, 30);
        foreach (var item in new[] { "copper_plate", "iron_plate", "steel" })
            tile.Inventory.Add(item, 1000);
        tile.Inventory.Add("iron_ore", 6); // the frame, and nothing left over
        var unit = Scout(sim, tile);
        Run(sim, Balance.UpkeepPeriod);
        Assert.True(unit.Unsupplied);

        sim.Enqueue(new MoveUnitsCommand(Me, [unit.Id], tile.Id, 20, 20));
        Run(sim, 2);
        Assert.False(unit.Moving);

        tile.Inventory.Add("iron_ore", 10);
        Run(sim, Simulation.TicksPerSecond);
        Assert.False(unit.Unsupplied);
        Assert.Equal(8, Items(tile, "iron_ore"));
        sim.Enqueue(new MoveUnitsCommand(Me, [unit.Id], tile.Id, 20, 20));
        Run(sim, 2);
        Assert.True(unit.Moving);
    }
}
