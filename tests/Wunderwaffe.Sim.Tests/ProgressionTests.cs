using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

public class ProgressionTests
{
    const string Items = """
        [ { "id": "ore", "name": "Ore", "tier": 1 }, { "id": "bar", "name": "Bar", "tier": 2 } ]
        """;

    const string Recipes = """
        [
          { "id": "mine_ore", "building": "drill", "ticks": 10, "inputs": {}, "outputs": { "ore": 1 } },
          { "id": "smelt_bar", "building": "furnace", "ticks": 10, "inputs": { "ore": 1 }, "outputs": { "bar": 1 } },
          { "id": "smelt_fast", "building": "furnace", "ticks": 5, "inputs": { "ore": 1 }, "outputs": { "bar": 1 } }
        ]
        """;

    const string Buildings = """
        [
          { "id": "core", "name": "Core", "kind": "core", "width": 1, "height": 1, "power": 0, "hp": 100, "cost": {} },
          { "id": "drill", "name": "Drill", "width": 1, "height": 1, "power": 0, "hp": 10, "cost": {} },
          { "id": "furnace", "name": "Furnace", "width": 1, "height": 1, "power": 0, "hp": 10, "cost": {} },
          { "id": "wall", "name": "Wall", "width": 1, "height": 1, "power": 0, "hp": 10, "cost": {} }
        ]
        """;

    const string Goals = """
        [
          { "id": "a", "name": "A", "description": "", "condition": { "type": "claim", "amount": 1 } },
          { "id": "b", "name": "B", "description": "", "condition": { "type": "produce", "item": "ore", "amount": 2 } }
        ]
        """;

    /// <summary>A 10x10 tile owned by <see cref="Harness.Me"/> with an ore deposit under (0, 0).</summary>
    static (Simulation Sim, TileState Tile) Small()
    {
        var sim = new Simulation(ContentDb.FromJson(Items, Recipes, Buildings, Goals));
        var tile = sim.AddTile(0, 0, Me, size: 10);
        tile.Grid.SetDeposit(0, 0, "ore");
        return (sim, tile);
    }

    static PlaceResult Check(Simulation sim, TileState tile, string building, int x, int y, string? recipe = null) =>
        Placement.Check(sim, tile, Me, building, x, y, 0, recipe, out _);

    [Fact]
    public void EveryBuildingAndRecipeIsAvailableFromTheStart()
    {
        // TT-001: there is no tech tree.
        var (sim, tile) = Small();
        Assert.Equal(PlaceResult.Ok, Check(sim, tile, "drill", 0, 0));
        Assert.Equal(PlaceResult.Ok, Check(sim, tile, "wall", 5, 5));
        Assert.Equal(PlaceResult.Ok, Check(sim, tile, "furnace", 5, 5, "smelt_fast"));
        Assert.Equal(PlaceResult.Ok, Check(sim, tile, "furnace", 5, 5, "smelt_bar"));
    }

    [Fact]
    public void ProductionCountsForTheTileOwnerOnly()
    {
        var (sim, tile) = Small();
        var theirs = sim.AddTile(1, 0, Them, size: 10);
        theirs.Grid.SetDeposit(0, 0, "ore");
        sim.Enqueue(new PlaceBuildingCommand(Them, theirs.Id, "drill", 0, 0));
        sim.Enqueue(new PlaceBuildingCommand(Them, theirs.Id, "core", 1, 0));
        Run(sim, 40);
        Assert.Equal(0, sim.PlayerOrNull(Me)?.ProducedOf("ore") ?? 0);
        Assert.True(sim.PlayerOrNull(Them)!.ProducedOf("ore") >= 3);
    }

