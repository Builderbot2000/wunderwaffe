using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

/// <summary>Victory circles, claims, presence, cores, Contested, platforms and fair starts (silo A).</summary>
public class TerritoryTests
{
    /// <summary>Hand-built tile size: room for a victory circle with ground around it.</summary>
    const int Size = Balance.CircleRadius * 3;

    /// <summary>Home (0, 0) is mine and stocked; east (1, 0) is unowned. Circles sit in the middle of each tile.</summary>
    static (Simulation Sim, TileState Home, TileState East) TwoTiles()
    {
        var sim = NewSim();
        var home = Tile(sim, 0, 0, Size);
        var east = sim.AddTile(1, 0, size: Size);
        return (sim, home, east);
    }

    /// <summary>
    /// Puts a building of <paramref name="player"/> straight onto a tile, unpaid, standing in for a unit having landed
    /// there first.
    /// </summary>
    static void Plant(Simulation sim, TileState tile, int player, string building, int x, int y)
    {
        var def = sim.Content.Buildings[building];
        tile.Grid.Add(def, player, x, y, def.Width, def.Height, 0, null);
    }

    static int Capture(Simulation sim) => Expansion.CaptureTicks(sim);

    // --- presence and paying (WM-015) ---

    [Fact]
    public void BuildingNeedsABuildingOrUnitOnTheTile()
    {
        var (sim, _, east) = TwoTiles();
        Assert.Equal(PlaceResult.NoPresence, Placement.Check(sim, east, Me, "conveyor", 0, 0, 0, null, out _));
        Plant(sim, east, Me, "conveyor", 0, 0);
        Assert.NotEqual(PlaceResult.NoPresence, Placement.Check(sim, east, Me, "conveyor", 1, 0, 0, null, out _));
    }

    [Fact]
    public void ANeighboursStockNeverPaysOnlyCoresOnTheTileDo()
    {
        // FB-019: home is mine and stocked, but east is paid only from my cores standing on east.
        var (sim, home, east) = TwoTiles();
        Plant(sim, east, Me, "conveyor", 0, 0);
        long homeCopper = Items(home, "copper_ore");
        Assert.Equal(PlaceResult.CannotAfford, Placement.Check(sim, east, Me, "conveyor", 1, 0, 0, null, out _));
        Place(sim, east, "conveyor", 1, 0);
        sim.Step();
        Assert.Null(east.Grid.BuildingAt(1, 0));

        // Their core there doesn't pay for me either.
        Stock(PutCore(sim, east, Them, 10, 10), "copper_ore", 50);
        Assert.Equal(PlaceResult.CannotAfford, Placement.Check(sim, east, Me, "conveyor", 1, 0, 0, null, out _));

        var mine = PutCore(sim, east, Me, 20, 20);
        Stock(mine, "copper_ore", 5);
        Place(sim, east, "conveyor", 1, 0);
        sim.Step();
        Assert.Equal("conveyor", east.Grid.BuildingAt(1, 0)?.DefId);
        Assert.Equal(4, mine.Stock!.Get("copper_ore"));
        Assert.Equal(homeCopper, Items(home, "copper_ore"));

        // Removing it refunds my core on east, not home.
        sim.Enqueue(new RemoveBuildingCommand(Me, east.Id, 1, 0));
        sim.Step();
        Assert.Equal(5, mine.Stock!.Get("copper_ore"));
        Assert.Equal(homeCopper, Items(home, "copper_ore"));
    }

