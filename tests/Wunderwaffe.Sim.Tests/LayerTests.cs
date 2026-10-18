using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

/// <summary>FB-022 (OQ-52): cell layers declared in content, stacking rules, and the frame and deposits as layers.</summary>
public class LayerTests
{
    const string HeavyCable =
        """{ "id": "heavy_cable", "name": "Heavy Cable", "menu": "power", "kind": "wire", "layer": "cable", "width": 1, "height": 1, "power": 0, "hp": 30, "cost": { "copper_ore": 3 } }""";

    static string AddBuilding(string buildings, string json) => buildings[..buildings.LastIndexOf(']')] + ",\n" + json + "\n]";

    static void Remove(Simulation sim, TileState tile, int x, int y)
    {
        sim.Enqueue(new RemoveBuildingCommand(Me, tile.Id, x, y));
        sim.Step();
    }

    [Fact]
    public void ASecondCableTierIsContentOnlyAndJoinsTheSameNetworks()
    {
        var sim = new Simulation(TestContent.Load(b => AddBuilding(b, HeavyCable)));
        var tile = Tile(sim);
        var grid = tile.Grid;
        int cable = grid.Layers.Of(BuildingKind.Wire);
        Harness.Ore(tile, "iron_ore", 10, 10);
        Place(sim, tile, "drill", 10, 10);
        Place(sim, tile, "core_large", 20, 10);
        Place(sim, tile, "wire", 10, 10);
        for (int x = 12; x < 20; x++)
            Place(sim, tile, x < 16 ? "wire" : "heavy_cable", x, 10);
        long copper = Items(tile, "copper_ore");
        Run(sim, 61);
        Assert.Equal(9, grid.CountOn(cable));
        Assert.Equal(4, grid.CountOn(cable, grid.Layers[cable].EntryOf("heavy_cable")));
        Assert.Equal("heavy_cable", grid.LaidAt(cable, 17, 10)!.Id);
        var drill = grid.BuildingAt(10, 10)!;
        Assert.Same(grid.Power.NetworkOf(drill), grid.Power.NetworkOf(grid.BuildingAt(20, 10)!));

        // Taking one up refunds its own cost and cuts the run.
        copper = Items(tile, "copper_ore");
        Remove(sim, tile, 17, 10);
        Assert.Equal(copper + 3, Items(tile, "copper_ore"));
        Assert.NotSame(grid.Power.NetworkOf(drill), grid.Power.NetworkOf(grid.BuildingAt(20, 10)!));
    }

    [Fact]
    public void LayersStackByTheirRulesAndComeUpTopmostFirst()
    {
        var (sim, tile, x, y) = WaterTile();
        var grid = tile.Grid;
        // Frame and cable sit on ground: bare water isn't, a platform is.
        Assert.Equal(PlaceResult.BadTerrain, UnitAssembly.CheckFrame(sim, tile, Me, x, y));
        Assert.Equal(PlaceResult.BadTerrain, Placement.Check(sim, tile, Me, "wire", x, y, 0, null, out _));
        Place(sim, tile, "platform", x, y);
        Place(sim, tile, "frame", x, y);
        Place(sim, tile, "wire", x, y);
        Place(sim, tile, "conveyor", x, y);
        sim.Step();
        Assert.True(grid.Has(BuildingKind.Platform, x, y) && grid.Has(BuildingKind.Frame, x, y) && grid.Has(BuildingKind.Wire, x, y));
        Assert.Equal("conveyor", grid.BuildingAt(x, y)?.DefId);
        // A platform doesn't go under a building; frame and cables do, but not twice.
        Assert.Equal(PlaceResult.Blocked, UnitAssembly.CheckFrame(sim, tile, Me, x, y));

        Remove(sim, tile, x, y);
        Assert.Null(grid.BuildingAt(x, y));
        Remove(sim, tile, x, y);
        Assert.False(grid.Has(BuildingKind.Wire, x, y));
        Remove(sim, tile, x, y);
        Assert.False(grid.Has(BuildingKind.Frame, x, y));
        Assert.True(grid.Has(BuildingKind.Platform, x, y));
        Remove(sim, tile, x, y);
        Assert.False(grid.Has(BuildingKind.Platform, x, y));
    }

    [Fact]
    public void ALayerCantBeTakenFromUnderOneThatSitsOnIt()
    {
        // Cables that may only lie on frame: the frame under a cable stays.
        var content = TestContent.Load(b => AddBuilding(b, HeavyCable),
            editLayers: l => l.Replace("""{ "id": "cable",    "name": "Power cables",      "sitsOn": ["ground"]""", """{ "id": "cable", "name": "Power cables", "sitsOn": ["frame"]"""));
        var sim = new Simulation(content);
        var tile = Tile(sim);
        Assert.Equal(PlaceResult.BadTerrain, Placement.Check(sim, tile, Me, "wire", 4, 4, 0, null, out _));
        sim.Enqueue(new PlaceFrameCommand(Me, tile.Id, 4, 4));
        Place(sim, tile, "wire", 4, 4);
        sim.Step();
        Assert.True(tile.Grid.Has(BuildingKind.Wire, 4, 4));
        sim.Enqueue(new RemoveFrameCommand(Me, tile.Id, 4, 4));
        sim.Step();
        Assert.True(tile.Grid.Has(BuildingKind.Frame, 4, 4));
    }

