using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Tests;

public class WorldTests
{
    const int Me = 0;
    const int Them = 1;

    const int Size = 20;

    /// <summary>The hand-built tile at grid position (x, y).</summary>
    static TileState At(Simulation sim, int x, int y) => sim.TileAtCell(x * Size, y * Size)!;

    /// <summary>A 3x3 grid of small hand-built tiles with the player settled on the middle one.</summary>
    static (Simulation Sim, TileState Home) NewWorld()
    {
        var sim = new Simulation(TestContent.Load());
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
                sim.AddTile(x, y, size: Size);
        }
        var home = At(sim, 1, 1);
        sim.SettleStartingTile(home, Me);
        return (sim, home);
    }

    [Fact]
    public void EveryCellMapsToItsTileWithUniqueIds()
    {
        var (sim, _) = NewWorld();
        Assert.Equal(9, sim.Tiles.Count);
        Assert.Equal(9, sim.Tiles.Select(t => t.Id).Distinct().Count());
        Assert.All(sim.Tiles, t => Assert.Same(t, sim.TileAtCell(t.OriginX + Size - 1, t.OriginY + Size - 1)));
        Assert.Null(sim.TileAtCell(3 * Size, 0));
        Assert.Null(sim.TileAtCell(-1, 0));
    }

    [Fact]
    public void ResolveCellMapsLocalCellsPastTheEdgeOntoTheNeighbour()
    {
        var (sim, home) = NewWorld();
        Assert.Equal((At(sim, 2, 1), 0, 5), sim.ResolveCell(home, Size, 5));
        Assert.Equal((At(sim, 0, 0), Size - 1, Size - 1), sim.ResolveCell(home, -1, -1));
        Assert.Null(sim.ResolveCell(At(sim, 0, 0), -1, 0));
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
        static (int, int) Pos(TileState t) => (t.OriginX / Size, t.OriginY / Size);
        Assert.Equal([(1, 0), (0, 1), (2, 1), (1, 2)], sim.Neighbors(At(sim, 1, 1)).Select(Pos));
        Assert.Equal([(1, 0), (0, 1)], sim.Neighbors(At(sim, 0, 0)).Select(Pos));
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
        var target = At(sim, 2, 1);
        long copper = home.Inventory.Get("copper_ore"), iron = home.Inventory.Get("iron_ore");

        sim.Enqueue(new ClaimTileCommand(Me, target.Id));
        sim.Step();

        Assert.Equal(Me, target.Owner);
        Assert.Equal(copper - Balance.ClaimCost["copper_ore"], home.Inventory.Get("copper_ore"));
        Assert.Equal(iron - Balance.ClaimCost["iron_ore"], home.Inventory.Get("iron_ore"));
        Assert.Equal(1, target.Grid.BuildingCount); // The core.
        Assert.Equal(Balance.ClaimedTileItems["copper_ore"], target.Inventory.Get("copper_ore"));
    }

    [Fact]
    public void ClaimedTileCanBeBuiltOn()
    {
        var (sim, _) = NewWorld();
        var target = At(sim, 2, 1);
        sim.Enqueue(new ClaimTileCommand(Me, target.Id));
        sim.Step();

        Assert.Equal(PlaceResult.Ok, Placement.Check(sim, target, Me, "conveyor", 0, 0, 0, null, out _));
    }

    [Fact]
    public void CannotClaimDiagonalOrDistantTiles()
    {
        var (sim, _) = NewWorld();
        Assert.Equal(ClaimResult.NotAdjacent, Expansion.Check(sim, Me, At(sim, 0, 0).Id, out _));

        // An id past the end of the world is not a tile.
        Assert.Equal(ClaimResult.NoSuchTile, Expansion.Check(sim, Me, 99, out _));
    }

    [Fact]
    public void ClaimChainsOutwardFromClaimedTiles()
    {
        var (sim, home) = NewWorld();
        home.Inventory.Add("copper_ore", 1000);
        home.Inventory.Add("iron_ore", 1000);
        var east = At(sim, 2, 1);
        var corner = At(sim, 2, 0);
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
    public void RefusesClaimsWhenOwnedOrUnaffordable()
    {
        var (sim, home) = NewWorld();
        var target = At(sim, 1, 0);

        Assert.Equal(ClaimResult.AlreadyOwned, Expansion.Check(sim, Me, home.Id, out _));
        Assert.Equal(ClaimResult.AlreadyOwned, Expansion.Check(sim, Them, home.Id, out _));
        Assert.Equal(ClaimResult.NotAdjacent, Expansion.Check(sim, Them, target.Id, out _));

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
            sim.Enqueue(new ClaimTileCommand(Me, At(sim, 1, 0).Id));
            sim.Enqueue(new ClaimTileCommand(Me, At(sim, 0, 1).Id));
            for (int i = 0; i < 120; i++)
                sim.Step();
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void StateHashSeesOwnership()
    {
        var (a, _) = NewWorld();
        var (b, _) = NewWorld();
        Assert.Equal(a.StateHash(), b.StateHash());
        At(b, 0, 0).Owner = Them;
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }
}