    [Fact]
    public void CoresPayInIdOrderAllOrNothing()
    {
        // OQ-38: the lowest-id core pays first, each item topped up from the next; a cost the cores can't cover
        // together takes nothing.
        var sim = NewSim();
        var tile = sim.AddTile(0, 0, Me, Size);
        var first = PutCore(sim, tile, Me, 20, 20);
        var second = PutCore(sim, tile, Me, 24, 20);
        Stock(first, "copper_ore", 15);
        Stock(second, "copper_ore", 20);
        Stock(second, "iron_ore", 30);
        // A smelter costs 20 copper ore and 10 iron ore.
        Place(sim, tile, "smelter", 2, 2, recipe: "smelt_iron");
        sim.Step();
        Assert.Equal("smelter", tile.Grid.BuildingAt(2, 2)?.DefId);
        Assert.Equal(0, first.Stock!.Get("copper_ore"));
        Assert.Equal((15L, 20L), (second.Stock!.Get("copper_ore"), second.Stock.Get("iron_ore")));

        // 15 copper ore left in all: a second smelter is refused and nothing is taken.
        Assert.Equal(PlaceResult.CannotAfford, Placement.Check(sim, tile, Me, "smelter", 5, 5, 0, "smelt_iron", out _));
        Place(sim, tile, "smelter", 5, 5, recipe: "smelt_iron");
        sim.Step();
        Assert.Null(tile.Grid.BuildingAt(5, 5));
        Assert.Equal((15L, 20L), (second.Stock!.Get("copper_ore"), second.Stock.Get("iron_ore")));
    }

    [Fact]
    public void RefundsGoToTheNearestCoreOrAreLost()
    {
        // OQ-39: a removed building's refund and held items go to its owner's nearest core on the tile.
        var sim = NewSim();
        var tile = sim.AddTile(0, 0, Me, Size);
        var west = PutCore(sim, tile, Me, 0, 10);
        var east = PutCore(sim, tile, Me, 26, 10);
        Stock(west, "copper_ore", 100);
        Place(sim, tile, "conveyor", 22, 10);
        sim.Step();
        Assert.Equal(99, west.Stock!.Get("copper_ore"));
        sim.Enqueue(new RemoveBuildingCommand(Me, tile.Id, 22, 10));
        sim.Step();
        Assert.Equal((99L, 1L), (west.Stock!.Get("copper_ore"), east.Stock!.Get("copper_ore")));

        // With no core of mine on the tile, removal still works and the refund is lost.
        var bare = sim.AddTile(1, 0, Me, Size);
        Plant(sim, bare, Me, "conveyor", 3, 3);
        sim.Enqueue(new RemoveBuildingCommand(Me, bare.Id, 3, 3));
        sim.Step();
        Assert.Null(bare.Grid.BuildingAt(3, 3));
        Assert.Empty(bare.Grid.Cores);
    }

    [Fact]
    public void AFullCoreRefusesDeliveriesAndTheBeltBacksUp()
    {
        // OQ-39: capacity is per item; a full core takes no more of that item, so the belt into it fills.
        var sim = NewSim();
        var tile = Tile(sim, 0, 0, Size);
        var core = PutCore(sim, tile, Me, 10, 10);
        int cap = core.Def.Capacity;
        Stock(core, "copper_ore", cap - 3);
        Ore(tile, "copper_ore", 2, 10);
        Place(sim, tile, "drill", 2, 10);
        Belt(sim, tile, 4, 10, 6);
        Run(sim, 60 * 60);
        Assert.Equal(cap, core.Stock!.Get("copper_ore"));
        var belt = tile.Grid.BuildingAt(9, 10)!.Belt!;
        Assert.Equal(BeltState.Capacity, belt.Count);

        // Iron ore is counted apart: there is still room for it.
        Assert.True(Funds.Accepts(core, "iron_ore"));
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
        Bank(sim, east, Me, 0, 2); // my foothold and funds, outside the circle

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
        Assert.Equal(60, Expansion.Progress(east, Me));
        Assert.Equal(0, Expansion.Progress(east, Them));

        Run(sim, Capture(sim) - 61);
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
        Assert.Equal(100, Expansion.Progress(east, Me));
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
        var core = home.Grid.Cores.Single();
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
        // The starting items came along in the core and pay for building here now (FB-019).
        Assert.Equal(Balance.StartingItems["copper_ore"], Items(east, "copper_ore"));
        Assert.Equal(0, Items(home, "copper_ore"));
        Assert.Equal(PlaceResult.Ok, Placement.Check(sim, east, Me, "conveyor", 0, 0, 0, null, out _));
    }

