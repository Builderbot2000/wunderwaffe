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
    public void StartingTileHasDepositsButNoCoalRegionalItem()
    {
        var sim = new Simulation(TestContent.Load());
        var tile = sim.AddStartingTile(0, 0, Player, seed: 1);
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
    public void EachStartIsALandedUnitWhoseCoreHoldsTheStartingItems()
    {
        // WM-005, OQ-37: no free core; each player starts with a landed unit (thruster, core, thruster on frame) whose
        // core stands in the circle and holds the starting items, and nothing else holds anything.
        var sim = Simulation.NewGame(TestContent.Load(), GameSetup.Default with { Players = 2 });
        Assert.Empty(sim.Units);
        for (int p = 0; p < 2; p++)
        {
            var home = sim.Tiles[sim.StartTileIds[p]];
            Assert.Equal(p, home.Owner);
            var core = Assert.Single(home.Grid.Cores);
            Assert.Equal((Simulation.CoreBuildingId, p), (core.DefId, core.Owner));
            Assert.True(home.InCircle(core.X, core.Y, core.Width, core.Height));
            Assert.Equal(Balance.StartingItems.OrderBy(kv => kv.Key).Select(kv => (kv.Key, (long)kv.Value)),
                core.Stock!.Entries.Where(kv => kv.Value > 0).Select(kv => (kv.Key, kv.Value)));
            Assert.Equal(2, home.Grid.Buildings.Count(b => b.DefId == "thruster" && b.Owner == p));
            Assert.Equal(3, home.Grid.BuildingCount);
            Assert.True(home.Grid.HasFrame(core.X, core.Y));
            Assert.Equal(AssembleResult.Ok, UnitAssembly.CheckActivate(sim, home, p, core.X, core.Y, out _));
        }
    }

    [Fact]
    public void SameSeedGivesSameDeposits()
    {
        var a = new Simulation(TestContent.Load());
        var b = new Simulation(TestContent.Load());
        a.AddStartingTile(0, 0, Player, seed: 42);
        b.AddStartingTile(0, 0, Player, seed: 42);
        Assert.Equal(a.StateHash(), b.StateHash());
        for (int y = 0; y < Simulation.DefaultTileSize; y++)
            for (int x = 0; x < Simulation.DefaultTileSize; x++)
                Assert.Equal(a.Tiles[0].Grid.DepositAt(x, y), b.Tiles[0].Grid.DepositAt(x, y));
    }
}
