using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Tests;

public class PlacementTests
{
    const int Player = 0;

    static (Simulation, TileState) NewSim()
    {
        var sim = new Simulation(TestContent.Load());
        var tile = sim.AddTile(0, 0, Player, size: 10);
        tile.Grid.SetDeposit(0, 0, "iron_ore");
        tile.Inventory.Add("copper_ore", 1000);
        tile.Inventory.Add("iron_ore", 1000);
        return (sim, tile);
    }

    static PlaceResult Check(Simulation sim, TileState tile, string building, int x, int y, int rot = 0, string? recipe = null, int player = Player) =>
        Placement.Check(sim, tile, player, building, x, y, rot, recipe, out _);

    [Fact]
    public void RejectsOutOfBoundsAndOverlap()
    {
        var (sim, tile) = NewSim();
        Assert.Equal(PlaceResult.OutOfBounds, Check(sim, tile, "smelter", 9, 0, recipe: "smelt_iron"));
        Assert.Equal(PlaceResult.OutOfBounds, Check(sim, tile, "conveyor", -1, 0));

        sim.Enqueue(new PlaceBuildingCommand(Player, 0, "smelter", 4, 4, RecipeId: "smelt_iron"));
        sim.Step();
        Assert.Equal(PlaceResult.Blocked, Check(sim, tile, "conveyor", 5, 5));
        Assert.Equal(PlaceResult.Ok, Check(sim, tile, "conveyor", 6, 4));
    }

    [Fact]
    public void ExtractorsNeedADepositAndPickItsRecipe()
    {
        var (sim, tile) = NewSim();
        Assert.Equal(PlaceResult.NeedsDeposit, Check(sim, tile, "drill", 5, 5));

        sim.Enqueue(new PlaceBuildingCommand(Player, 0, "drill", 0, 0));
        sim.Step();
        Assert.Equal("mine_iron_ore", tile.Grid.BuildingAt(1, 1)!.RecipeId);
    }

    [Fact]
    public void PlacedBuildingsBelongToTheirPlacerWithFullHitPoints()
    {
        var (sim, tile) = NewSim();
        sim.Enqueue(new PlaceBuildingCommand(Player, 0, "conveyor", 5, 5));
        sim.Step();
        var belt = tile.Grid.BuildingAt(5, 5)!;
        Assert.Equal(Player, belt.Owner);
        Assert.Equal(sim.Content.Buildings["conveyor"].Hp, belt.Hp);

        var start = new Simulation(TestContent.Load()).AddStartingTile(0, 0, 1, seed: 1);
        Assert.All(start.Grid.Buildings, b => Assert.Equal(1, b.Owner)); // the core and the scout
    }

    [Fact]
    public void CraftersWithSeveralRecipesNeedOneChosen()
    {
        var (sim, tile) = NewSim();
        Assert.Equal(PlaceResult.InvalidRecipe, Check(sim, tile, "smelter", 4, 4));
        Assert.Equal(PlaceResult.InvalidRecipe, Check(sim, tile, "smelter", 4, 4, recipe: "make_steel"));
        Assert.Equal(PlaceResult.Ok, Check(sim, tile, "smelter", 4, 4, recipe: "smelt_copper"));
    }

    [Fact]
    public void RotationSwapsFootprint()
    {
        var content = Content.ContentDb.FromJson(
            "[]", "[]", """[ { "id": "wall", "name": "Wall", "width": 1, "height": 3, "power": 0, "hp": 10, "cost": {} } ]""");
        var sim = new Simulation(content);
        var tile = sim.AddTile(0, 0, Player, size: 10);

        Placement.Check(sim, tile, Player, "wall", 0, 0, 1, null, out var plan);
        Assert.Equal((3, 1), (plan.Width, plan.Height));
        Assert.Equal(PlaceResult.OutOfBounds, Placement.Check(sim, tile, Player, "wall", 8, 0, 1, null, out _));
        Assert.Equal(PlaceResult.Ok, Placement.Check(sim, tile, Player, "wall", 9, 0, 0, null, out _));
    }

    [Fact]
    public void PlacingPaysAndRemovingRefunds()
    {
        var (sim, tile) = NewSim();
        sim.Enqueue(new PlaceBuildingCommand(Player, 0, "smelter", 4, 4, RecipeId: "smelt_iron"));
        sim.Step();
        Assert.Equal(1000 - 20, tile.Inventory.Get("copper_ore"));
        Assert.Equal("smelt_iron", tile.Grid.BuildingAt(5, 5)!.RecipeId);

        sim.Enqueue(new RemoveBuildingCommand(Player, 0, 5, 5));
        sim.Step();
        Assert.Equal(1000, tile.Inventory.Get("copper_ore"));
        Assert.Equal(0, tile.Grid.BuildingCount);
    }

    [Fact]
    public void CannotAffordOrBuildOnSomeoneElsesTile()
    {
        var (sim, tile) = NewSim();
        Assert.Equal(PlaceResult.NoPresence, Check(sim, tile, "conveyor", 5, 5, player: 1));
        Assert.Equal(PlaceResult.CannotAfford, Check(sim, tile, "steel_mill", 5, 5));
    }

    [Fact]
    public void TheLastCoreCannotBeRemoved()
    {
        var sim = new Simulation(TestContent.Load());
        var tile = sim.AddStartingTile(0, 0, Player, seed: 1);
        var core = tile.Grid.Buildings.Single(b => b.DefId == Simulation.CoreBuildingId);
        int count = tile.Grid.BuildingCount;
        sim.Enqueue(new RemoveBuildingCommand(Player, 0, core.X, core.Y));
        sim.Step();
        Assert.Equal(count, tile.Grid.BuildingCount);
    }

    [Fact]
    public void RejectsCellsOfOtherTilesAndUnbuildableTerrain()
    {
        // On a generated world a tile's box overlaps its neighbours, and some of its ground is water or peaks.
        var sim = Simulation.NewGame(TestContent.Load(), GameSetup.Default);
        foreach (var tile in sim.Tiles)
            tile.Owner = Player;
        var coast = sim.Tiles.First(t => t.Grid.BuildableCount > 0 && t.Grid.BuildableCount < t.Grid.Area);
        coast.Inventory.Add("copper_ore", 1000);
        coast.Inventory.Add("iron_ore", 1000);

        var cells = Enumerable.Range(0, coast.Grid.Width * coast.Grid.Height)
            .Select(i => (X: i % coast.Grid.Width, Y: i / coast.Grid.Width)).ToList();
        var foreign = cells.First(c => !coast.Grid.InBounds(c.X, c.Y));
        var wet = cells.First(c => coast.Grid.InBounds(c.X, c.Y) && !coast.Grid.Buildable(c.X, c.Y));
        var dry = cells.First(c => coast.Grid.Buildable(c.X, c.Y) && coast.Grid.BuildingAt(c.X, c.Y) == null);

        Assert.Equal(PlaceResult.OutOfBounds, Check(sim, coast, "conveyor", foreign.X, foreign.Y));
        Assert.Equal(PlaceResult.BadTerrain, Check(sim, coast, "conveyor", wet.X, wet.Y));
        Assert.Equal(PlaceResult.Ok, Check(sim, coast, "conveyor", dry.X, dry.Y));
        Assert.Equal(PlaceResult.BadTerrain, UnitAssembly.CheckFrame(coast, Player, wet.X, wet.Y));
    }
}
