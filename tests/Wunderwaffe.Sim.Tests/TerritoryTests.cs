using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

/// <summary>Victory circles, claims, presence, cores, Contested, platforms and fair starts (silo A).</summary>
public class TerritoryTests
{
    const int Size = 30;

    /// <summary>Home (0, 0) is mine and stocked; east (1, 0) is unowned. Circles sit in the middle of each tile.</summary>
    static (Simulation Sim, TileState Home, TileState East) TwoTiles()
    {
        var sim = NewSim();
        var home = Tile(sim, 0, 0, Size);
        var east = sim.AddTile(1, 0, size: Size);
        return (sim, home, east);
    }

    /// <summary>
    /// Puts a building of <paramref name="player"/> on a tile they don't hold, standing in for a unit having landed there
    /// first: the tile is lent to them for the placement.
    /// </summary>
    static void Plant(Simulation sim, TileState tile, int player, string building, int x, int y)
    {
        int owner = tile.Owner;
        tile.Owner = player;
        tile.Inventory.Add(sim.Content.Buildings[building].Cost);
        sim.Enqueue(new PlaceBuildingCommand(player, tile.Id, building, x, y));
        sim.Step();
        tile.Owner = owner;
        Assert.Equal(player, tile.Grid.BuildingAt(x, y)?.Owner);
    }

    static int Capture(Simulation sim) => Expansion.CaptureTicks(sim);

    // --- presence and paying (WM-015) ---

    [Fact]
    public void BuildingNeedsABuildingOrUnitOnTheTile()
    {
        var (sim, home, east) = TwoTiles();
        Assert.Equal(PlaceResult.NoPresence, Placement.Check(sim, east, Me, "conveyor", 0, 0, 0, null, out _));

        Plant(sim, east, Me, "conveyor", 0, 0);
        Assert.Equal(PlaceResult.Ok, Placement.Check(sim, east, Me, "conveyor", 1, 0, 0, null, out var plan));
        // East has no stock of its own (and isn't mine), so my neighbouring home tile pays.
        Assert.Same(home, plan.Payer);
        long copper = Items(home, "copper_ore");
        Place(sim, east, "conveyor", 1, 0);
        sim.Step();
        Assert.Equal(copper - 1, Items(home, "copper_ore"));
        Assert.Equal(0, Items(east, "copper_ore"));

        // Removing my own building there refunds home.
        sim.Enqueue(new RemoveBuildingCommand(Me, east.Id, 1, 0));
        sim.Step();
        Assert.Equal(copper, Items(home, "copper_ore"));
    }

    [Fact]
    public void ATileTooFarFromMyStockCannotBePaidFor()
    {
        var sim = NewSim();
        Tile(sim, 0, 0, Size);
        sim.AddTile(1, 0, size: Size);
        var far = sim.AddTile(2, 0, size: Size);
        Plant(sim, far, Me, "conveyor", 0, 0);
        Assert.Equal(PlaceResult.CannotAfford, Placement.Check(sim, far, Me, "conveyor", 1, 0, 0, null, out _));
    }

    [Fact]
    public void NobodyBuildsInAnEnemyCircleWhileAnEnemyBuildingStandsThere()
    {
        var (sim, _, east) = TwoTiles();
        east.Owner = Them;
        int cx = east.CircleX, cy = east.CircleY;
        Plant(sim, east, Them, "conveyor", cx, cy);
        Plant(sim, east, Me, "conveyor", 0, 0); // my foothold, outside the circle

        Assert.Equal(PlaceResult.EnemyCircle, Placement.Check(sim, east, Me, "conveyor", cx + 2, cy, 0, null, out _));
        Assert.Equal(PlaceResult.Ok, Placement.Check(sim, east, Me, "conveyor", 1, 0, 0, null, out _));

        // CB-003: destroy their building and the circle opens; hold it and the tile is mine.
        sim.Enqueue(new DebugDestroyCommand(Me, east.Id, cx, cy));
        sim.Step();
        Assert.Equal(PlaceResult.Ok, Placement.Check(sim, east, Me, "conveyor", cx + 2, cy, 0, null, out _));
        Place(sim, east, "conveyor", cx + 2, cy);
        Run(sim, Capture(sim) + 1);
        Assert.Equal(Me, east.Owner);
    }

    // --- claiming by circle (WM-006) ---

