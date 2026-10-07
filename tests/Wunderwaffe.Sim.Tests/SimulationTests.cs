using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Tests;

public class SimulationTests
{
    const int Player = 0;

    /// <summary>A 20x20 owned tile with iron ore under the top two rows.</summary>
    static (Simulation, TileState) NewSim()
    {
        var sim = new Simulation(TestContent.Load());
        var tile = sim.AddTile(0, 0, Player, size: 20);
        for (int x = 0; x < 20; x++)
        {
            tile.Grid.SetDeposit(x, 0, "iron_ore");
            tile.Grid.SetDeposit(x, 1, "iron_ore");
        }
        return (sim, tile);
    }

    static void Grant(Simulation sim, int copper, int iron)
    {
        sim.Enqueue(new DebugGrantCommand(Player, 0, "copper_ore", copper));
        sim.Enqueue(new DebugGrantCommand(Player, 0, "iron_ore", iron));
    }

    static void Drills(Simulation sim, int count)
    {
        for (int i = 0; i < count; i++)
            sim.Enqueue(new PlaceBuildingCommand(Player, 0, "drill", i * 2, 0));
    }

    static void Smelters(Simulation sim, int count)
    {
        for (int i = 0; i < count; i++)
            sim.Enqueue(new PlaceBuildingCommand(Player, 0, "smelter", i * 2, 5, RecipeId: "smelt_iron"));
    }

    static void Run(Simulation sim, int seconds)
    {
        for (int i = 0; i < seconds * Simulation.TicksPerSecond; i++)
            sim.Step();
    }

    [Fact]
    public void FullySuppliedBuildingsRunAtRecipeRate()
    {
        // 2 smelters need 2 ore/s; 4 drills supply 4 ore/s. Grant exactly the build cost so no ore is left over.
        var (sim, tile) = NewSim();
        Grant(sim, copper: 4 * 10 + 2 * 20, iron: 2 * 10);
        Drills(sim, 4);
        Smelters(sim, 2);
        Run(sim, 60);
        Assert.Equal(60, tile.Inventory.Get("iron_plate"));
    }

    [Fact]
    public void StarvedBuildingsAreLimitedBySupply()
    {
        // FB-014: 2 smelters want 2 ore/s but 1 drill gives 1 ore/s, so plates are capped at 0.5/s.
        var (sim, tile) = NewSim();
        Grant(sim, copper: 10 + 2 * 20, iron: 2 * 10);
        Drills(sim, 1);
        Smelters(sim, 2);
        Run(sim, 60);
        Assert.InRange(tile.Inventory.Get("iron_plate"), 29, 30);
    }

    [Fact]
    public void SameCommandsGiveSameStateHash()
    {
        // NF-004: lockstep multiplayer depends on this.
        Simulation Build()
        {
            var sim = new Simulation(TestContent.Load());
            sim.AddStartingTile(0, 0, Player, seed: 3037);
            sim.Enqueue(new PlaceBuildingCommand(Player, 0, "conveyor", 1, 1));
            sim.Enqueue(new PlaceBuildingCommand(Player, 0, "conveyor", 2, 1));
            Run(sim, 30);
            return sim;
        }
        var a = Build();
        var b = Build();
        Assert.Equal(a.StateHash(), b.StateHash());

        b.Step();
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void StartingTileHasCoreDepositsAndItems()
    {
        var sim = new Simulation(TestContent.Load());
        var tile = sim.AddStartingTile(0, 0, Player, seed: 1);

        Assert.Single(tile.Grid.Buildings, b => b.DefId == Simulation.CoreBuildingId);
        Assert.Equal(Simulation.StartingItems["copper_ore"], tile.Inventory.Get("copper_ore"));
        var deposits = new HashSet<string?>();
        for (int y = 0; y < tile.Grid.Height; y++)
            for (int x = 0; x < tile.Grid.Width; x++)
                deposits.Add(tile.Grid.DepositAt(x, y));
        Assert.Contains("iron_ore", deposits);
        Assert.Contains("copper_ore", deposits);
        Assert.Contains("coal", deposits);
    }

    [Fact]
    public void SameSeedGivesSameDeposits()
    {
        var a = new Simulation(TestContent.Load());
        var b = new Simulation(TestContent.Load());
        a.AddStartingTile(0, 0, Player, seed: 42);
        b.AddStartingTile(0, 0, Player, seed: 42);
        Assert.Equal(a.StateHash(), b.StateHash());
        for (int y = 0; y < 96; y++)
            for (int x = 0; x < 96; x++)
                Assert.Equal(a.Tiles[0].Grid.DepositAt(x, y), b.Tiles[0].Grid.DepositAt(x, y));
    }
}