    [Fact]
    public void ALayerMarkedBlocksBuildingsKeepsBuildingsOff()
    {
        var content = TestContent.Load(b => AddBuilding(b, HeavyCable),
            editLayers: l => l.Replace("\"underBuildings\": true, \"draw\": \"links\"", "\"blocksBuildings\": true, \"draw\": \"links\""));
        var sim = new Simulation(content);
        var tile = Tile(sim);
        Place(sim, tile, "wire", 4, 4);
        sim.Step();
        Assert.Equal(PlaceResult.Blocked, Placement.Check(sim, tile, Me, "thruster", 4, 4, 0, null, out _));
        Assert.Equal(PlaceResult.Ok, Placement.Check(sim, tile, Me, "thruster", 5, 4, 0, null, out _));
    }

    [Fact]
    public void TheFrameIsABuildingWhoseCostComesFromContent()
    {
        var content = TestContent.Load(b => b.Replace(
            "\"hp\": 50, \"cost\": { \"iron_ore\": 2 } }", "\"hp\": 50, \"cost\": { \"copper_ore\": 5 } }"));
        var sim = new Simulation(content);
        var tile = Tile(sim);
        long copper = Items(tile, "copper_ore");
        sim.Enqueue(new PlaceFrameCommand(Me, tile.Id, 4, 4));
        Place(sim, tile, "frame", 5, 4);
        sim.Step();
        Assert.True(tile.Grid.Has(BuildingKind.Frame, 4, 4) && tile.Grid.Has(BuildingKind.Frame, 5, 4));
        Assert.Equal(copper - 10, Items(tile, "copper_ore"));
        Assert.Equal(5, UnitStats.Of(content, [], 1).Cost["copper_ore"]);
    }

    [Fact]
    public void DepositsAreALayerThatIsHashedAndSnapshotted()
    {
        var content = TestContent.Load();
        var sim = Simulation.NewGame(content, GameSetup.Default);
        var grid = sim.Tiles[sim.StartTileId].Grid;
        var (x, y) = Enumerable.Range(0, grid.Width * grid.Height).Select(i => (i % grid.Width, i / grid.Width))
            .First(c => grid.DepositAt(c.Item1, c.Item2) != null);
        ulong before = sim.StateHash();
        int cells = grid.CountOn(grid.Layers.Deposit);
        grid.SetDeposit(x, y, null);
        Assert.NotEqual(before, sim.StateHash());
        Assert.Equal(cells - 1, grid.CountOn(grid.Layers.Deposit));

        var restored = Snapshot.Capture(sim).Restore(content, sim.Setup!, sim.CommandLog);
        Assert.Null(restored.Tiles[sim.StartTileId].Grid.DepositAt(x, y));
        Assert.Equal(sim.StateHash(), restored.StateHash());
    }

    [Theory]
    [InlineData("\"layer\": \"cable\"", "\"layer\": \"nowhere\"", "unknown layer 'nowhere'")]
    [InlineData("\"kind\": \"wire\", \"layer\": \"cable\",", "\"kind\": \"wire\",", "name a layer")]
    [InlineData("\"kind\": \"frame\", \"layer\": \"frame\", \"width\": 1", "\"kind\": \"frame\", \"layer\": \"frame\", \"width\": 2", "must be 1x1")]
    public void LayerContentIsValidated(string from, string to, string error)
    {
        var ex = Assert.Throws<ContentException>(() => TestContent.Load(b => b.Replace(from, to)));
        Assert.Contains(ex.Errors, e => e.Contains(error));
    }

    [Fact]
    public void ALayerSitsOnlyOnLayersBelowIt()
    {
        var ex = Assert.Throws<ContentException>(() => TestContent.Load(b => AddBuilding(b, HeavyCable),
            editLayers: l => l.Replace("\"sitsOn\": [\"water\"]", "\"sitsOn\": [\"cable\"]")));
        Assert.Contains(ex.Errors, e => e.Contains("sitsOn 'cable'"));
    }

    static (Simulation Sim, TileState Tile, int X, int Y) WaterTile()
    {
        var sim = Simulation.NewGame(TestContent.Load(), GameSetup.Default);
        var content = sim.Content;
        foreach (var t in sim.Tiles.Where(t => t.Owner == TileState.Unowned))
        {
            var g = t.Grid;
            for (int i = 0; i < g.Width * g.Height; i++)
            {
                int x = i % g.Width, y = i / g.Width;
                if (g.InBounds(x, y) && content.TerrainList[g.TerrainAt(x, y)] is { Water: true, Buildable: false })
                {
                    t.Owner = Me;
                    Fund(sim, t, "copper_ore", 1000);
                    Fund(sim, t, "iron_ore", 1000);
                    return (sim, t, x, y);
                }
            }
        }
        throw new InvalidOperationException("no water");
    }
}