    [Fact]
    public void LandingInAnEnemyCircleIsRefusedWhileTheyHoldIt()
    {
        var sim = NewSim();
        var home = Tile(sim, 0, 0, Size);
        var east = sim.AddTile(1, 0, size: Size);
        east.Owner = Them;
        Plant(sim, east, Them, "conveyor", east.CircleX, east.CircleY + 3);

        var scout = Scout(sim, home, 27, 15);
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

    [Fact]
    public void AnUnownedTileIsContestedOnlyWithTwoPlayersOnIt()
    {
        var sim = NewSim();
        var home = Tile(sim, 0, 0, Size);
        var east = sim.AddTile(1, 0, size: Size);
        var scout = Scout(sim, home, 27, 15);
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
        var home = Tile(sim, 0, 0, Size); // its bank core is the first
        Place(sim, home, "core_large", 2, 2);
        sim.Step();
        Assert.Equal(2, sim.CoreCount(Me));

        // Nothing is free: a core costs its variant's cost (OQ-37). Removing one hands its stock and refund to the
        // nearest core left.
        long copper = Items(home, "copper_ore");
        sim.Enqueue(new RemoveBuildingCommand(Me, home.Id, 2, 2));
        sim.Step();
        Assert.Equal(1, sim.CoreCount(Me));
        Assert.True(Items(home, "iron_plate") > 0);
        Assert.Equal(copper, Items(home, "copper_ore"));
        sim.Enqueue(new RemoveBuildingCommand(Me, home.Id, Size - 1, Size - 1));
        sim.Step();
        Assert.Equal(1, sim.CoreCount(Me));

        // Destruction takes the last one too, and its items are lost (OQ-39).
        sim.Enqueue(new DebugDestroyCommand(Me, home.Id, Size - 1, Size - 1));
        sim.Step();
        Assert.Equal(0, sim.CoreCount(Me));
        Assert.Equal(0, Items(home, "copper_ore"));
    }

    [Fact]
    public void APlayerWithOnlyACoreOnAUnitIsStillInTheMatch()
    {
        // WM-016: elimination counts cores on units too.
        var sim = Simulation.NewGame(TestContent.Load(), GameSetup.Default with { Players = 2 });
        var home = sim.Tiles[sim.StartTileId];
        var core = home.Grid.Cores.Single(c => c.Owner == Me);
        sim.Enqueue(new ActivateUnitCommand(Me, home.Id, core.X, core.Y));
        Run(sim, Victory.CheckEvery * 2);
        Assert.Single(sim.Units, u => u.Owner == Me);
        Assert.Equal(1, sim.CoreCount(Me));
        Assert.True(Victory.HasCore(sim, Me));
        Assert.False(Victory.IsEliminated(sim, Me));

        Combat.DestroyUnit(sim, sim.Units.Single(u => u.Owner == Me));
        Run(sim, Victory.CheckEvery * 2);
        Assert.True(Victory.IsEliminated(sim, Me));
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
                        Fund(sim, t, "copper_ore", 1000);
                        Fund(sim, t, "iron_ore", 1000);
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
        int inside = 0, rich = 0, withDeposits = 0;
        foreach (var t in sim.Tiles)
        {
            Assert.True(t.Grid.InBounds(t.CircleX, t.CircleY), $"tile {t.Id}: centre off the tile");
            if (Disc(t).All(c => t.Grid.InBounds(c.X, c.Y)))
                inside++;
            int Deposits(IEnumerable<(int X, int Y)> cells) => cells.Count(c => t.Grid.InRect(c.X, c.Y) && t.Grid.InBounds(c.X, c.Y) && t.Grid.DepositAt(c.X, c.Y) != null);
            if (t.Grid.DepositCounts().Count > 0)
            {
                withDeposits++;
                if (Deposits(Disc(t)) > 0)
                    rich++;
            }
        }
        // A deposit can lie only along a tile's edge, out of reach of any circle that fits, but that is rare.
        Assert.True(rich * 10 >= withDeposits * 9, $"{rich} of {withDeposits} circles hold deposits");
        // Tiles are about 256 cells across against a 65-cell circle, so nearly all fit it wholly.
        Assert.True(inside * 10 >= sim.Tiles.Count * 9, $"{inside} of {sim.Tiles.Count} circles wholly inside");

        var again = Simulation.NewGame(TestContent.Load(), new GameSetup(w, h, seed));
        Assert.Equal(sim.Tiles.Select(t => (t.CircleX, t.CircleY)), again.Tiles.Select(t => (t.CircleX, t.CircleY)));
    }

    [Theory]
    [InlineData(3, 7UL)]
    [InlineData(4, 3037UL)]
    public void SeveralPlayersGetSpreadOutFairStarts(int players, ulong seed)
    {
        var setup = new GameSetup(7, 7, seed, Players: players);
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

    // --- seams with blueprints (D) and movement (E) ---

    [Fact]
    public void ALandedScoutLiftsOffAgainFromAnUnownedTile()
    {
        var sim = NewSim();
        var home = sim.AddTile(0, 0, size: Size);
        var east = sim.AddTile(1, 0, size: Size);
        var north = sim.AddTile(1, 1, size: Size);
        sim.SettleStartingTile(home, Me);
        var core = home.Grid.Cores.Single();
        sim.Enqueue(new ActivateUnitCommand(Me, home.Id, core.X, core.Y));
        sim.Step();
        var scout = Assert.Single(sim.Units);
        // Land outside the circle, so the tile stays unowned.
        sim.Enqueue(new MoveUnitsCommand(Me, [scout.Id], east.Id, 3, 3));
        sim.Step();
        for (int i = 0; i < 60 * 60 && scout.Moving; i++)
            sim.Step();
        Assert.Equal(east.Id, scout.TileId);
        sim.Enqueue(new DeployUnitCommand(Me, scout.Id));
        sim.Step();
        Assert.Empty(sim.Units);
        Assert.Equal(TileState.Unowned, east.Owner);

        var landed = east.Grid.Cores.Single();
        Assert.Equal(AssembleResult.Ok, UnitAssembly.CheckActivate(sim, east, Me, landed.X, landed.Y, out _));
        Assert.Equal(AssembleResult.NotOwner, UnitAssembly.CheckActivate(sim, east, Them, landed.X, landed.Y, out _));
        sim.Enqueue(new ActivateUnitCommand(Me, east.Id, landed.X, landed.Y));
        sim.Step();
        var again = Assert.Single(sim.Units);
        sim.Enqueue(new MoveUnitsCommand(Me, [again.Id], north.Id, north.CircleX, north.CircleY));
        sim.Step();
        for (int i = 0; i < 3 * 60 * 60 && again.Moving; i++)
            sim.Step();
        Assert.Equal(north.Id, again.TileId);
    }

    [Fact]
    public void FramesOnATileYouDontOwnArePaidFromYourCoresThere()
    {
        var (sim, home, east) = TwoTiles();
        Plant(sim, east, Me, "conveyor", 2, 2);
        long iron = Items(home, "iron_ore");
        Assert.Equal(PlaceResult.NoPresence, UnitAssembly.CheckFrame(sim, east, Them, 5, 5));
        Assert.Equal(PlaceResult.CannotAfford, UnitAssembly.CheckFrame(sim, east, Me, 5, 5));
        var mine = PutCore(sim, east, Me, 20, 20);
        Stock(mine, "iron_ore", 10);
        Assert.Equal(PlaceResult.Ok, UnitAssembly.CheckFrame(sim, east, Me, 5, 5));
        sim.Enqueue(new PlaceFrameCommand(Me, east.Id, 5, 5));
        sim.Step();
        Assert.True(east.Grid.HasFrame(5, 5));
        Assert.Equal(10 - Balance.FrameCost["iron_ore"], mine.Stock!.Get("iron_ore"));
        sim.Enqueue(new RemoveFrameCommand(Me, east.Id, 5, 5));
        sim.Step();
        Assert.False(east.Grid.HasFrame(5, 5));
        Assert.Equal(10, mine.Stock!.Get("iron_ore"));
        Assert.Equal(iron, Items(home, "iron_ore"));
    }

    [Fact]
    public void ABlueprintOnATileYouDontOwnIsPaidFromYourCoresThere()
    {
        var (sim, home, east) = TwoTiles();
        Plant(sim, east, Me, "conveyor", 2, 2);
        long copper = Items(home, "copper_ore");
        var belts = new Blueprint([new BlueprintBlock("conveyor", 0, 0, 0), new BlueprintBlock("conveyor", 1, 0, 0)], []);
        Assert.Equal(BlueprintResult.CannotAfford, Blueprints.Check(sim, east, Me, belts, 5, 5, 0, false, out _));
        var mine = PutCore(sim, east, Me, 20, 20);
        Stock(mine, "copper_ore", 10);
        Assert.Equal(BlueprintResult.Ok, Blueprints.Check(sim, east, Me, belts, 5, 5, 0, false, out var plan));
        sim.Enqueue(new PlaceBlueprintCommand(Me, east.Id, belts, 5, 5));
        sim.Step();
        Assert.Equal(3, east.Grid.Buildings.Count(b => b.DefId == "conveyor"));
        Assert.Equal(10 - plan.Cost["copper_ore"], mine.Stock!.Get("copper_ore"));
        Assert.Equal(copper, Items(home, "copper_ore"));
        Assert.Equal(BlueprintResult.Blocked, Blueprints.Check(sim, east, Them, belts, 8, 8, 0, false, out _));
    }

    [Fact]
    public void TheStartingScoutPassesLiftOffValidationAndIsGround()
    {
        // D's UD-003 power rule and E's UD-004 locomotion rule: thrusters only, no power draw.
        var sim = Simulation.NewGame(TestContent.Load(), GameSetup.Default);
        var home = sim.Tiles[sim.StartTileId];
        var core = home.Grid.Cores.Single();
        Assert.Equal(AssembleResult.Ok, UnitAssembly.CheckActivate(sim, home, Me, core.X, core.Y, out _));
        sim.Enqueue(new ActivateUnitCommand(Me, home.Id, core.X, core.Y));
        sim.Step();
        Assert.Equal(LocomotionClass.Ground, Assert.Single(sim.Units).Locomotion);
    }

    [Fact]
    public void AScoutSentToACircleItCantReachKeepsStill()
    {
        // Every tile has a circle, ocean tiles too (WM-014); a ground scout can't get to one out at sea (UD-004).
        var content = TestContent.Load();
        for (ulong seed = 3037; seed < 3037 + 20; seed++)
        {
            var sim = Simulation.NewGame(content, GameSetup.Default with { Seed = seed });
            var home = sim.Tiles[sim.StartTileId];
            var core = home.Grid.Cores.Single();
            sim.Enqueue(new ActivateUnitCommand(Me, home.Id, core.X, core.Y));
            sim.Step();
            var scout = Assert.Single(sim.Units);
            if (sim.Tiles.FirstOrDefault(t => UnitNav.Plan(sim, scout, t.Id, t.CircleX, t.CircleY) == null) is not { } sea)
                continue;
            Assert.Equal(MoveResult.Unreachable, UnitSim.Order(sim, Me, [scout.Id], sea.Id, sea.CircleX, sea.CircleY));
            Assert.Null(scout.Order);
            Assert.False(scout.Moving);
            return;
        }
        Assert.Fail("no seed with a tile out of a ground scout's reach");
    }
}