    [Fact]
    public void HoldingTheCircleClaimsTheTileAndWipesRivalsThere()
    {
        var (sim, _, east) = TwoTiles();
        int cx = east.CircleX, cy = east.CircleY;
        Plant(sim, east, Me, "conveyor", cx, cy);
        Run(sim, 60);
        Plant(sim, east, Them, "conveyor", cx + 1, cy);       // a rival in the circle, a second behind
        Plant(sim, east, Them, "conveyor", 0, 0);             // and one outside it
        Assert.Equal(63, Expansion.Progress(east, Me));
        Assert.Equal(2, Expansion.Progress(east, Them));

        Run(sim, Capture(sim) - 64);
        Assert.Equal(TileState.Unowned, east.Owner);
        sim.Step();
        Assert.Equal(Me, east.Owner);
        Assert.Null(east.Grid.BuildingAt(cx + 1, cy));
        Assert.Equal(Them, east.Grid.BuildingAt(0, 0)?.Owner);
        Assert.Empty(east.ClaimTicks);
        Assert.Equal(2, sim.Tiles.Count(t => t.Owner == Me));
    }

    [Fact]
    public void LeavingTheCircleResetsTheTimer()
    {
        var (sim, _, east) = TwoTiles();
        int cx = east.CircleX, cy = east.CircleY;
        Plant(sim, east, Me, "conveyor", cx, cy);
        Run(sim, 100);
        Assert.Equal(101, Expansion.Progress(east, Me));
        sim.Enqueue(new RemoveBuildingCommand(Me, east.Id, cx, cy));
        sim.Step();
        Assert.Equal(0, Expansion.Progress(east, Me));
        Assert.Empty(east.ClaimTicks);
    }

    [Fact]
    public void BuildingsOutsideTheCircleClaimNothing()
    {
        var (sim, _, east) = TwoTiles();
        Plant(sim, east, Me, "conveyor", 0, 0);
        Assert.False(east.InCircle(0, 0));
        Run(sim, Capture(sim) + 10);
        Assert.Equal(TileState.Unowned, east.Owner);
    }

    [Fact]
    public void AFootprintCountsIfAnyCellIsInTheCircle()
    {
        var (_, _, east) = TwoTiles();
        int r = Balance.CircleRadius;
        Assert.True(east.InCircle(east.CircleX + r, east.CircleY, 2, 2));
        Assert.False(east.InCircle(east.CircleX + r + 1, east.CircleY, 2, 2));
        Assert.True(east.InCircle(east.CircleX + r + 1 - 2, east.CircleY - 1, 2, 2));
    }

    // --- landing (UD-011) and the expansion path ---

    [Fact]
    public void AScoutFliesToAnUnownedTileLandsInItsCircleAndClaimsIt()
    {
        var sim = NewSim();
        var home = sim.AddTile(0, 0, size: Size);
        var east = sim.AddTile(1, 0, size: Size);
        sim.SettleStartingTile(home, Me);
        var core = home.Grid.Buildings.Single(b => b.DefId == "unit_core");
        Assert.Equal(PlaceResult.NoPresence, Placement.Check(sim, east, Me, "conveyor", 0, 0, 0, null, out _));

        sim.Enqueue(new ActivateUnitCommand(Me, home.Id, core.X, core.Y));
        sim.Step();
        var scout = Assert.Single(sim.Units);
        sim.Enqueue(new MoveUnitsCommand(Me, [scout.Id], east.Id, east.CircleX, east.CircleY));
        sim.Step();
        for (int i = 0; i < 60 * 60 && scout.Moving; i++)
            sim.Step();
        Assert.Equal(east.Id, scout.TileId);
        // Flying over is presence already.
        Assert.True(Territory.HasPresence(sim, Me, east));

        Assert.Equal(DeployResult.Ok, UnitAssembly.CheckDeploy(sim, Me, scout.Id, out _));
        sim.Enqueue(new DeployUnitCommand(Me, scout.Id));
        Run(sim, Capture(sim) + 1);
        Assert.Equal(Me, east.Owner);
        Assert.Equal(3, east.Grid.BuildingCount);
    }

    [Fact]
    public void LandingInAnEnemyCircleIsRefusedWhileTheyHoldIt()
    {
        var sim = NewSim();
        var home = Tile(sim, 0, 0, Size);
        var east = sim.AddTile(1, 0, size: Size);
        east.Owner = Them;
        Plant(sim, east, Them, "conveyor", east.CircleX, east.CircleY + 3);

        Frame3(sim, home, 26, 15);
        sim.Enqueue(new ActivateUnitCommand(Me, home.Id, 27, 15));
        sim.Step();
        var scout = sim.Units.Single();
        sim.Enqueue(new MoveUnitsCommand(Me, [scout.Id], east.Id, east.CircleX, east.CircleY));
        sim.Step();
        for (int i = 0; i < 60 * 60 && scout.Moving; i++)
            sim.Step();
        Assert.Equal(east.Id, scout.TileId);
        Assert.Equal(DeployResult.EnemyCircle, UnitAssembly.CheckDeploy(sim, Me, scout.Id, out _));
        Assert.True(Territory.IsContested(sim, east));
        Assert.False(Territory.IsContested(sim, home));
        Assert.Equal([false, true], Territory.ContestedTiles(sim));
    }

