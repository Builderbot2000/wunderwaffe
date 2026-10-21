using Wunderwaffe.Sim.Api;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

/// <summary>
/// WM-015 / OQ-58: building only inside a build radius of the player's major buildings or flying units, on that tile.
/// No bank here: its radius covers any tile (<see cref="Foothold"/>).
/// </summary>
public class BuildRadiusTests
{
    const int Size = 96;

    /// <summary>A tile of mine with a stocked real core (2x2, build radius 24) at (40, 40) and nothing else.</summary>
    static (Simulation Sim, TileState Tile, PlacedBuilding Core) CoreTile()
    {
        var sim = NewSim();
        var tile = sim.AddTile(0, 0, Me, Size);
        var core = PutCore(sim, tile, Me, 40, 40);
        foreach (var item in new[] { "copper_ore", "iron_ore", "copper_plate", "iron_plate", "steel" })
            Stock(core, item, 1000);
        return (sim, tile, core);
    }

    static PlaceResult Check(Simulation sim, TileState tile, string building, int x, int y, int player = Me) =>
        Placement.Check(sim, tile, player, building, x, y, 0, null, out _);

    static void Plant(Simulation sim, TileState tile, string building, int x, int y, int player = Me)
    {
        var def = sim.Content.Buildings[building];
        tile.Grid.Add(def, player, x, y, def.Width, def.Height, 0, null);
    }

    [Fact]
    public void MajorBuildingsGiveARadiusFromTheMomentTheyArePlacedMinorOnesNone()
    {
        var (sim, tile, _) = CoreTile();
        int r = sim.Content.Buildings["core"].BuildRadius;
        Assert.True(r > 0);
        Assert.Equal(0, sim.Content.Buildings["conveyor"].BuildRadius);
        // Measured from the footprint's nearest cell: the core's east column is 41.
        Assert.Equal(PlaceResult.Ok, Check(sim, tile, "conveyor", 41 + r, 40));
        Assert.Equal(PlaceResult.OutOfRange, Check(sim, tile, "conveyor", 41 + r + 1, 40));
        Assert.Equal(PlaceResult.OutOfRange, Check(sim, tile, "conveyor", 41 + r, 40 - r)); // straight-line, not square

        // A conveyor at the edge reaches no further; a turret placed there does, at once.
        Place(sim, tile, "conveyor", 41 + r, 41);
        sim.Step();
        Assert.Equal(PlaceResult.OutOfRange, Check(sim, tile, "conveyor", 41 + r + 1, 41));
        Place(sim, tile, "gun_turret", 41 + r, 40);
        sim.Step();
        int turret = sim.Content.Buildings["gun_turret"].BuildRadius;
        Assert.True(turret > 0);
        Assert.Equal(PlaceResult.Ok, Check(sim, tile, "conveyor", 41 + r + turret, 40));
        Assert.Equal(PlaceResult.OutOfRange, Check(sim, tile, "conveyor", 41 + r + turret + 1, 40));

        // The query lists them, the core first, for the UI and drivers.
        var radii = new GameQuery(sim, Me).BuildRadii(tile.Id);
        Assert.Equal([(40, 40, r), (41 + r, 40, turret)], radii.Select(b => (b.X, b.Y, b.Radius)));
        Assert.Empty(new GameQuery(sim, Them).BuildRadii(tile.Id));
    }

    [Fact]
    public void AFlyingUnitsRadiusFollowsItsLiveBlocks()
    {
        var sim = NewSim();
        var tile = Tile(sim, 0, 0, Size);
        var unit = Scout(sim, tile, 40, 40);
        tile.Grid.Remove(tile.Grid.Cores.Single(c => c.DefId == "bank")); // only the unit reaches now

        int full = UnitStats.BuildRadius(unit);
        Assert.Equal(Balance.VehicleRadiusBase + 3 * Balance.VehicleRadiusPerBlock, full);
        var mine = Assert.Single(Territory.BuildRadii(sim, Me, tile));
        Assert.Equal((unit.Id, full), (mine.UnitId, mine.Radius));
        Assert.NotEqual(PlaceResult.OutOfRange, Check(sim, tile, "conveyor", mine.X + full, mine.Y));
        Assert.Equal(PlaceResult.OutOfRange, Check(sim, tile, "conveyor", mine.X + full + 1, mine.Y));

        // A lost block shrinks it.
        Combat.DamageBlock(sim, unit, unit.Blocks.ToList().FindIndex(b => b.DefId == "thruster"), 10_000, "kinetic");
        Assert.Equal(full - Balance.VehicleRadiusPerBlock, UnitStats.BuildRadius(unit));
        Assert.Equal(PlaceResult.OutOfRange, Check(sim, tile, "conveyor", mine.X + full, mine.Y));
        Assert.Equal(Balance.VehicleRadiusMax, UnitStats.BuildRadius(1000));
    }

    [Fact]
    public void ARadiusStopsAtItsTilesBorder()
    {
        var sim = NewSim();
        var west = sim.AddTile(0, 0, Me, Size);
        var east = sim.AddTile(1, 0, Me, Size);
        PutCore(sim, west, Me, Size - 2, 40);
        Plant(sim, east, "conveyor", 50, 50); // presence on east, but no reach
        Assert.Equal(PlaceResult.OutOfRange, Check(sim, east, "conveyor", 0, 40));
        Assert.Equal(PlaceResult.CannotAfford, Check(sim, west, "conveyor", Size - 3, 40));
    }

    [Fact]
    public void EveryBuildingOperationNeedsTheRadius()
    {
        var (sim, tile, _) = CoreTile();
        int far = 5, near = 30;
        // Placing a building, a frame, a blueprint.
        Assert.Equal(PlaceResult.OutOfRange, Check(sim, tile, "drill", far, far));
        Assert.Equal(PlaceResult.OutOfRange, UnitAssembly.CheckFrame(sim, tile, Me, far, far));
        Assert.Equal(PlaceResult.Ok, UnitAssembly.CheckFrame(sim, tile, Me, near, near));
        var belts = new Blueprint([new BlueprintBlock("conveyor", 0, 0, 0), new BlueprintBlock("conveyor", 1, 0, 0)], []);
        Assert.Equal(BlueprintResult.Blocked, Blueprints.Check(sim, tile, Me, belts, far, far, 0, false, out var plan));
        Assert.All(plan.Problems, p => Assert.Equal(PlaceResult.OutOfRange, p.Result));
        Assert.Equal(BlueprintResult.Ok, Blueprints.Check(sim, tile, Me, belts, near, near, 0, false, out _));

        // Removing a building, a frame, a wreck: refused outside, done inside.
        foreach (int at in new[] { far, near })
        {
            Plant(sim, tile, "conveyor", at, at);
            tile.Grid.Lay(sim.Content.Frame!, at, at + 1);
            Plant(sim, tile, "wall", at, at + 2);
            Combat.DestroyBuilding(sim, tile, tile.Grid.BuildingAt(at, at + 2)!);
            sim.Enqueue(new RemoveBuildingCommand(Me, tile.Id, at, at));
            sim.Enqueue(new RemoveFrameCommand(Me, tile.Id, at, at + 1));
            sim.Enqueue(new RemoveBuildingCommand(Me, tile.Id, at, at + 2));
            sim.Step();
            bool inside = at == near;
            Assert.Equal(inside, tile.Grid.BuildingAt(at, at) == null);
            Assert.Equal(inside, !tile.Grid.Has(Content.BuildingKind.Frame, at, at + 1));
            Assert.Equal(inside, tile.Grid.WreckAt(at, at + 2) == null);
        }
    }
}
