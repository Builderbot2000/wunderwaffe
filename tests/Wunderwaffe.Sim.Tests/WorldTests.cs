using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Tests;

public class WorldTests
{
    const int Me = 0;
    const int Them = 1;

    /// <summary>A 3x3 world of small tiles with the player settled on the middle one.</summary>
    static (Simulation Sim, TileState Home) NewWorld()
    {
        var sim = new Simulation(TestContent.Load());
        sim.GenerateWorld(3, 3, seed: 7, tileSize: 20);
        var home = sim.TileAt(1, 1)!;
        sim.SettleStartingTile(home, Me);
        return (sim, home);
    }

    [Fact]
    public void WorldHasOneTilePerCoordinateWithUniqueIds()
    {
        var (sim, _) = NewWorld();
        Assert.Equal(9, sim.Tiles.Count);
        Assert.Equal(9, sim.Tiles.Select(t => t.Id).Distinct().Count());
        Assert.All(sim.Tiles, t => Assert.Same(t, sim.TileAt(t.X, t.Y)));
        Assert.Null(sim.TileAt(3, 0));
        Assert.Null(sim.TileAt(-1, 0));
    }

    [Fact]
    public void RejectsTwoTilesAtOneCoordinate()
    {
        var (sim, _) = NewWorld();
        Assert.Throws<InvalidOperationException>(() => sim.AddTile(0, 0));
    }

    [Fact]
    public void AdjacencyIsEdgeSharingOnly()
    {
        var (sim, _) = NewWorld();
        Assert.Equal([(1, 0), (2, 1), (1, 2), (0, 1)], sim.Neighbors(sim.TileAt(1, 1)!).Select(t => (t.X, t.Y)));
        Assert.Equal([(1, 0), (0, 1)], sim.Neighbors(sim.TileAt(0, 0)!).Select(t => (t.X, t.Y)));
    }

    [Fact]
    public void TilesHaveDifferingDeposits()
    {
        var (sim, _) = NewWorld();
        Assert.All(sim.Tiles, t => Assert.NotEmpty(t.Grid.DepositCounts()));
        var layouts = sim.Tiles.Select(t => string.Join(",", t.Grid.DepositCounts().Select(kv => $"{kv.Key}{kv.Value}")));
        Assert.True(layouts.Distinct().Count() > 1);
    }

    [Fact]
    public void OwnershipStatesFollowOwnerAndContested()
    {
        var (sim, home) = NewWorld();
        Assert.Equal(TileOwnership.Owned, home.Ownership);
        Assert.Equal(TileOwnership.Unclaimed, sim.TileAt(0, 0)!.Ownership);
        home.Contested = true;
        Assert.Equal(TileOwnership.Contested, home.Ownership);
    }

    [Fact]
    public void StartingTileCannotBeSettledTwice()
    {
        var (sim, home) = NewWorld();
        Assert.Throws<InvalidOperationException>(() => sim.SettleStartingTile(home, Them));
    }

    [Fact]
    public void ClaimsAnAdjacentTileAndPaysFromTheNeighbour()
    {
        var (sim, home) = NewWorld();
        var target = sim.TileAt(2, 1)!;
        long copper = home.Inventory.Get("copper_ore"), iron = home.Inventory.Get("iron_ore");

        sim.Enqueue(new ClaimTileCommand(Me, target.Id));
        sim.Step();

        Assert.Equal(Me, target.Owner);
        Assert.Equal(copper - Simulation.ClaimCost["copper_ore"], home.Inventory.Get("copper_ore"));
        Assert.Equal(iron - Simulation.ClaimCost["iron_ore"], home.Inventory.Get("iron_ore"));
        Assert.Equal(1, target.Grid.BuildingCount); // The core.
        Assert.Equal(Simulation.ClaimedTileItems["copper_ore"], target.Inventory.Get("copper_ore"));
    }

    [Fact]
    public void ClaimedTileCanBeBuiltOn()
    {
        var (sim, _) = NewWorld();
        var target = sim.TileAt(2, 1)!;
        sim.Enqueue(new ClaimTileCommand(Me, target.Id));
        sim.Step();

        Assert.Equal(PlaceResult.Ok, Placement.Check(sim, target, Me, "conveyor", 0, 0, 0, null, out _));
    }

    [Fact]
    public void CannotClaimDiagonalOrDistantTiles()
    {
        var (sim, _) = NewWorld();
        Assert.Equal(ClaimResult.NotAdjacent, Expansion.Check(sim, Me, sim.TileAt(0, 0)!.Id, out _));

        // An id past the end of the world is not a tile.
        Assert.Equal(ClaimResult.NoSuchTile, Expansion.Check(sim, Me, 99, out _));
    }

    [Fact]
    public void ClaimChainsOutwardFromClaimedTiles()
    {
        var (sim, home) = NewWorld();
        home.Inventory.Add("copper_ore", 1000);
        home.Inventory.Add("iron_ore", 1000);
        var east = sim.TileAt(2, 1)!;
        var corner = sim.TileAt(2, 0)!;
        Assert.Equal(ClaimResult.NotAdjacent, Expansion.Check(sim, Me, corner.Id, out _));

        sim.Enqueue(new ClaimTileCommand(Me, east.Id));
        sim.Step();

        // (2,0) borders (2,1) and (1,0); only (2,1) is owned, but it can't afford the cost yet.
        Assert.Equal(ClaimResult.CannotAfford, Expansion.Check(sim, Me, corner.Id, out _));
        east.Inventory.Add("copper_ore", 100);
        east.Inventory.Add("iron_ore", 50);
        Assert.Equal(ClaimResult.Ok, Expansion.Check(sim, Me, corner.Id, out var source));
        Assert.Same(east, source);
    }

    [Fact]
    public void RefusesClaimsWhenOwnedContestedOrUnaffordable()
    {
        var (sim, home) = NewWorld();
        var target = sim.TileAt(1, 0)!;

        Assert.Equal(ClaimResult.AlreadyOwned, Expansion.Check(sim, Me, home.Id, out _));
        Assert.Equal(ClaimResult.AlreadyOwned, Expansion.Check(sim, Them, home.Id, out _));
        Assert.Equal(ClaimResult.NotAdjacent, Expansion.Check(sim, Them, target.Id, out _));

        target.Contested = true;
        Assert.Equal(ClaimResult.Contested, Expansion.Check(sim, Me, target.Id, out _));
        target.Contested = false;

        home.Inventory.TryTake(new Dictionary<string, int> { ["copper_ore"] = (int)home.Inventory.Get("copper_ore") });
        Assert.Equal(ClaimResult.CannotAfford, Expansion.Check(sim, Me, target.Id, out _));

        sim.Enqueue(new ClaimTileCommand(Me, target.Id));
        sim.Step();
        Assert.Equal(TileState.Unowned, target.Owner);
    }

    [Fact]
    public void ClaimsAreDeterministic()
    {
        ulong Run()
        {
            var (sim, _) = NewWorld();
            sim.Enqueue(new ClaimTileCommand(Me, sim.TileAt(1, 0)!.Id));
            sim.Enqueue(new ClaimTileCommand(Me, sim.TileAt(0, 1)!.Id));
            for (int i = 0; i < 120; i++)
                sim.Step();
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void StateHashSeesOwnershipAndContested()
    {
        var (a, _) = NewWorld();
        var (b, _) = NewWorld();
        Assert.Equal(a.StateHash(), b.StateHash());
        b.TileAt(0, 0)!.Contested = true;
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }
}