    static void Frame3(Simulation sim, TileState tile, int x, int y)
    {
        for (int i = 0; i < 3; i++)
            sim.Enqueue(new PlaceFrameCommand(Me, tile.Id, x + i, y));
        Place(sim, tile, "thruster", x, y);
        Place(sim, tile, "unit_core", x + 1, y);
        Place(sim, tile, "thruster", x + 2, y);
        sim.Step();
    }

    [Fact]
    public void AnUnownedTileIsContestedOnlyWithTwoPlayersOnIt()
    {
        var sim = NewSim();
        var home = Tile(sim, 0, 0, Size);
        var east = sim.AddTile(1, 0, size: Size);
        Frame3(sim, home, 26, 15);
        sim.Enqueue(new ActivateUnitCommand(Me, home.Id, 27, 15));
        sim.Step();
        var scout = sim.Units.Single();
        sim.Enqueue(new MoveUnitsCommand(Me, [scout.Id], east.Id, 10, 15));
        sim.Step();
        for (int i = 0; i < 60 * 60 && scout.Moving; i++)
            sim.Step();
        Assert.Equal(east.Id, scout.TileId);
        Assert.False(Territory.IsContested(sim, east));
    }

    // --- cores (WM-016) ---

    [Fact]
    public void CoresAreBuildableAndOnlyTheLastOneStays()
    {
        var sim = NewSim();
        var home = Tile(sim, 0, 0, Size);
        Place(sim, home, "core", 2, 2);
        Place(sim, home, "core", 10, 2);
        sim.Step();
        Assert.Equal(2, sim.CoreCount(Me));

        sim.Enqueue(new RemoveBuildingCommand(Me, home.Id, 2, 2));
        sim.Step();
        Assert.Equal(1, sim.CoreCount(Me));
        sim.Enqueue(new RemoveBuildingCommand(Me, home.Id, 10, 2));
        sim.Step();
        Assert.Equal(1, sim.CoreCount(Me));

        // Destruction takes the last one too (combat's job; a debug command until then).
        long copper = Items(home, "copper_ore");
        sim.Enqueue(new DebugDestroyCommand(Me, home.Id, 10, 2));
        sim.Step();
        Assert.Equal(0, sim.CoreCount(Me));
        Assert.Equal(copper, Items(home, "copper_ore")); // no refund
    }

    [Fact]
    public void NobodyRemovesSomeoneElsesBuilding()
    {
        var (sim, home, _) = TwoTiles();
        Place(sim, home, "conveyor", 3, 3);
        sim.Step();
        sim.Enqueue(new RemoveBuildingCommand(Them, home.Id, 3, 3));
        sim.Step();
        Assert.NotNull(home.Grid.BuildingAt(3, 3));
    }

    // --- platforms (FB-017) ---

    static (Simulation Sim, TileState Tile, int X, int Y) WaterTile()
    {
        var sim = Simulation.NewGame(TestContent.Load(), GameSetup.Default);
        var content = sim.Content;
        foreach (var t in sim.Tiles.Where(t => t.Owner == TileState.Unowned))
        {
            var g = t.Grid;
            for (int y = 0; y < g.Height; y++)
            {
                for (int x = 0; x + 1 < g.Width; x++)
                {
                    if (g.InBounds(x, y) && g.InBounds(x + 1, y) && content.TerrainList[g.TerrainAt(x, y)] is { Water: true, Buildable: false }
                        && content.TerrainList[g.TerrainAt(x + 1, y)] is { Water: true, Buildable: false })
                    {
                        t.Owner = Me;
                        t.Inventory.Add("copper_ore", 1000);
                        t.Inventory.Add("iron_ore", 1000);
                        return (sim, t, x, y);
                    }
                }
            }
        }
        throw new InvalidOperationException("no water");
    }

