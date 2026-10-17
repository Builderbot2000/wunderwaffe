using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

public class SimulationTests
{
    const int Player = 0;

    static void Run(Simulation sim, int seconds)
    {
        for (int i = 0; i < seconds * Simulation.TicksPerSecond; i++)
            sim.Step();
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
        Assert.Equal(Balance.StartingItems["copper_ore"], Items(tile, "copper_ore"));
        var deposits = new HashSet<string?>();
        for (int y = 0; y < tile.Grid.Height; y++)
            for (int x = 0; x < tile.Grid.Width; x++)
                deposits.Add(tile.Grid.DepositAt(x, y));
        Assert.Contains("iron_ore", deposits);
        Assert.Contains("copper_ore", deposits);
        // RS-003: coal is regional, so a starting tile never has it.
        Assert.DoesNotContain("coal", deposits);
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
