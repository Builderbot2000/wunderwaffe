using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

/// <summary>FB-023 building relocation: cut and paste on one tile, grafting onto a landed unit.</summary>
public class RelocationTests
{
    static void Move(Simulation sim, TileState tile, int fromX, int fromY, int x, int y, int rotation = 0, TileState? to = null, int player = Me)
    {
        sim.Enqueue(new MoveBuildingCommand(player, tile.Id, fromX, fromY, (to ?? tile).Id, x, y, rotation));
        sim.Step();
    }

    static long FundsOf(TileState tile) =>
        tile.Grid.Cores.Sum(c => c.Stock!.Entries.Sum(e => (long)e.Value));

    [Fact]
    public void AMovedBuildingKeepsItsStateAndCostsNothing()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Ore(tile, "iron_ore", 2, 2);
        Ore(tile, "iron_ore", 27, 28);
        Place(sim, tile, "drill", 2, 2);
        sim.Step();
        sim.Enqueue(new DebugDamageCommand(Me, tile.Id, 2, 2, 30));
        // With nowhere to put its ore the drill fills up and sleeps.
        Run(sim, 3000);
        var drill = tile.Grid.BuildingAt(2, 2)!;
        int held = drill.Machine!.OutputCount, hp = drill.Hp;
        Assert.True(held > 0);
        Assert.True(hp < sim.Content.Buildings["drill"].Hp);
        long funds = FundsOf(tile), ore = Items(tile, "iron_ore");

        // Next to the bank core, turned: it is the same building, and it wakes and empties into the core.
        Move(sim, tile, 3, 3, 27, 28, rotation: 1);
        Assert.Null(tile.Grid.BuildingAt(2, 2));
        var moved = tile.Grid.BuildingAt(28, 29)!;
        Assert.Same(drill, moved);
        Assert.Equal((27, 28, 1), (moved.X, moved.Y, moved.Rotation));
        Assert.Equal(hp, moved.Hp);
        Assert.Equal("drill", moved.DefId);
        Assert.NotNull(moved.RecipeId);
        Assert.Equal(funds, FundsOf(tile));
        Run(sim, 60);
        Assert.True(Items(tile, "iron_ore") > ore);
    }

    [Fact]
    public void RefusedPastesLeaveTheBuildingWhereItWas()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        var other = Tile(sim, 30, 0);
        Foothold(sim, tile, Them, 2, 20);
        Ore(tile, "iron_ore", 2, 2);
        Place(sim, tile, "drill", 2, 2);
        Place(sim, tile, "conveyor", 10, 10);
        sim.Step();
        var drill = tile.Grid.BuildingAt(2, 2)!;
        PlaceResult Check(int fromX, int fromY, int x, int y, TileState? to = null, int player = Me) =>
            Relocation.Check(sim, tile, player, fromX, fromY, to ?? tile, x, y, 0, out _);

        Assert.Equal(PlaceResult.OtherTile, Check(2, 2, 2, 2, other));
        Assert.Equal(PlaceResult.NeedsDeposit, Check(2, 2, 5, 5));
        Assert.Equal(PlaceResult.Blocked, Check(10, 10, 2, 20));
        Assert.Equal(PlaceResult.OutOfBounds, Check(10, 10, 30, 10));
        Assert.Equal(PlaceResult.NotOwner, Check(2, 20, 14, 14));
        Assert.Equal(PlaceResult.NoSuchBuilding, Check(20, 20, 21, 21));
        // Its own cells count as free: a drill can shift by one onto the rest of its ore.
        Ore(tile, "iron_ore", 4, 2, 1, 2);
        Assert.Equal(PlaceResult.Ok, Check(2, 2, 3, 2));

        int revision = tile.Grid.Revision;
        Move(sim, tile, 2, 2, 2, 2, to: other);
        Move(sim, tile, 2, 2, 5, 5);
        Move(sim, tile, 2, 20, 14, 14);
        Assert.Same(drill, tile.Grid.BuildingAt(2, 2));
        Assert.Equal(Them, tile.Grid.BuildingAt(2, 20)!.Owner);
        Assert.Equal(revision, tile.Grid.Revision);
    }

    [Fact]
    public void AGraftFliesOutAndCanBeCutOffAfterLanding()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        LayScout(sim, tile, 10, 10);
        sim.Enqueue(new PlaceFrameCommand(Me, tile.Id, 13, 10));
        sim.Enqueue(new PlaceFrameCommand(Me, tile.Id, 13, 11));
        Place(sim, tile, "wall", 20, 20);
        sim.Step();
        var wall = tile.Grid.BuildingAt(20, 20)!;
        sim.Enqueue(new DebugDamageCommand(Me, tile.Id, 20, 20, 100));
        sim.Step();
        int hp = wall.Hp;

        // Pasted onto the frame it is a block of the unit, and lift-off validation still decides.
        Move(sim, tile, 20, 20, 13, 10);
        Assert.Equal(AssembleResult.Ok, UnitAssembly.CheckActivate(sim, tile, Me, 10, 10, out var plan));
        Assert.Contains(wall, plan!.Buildings);
        var unit = LiftOff(sim, tile, 10, 10);
        Assert.Null(tile.Grid.BuildingAt(13, 10));
        var block = Assert.Single(unit.Blocks, b => b.DefId == "wall");
        Assert.Equal(hp, block.Hp);

        sim.Enqueue(new DeployUnitCommand(Me, unit.Id));
        sim.Step();
        Assert.Empty(sim.Units);
        var landed = tile.Grid.Buildings.Single(b => b.DefId == "wall");
        Assert.Equal(hp, landed.Hp);
        Move(sim, tile, landed.X, landed.Y, 20, 20);
        Assert.Equal("wall", tile.Grid.BuildingAt(20, 20)!.DefId);
        Assert.Equal(hp, tile.Grid.BuildingAt(20, 20)!.Hp);
    }

    [Fact]
    public void AGraftThatBreaksLiftOffIsRefusedAtLiftOff()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        LayScout(sim, tile, 10, 10);
        sim.Enqueue(new PlaceFrameCommand(Me, tile.Id, 13, 10));
        Place(sim, tile, "repair_bay", 20, 20);
        sim.Step();

        // Half on the frame: the paste is fine, flying it out is not (UD-003).
        Move(sim, tile, 20, 20, 13, 10);
        Assert.Equal("repair_bay", tile.Grid.BuildingAt(13, 10)!.DefId);
        Assert.Equal(AssembleResult.PartialBuilding, UnitAssembly.CheckActivate(sim, tile, Me, 10, 10, out _));
    }

    [Fact]
    public void MovesReplayToTheSameState()
    {
        Simulation Play()
        {
            var sim = NewSim();
            var tile = Tile(sim);
            Belt(sim, tile, 2, 2, 5);
            sim.Step();
            Move(sim, tile, 6, 2, 6, 3, rotation: 1);
            Run(sim, 30);
            return sim;
        }

        var a = Play();
        var b = Play();
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal(1, a.Tiles[0].Grid.BuildingAt(6, 3)!.Rotation);

        var unmoved = NewSim();
        var t = Tile(unmoved);
        Belt(unmoved, t, 2, 2, 5);
        unmoved.Step();
        unmoved.Step();
        Run(unmoved, 30);
        Assert.NotEqual(a.StateHash(), unmoved.StateHash());
    }
}
