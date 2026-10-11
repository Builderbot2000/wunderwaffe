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
    public void StateHashSeesOwnership()
    {
        var (a, _) = NewWorld();
        var (b, _) = NewWorld();
        Assert.Equal(a.StateHash(), b.StateHash());
        At(b, 0, 0).Owner = Them;
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }
}