    [Fact]
    public void PlatformsFloatOnWaterAndCarryBuildings()
    {
        var (sim, tile, x, y) = WaterTile();
        Assert.Equal(PlaceResult.BadTerrain, Placement.Check(sim, tile, Me, "conveyor", x, y, 0, null, out _));
        Assert.Equal(PlaceResult.Ok, Placement.Check(sim, tile, Me, "platform", x, y, 0, null, out _));
        long iron = Items(tile, "iron_ore");
        Place(sim, tile, "platform", x, y);
        sim.Step();
        Assert.True(tile.Grid.HasPlatform(x, y));
        Assert.Null(tile.Grid.BuildingAt(x, y));
        Assert.Equal(iron - 2, Items(tile, "iron_ore"));
        Assert.Equal(PlaceResult.Blocked, Placement.Check(sim, tile, Me, "platform", x, y, 0, null, out _));

        Place(sim, tile, "conveyor", x, y);
        sim.Step();
        Assert.Equal("conveyor", tile.Grid.BuildingAt(x, y)?.DefId);
        // The neighbouring water cell has no platform.
        Assert.Equal(PlaceResult.BadTerrain, Placement.Check(sim, tile, Me, "conveyor", x + 1, y, 0, null, out _));

        // Removing takes the building first, then the platform, which is refunded.
        sim.Enqueue(new RemoveBuildingCommand(Me, tile.Id, x, y));
        sim.Step();
        Assert.True(tile.Grid.HasPlatform(x, y));
        sim.Enqueue(new RemoveBuildingCommand(Me, tile.Id, x, y));
        sim.Step();
        Assert.False(tile.Grid.HasPlatform(x, y));
        Assert.Equal(iron, Items(tile, "iron_ore"));
    }

    [Fact]
    public void PlatformsOnlyGoOnWater()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Assert.Equal(PlaceResult.BadTerrain, Placement.Check(sim, tile, Me, "platform", 3, 3, 0, null, out _));
    }

    // --- generation (WM-014, WM-005) ---

    static IEnumerable<(int X, int Y)> Disc(TileState t)
    {
        int r = Balance.CircleRadius;
        for (int y = t.CircleY - r; y <= t.CircleY + r; y++)
        {
            for (int x = t.CircleX - r; x <= t.CircleX + r; x++)
            {
                if (t.InCircle(x, y))
                    yield return (x, y);
            }
        }
    }

    [Theory]
    [InlineData(5, 5, 3037UL)]
    [InlineData(7, 7, 11UL)]
    public void EveryTileGetsACircleOnItsDepositsAwayFromTheBorder(int w, int h, ulong seed)
    {
        var sim = Simulation.NewGame(TestContent.Load(), new GameSetup(w, h, seed));
        int inside = 0;
        foreach (var t in sim.Tiles)
        {
            Assert.True(t.Grid.InBounds(t.CircleX, t.CircleY), $"tile {t.Id}: centre off the tile");
            if (Disc(t).All(c => t.Grid.InBounds(c.X, c.Y)))
                inside++;
            // On the richest spot: at least as many deposit cells as a circle in the middle of the tile would hold.
            int Deposits(IEnumerable<(int X, int Y)> cells) => cells.Count(c => t.Grid.InRect(c.X, c.Y) && t.Grid.InBounds(c.X, c.Y) && t.Grid.DepositAt(c.X, c.Y) != null);
            if (t.Grid.DepositCounts().Count > 0)
                Assert.True(Deposits(Disc(t)) > 0, $"tile {t.Id}: no deposits in its circle");
        }
        // Tiles are about 96 cells across against a 25-cell circle, so nearly all fit it wholly.
        Assert.True(inside * 10 >= sim.Tiles.Count * 9, $"{inside} of {sim.Tiles.Count} circles wholly inside");

        var again = Simulation.NewGame(TestContent.Load(), new GameSetup(w, h, seed));
        Assert.Equal(sim.Tiles.Select(t => (t.CircleX, t.CircleY)), again.Tiles.Select(t => (t.CircleX, t.CircleY)));
    }

    [Theory]
    [InlineData(3, 7UL)]
    [InlineData(4, 3037UL)]
    public void SeveralPlayersGetSpreadOutFairStarts(int players, ulong seed)
    {
        var setup = new GameSetup(9, 9, seed, Players: players);
        var sim = Simulation.NewGame(TestContent.Load(), setup);
        var starts = sim.StartTileIds.Select(id => sim.Tiles[id]).ToList();
        Assert.Equal(players, starts.Distinct().Count());
        foreach (var s in starts)
        {
            Assert.DoesNotContain(s.Neighbors, n => sim.StartTileIds.Contains(n));
            Assert.False(s.Grid.DepositCounts().ContainsKey("coal"));
            // Regional deposits one claim away (RS-003), so within two hops (OQ-22).
            Assert.Contains(sim.Neighbors(s), n => n.Grid.DepositCounts().ContainsKey("coal"));
            var core = s.Grid.Buildings.Single(b => b.DefId == Simulation.CoreBuildingId);
            Assert.True(s.InCircle(core.X + 1, core.Y + 1));
        }
        Assert.Equal(sim.StartTileIds, Simulation.NewGame(TestContent.Load(), setup).StartTileIds);
    }
}