    [Fact]
    public void GoalsCompleteInOrder()
    {
        var (sim, tile) = Small();
        sim.Step();
        // The player owns one tile, so goal A is met on the first tick; B needs ore.
        var me = sim.PlayerOrNull(Me)!;
        Assert.Equal(1, me.GoalsDone);
        Assert.Equal("b", Progression.CurrentGoal(sim, Me)!.Id);
        Assert.Equal(0, Progression.Progress(sim, Me, Progression.CurrentGoal(sim, Me)!.Condition));

        sim.Enqueue(new PlaceBuildingCommand(Me, tile.Id, "drill", 0, 0));
        sim.Enqueue(new PlaceBuildingCommand(Me, tile.Id, "core", 1, 0));
        Run(sim, 30);
        Assert.Equal(2, me.GoalsDone);
        Assert.Null(Progression.CurrentGoal(sim, Me));
        Assert.True(me.GoalTicks[0] < me.GoalTicks[1]);
    }

    [Fact]
    public void ShippingAndLaunchingAreCounted()
    {
        var sim = NewSim();
        var a = Tile(sim, 0, 0);
        var b = Tile(sim, 1, 0);
        Ore(a, "iron_ore", 20, 10);
        Place(sim, a, "drill", 20, 10);
        Belt(sim, a, 22, 10, 7);
        Place(sim, a, "export_port", 29, 10, 0);
        Place(sim, b, "import_port", 0, 10, 2);
        Belt(sim, b, 1, 10, 3);
        Place(sim, b, "core", 4, 9);
        Run(sim, 60 * 20);
        long shipped = sim.PlayerOrNull(Me)!.Shipped;
        Assert.True(shipped >= 5, $"shipped {shipped}");

        for (int x = 9; x <= 11; x++)
            sim.Enqueue(new PlaceFrameCommand(Me, a.Id, x, 5));
        Place(sim, a, "unit_core", 10, 5);
        Place(sim, a, "thruster", 11, 5);
        sim.Step();
        sim.Enqueue(new ActivateUnitCommand(Me, a.Id, 10, 5));
        sim.Step();
        Assert.Equal(1, sim.PlayerOrNull(Me)!.Launched);
    }

    [Fact]
    public void RealContentChainIsWellFormed()
    {
        var content = TestContent.Load();
        Assert.NotEmpty(content.Goals);
        Assert.True(content.Items["coal"].Regional);
    }

    [Theory]
    [InlineData("""[ { "id": "x", "name": "X", "description": "", "condition": { "type": "dance", "amount": 1 } } ]""", "unknown condition type")]
    [InlineData("""[ { "id": "x", "name": "X", "description": "", "condition": { "type": "produce", "item": "gold", "amount": 1 } } ]""", "unknown condition item")]
    [InlineData("""[ { "id": "x", "name": "X", "description": "", "condition": { "type": "claim", "amount": 0 } } ]""", "amount must be > 0")]
    public void InvalidGoalsAreRejected(string goals, string error)
    {
        var ex = Assert.Throws<ContentException>(() => ContentDb.FromJson(Items, Recipes, Buildings, goals));
        Assert.Contains(ex.Errors, e => e.Contains(error));
    }

    [Fact]
    public void MachineStatusSaysWhyItIsIdle()
    {
        // FB-006: a starved machine and one with nowhere to put its output read differently.
        var (sim, tile) = Small();
        sim.Enqueue(new PlaceBuildingCommand(Me, tile.Id, "drill", 0, 0));
        sim.Enqueue(new PlaceBuildingCommand(Me, tile.Id, "furnace", 5, 5, RecipeId: "smelt_bar"));
        Run(sim, 2);
        var drill = tile.Grid.BuildingAt(0, 0)!.Machine!;
        var furnace = tile.Grid.BuildingAt(5, 5)!.Machine!;
        Assert.Equal(MachineStatus.Working, drill.Status);
        Assert.Equal(MachineStatus.Starved, furnace.Status);
        Assert.Equal("ore", furnace.MissingInput);
        Assert.Equal(2, furnace.IdleTicks);

        // Nothing takes the drill's ore: it fills up and stops.
        Run(sim, 10 * (MachineState.OutputCapacity + 2));
        Assert.Equal(MachineStatus.OutputFull, drill.Status);
        Assert.True(drill.IdleTicks > 0);
    }
}
