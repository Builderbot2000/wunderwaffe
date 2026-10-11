using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

public class UnitTests
{
    const int S = Unit.CellScale;

    static void Frame(Simulation sim, TileState tile, int x, int y, int w = 1, int h = 1)
    {
        for (int cy = y; cy < y + h; cy++)
            for (int cx = x; cx < x + w; cx++)
                sim.Enqueue(new PlaceFrameCommand(Me, tile.Id, cx, cy));
    }

    /// <summary>Three framed cells in a row: a thruster pushing east on each end and the unit core between them.</summary>
    static void LayScout(Simulation sim, TileState tile, int x, int y)
    {
        Frame(sim, tile, x - 1, y, 3);
        Place(sim, tile, "thruster", x - 1, y, 0);
        Place(sim, tile, "unit_core", x, y);
        Place(sim, tile, "thruster", x + 1, y, 0);
        sim.Step();
    }

    static Unit Scout(Simulation sim, TileState tile, int x = 10, int y = 10)
    {
        LayScout(sim, tile, x, y);
        sim.Enqueue(new ActivateUnitCommand(Me, tile.Id, x, y));
        sim.Step();
        return sim.Units[^1];
    }

    static void Move(Simulation sim, Unit u, TileState to, int x, int y, int player = Me) =>
        sim.Enqueue(new MoveUnitsCommand(player, [u.Id], to.Id, x, y));

    static bool RunUntil(Simulation sim, Func<bool> done, int maxTicks)
    {
        for (int i = 0; i < maxTicks; i++)
        {
            sim.Step();
            if (done())
                return true;
        }
        return false;
    }

    /// <summary>Distance from the unit to the centre of a cell of <paramref name="on"/> (world cells if null), in cells.</summary>
    static double CellsTo(Unit u, int cx, int cy, TileState? on = null) =>
        Math.Sqrt(Math.Pow(u.X - (cx + (on?.OriginX ?? 0) + 0.5) * S, 2) + Math.Pow(u.Y - (cy + (on?.OriginY ?? 0) + 0.5) * S, 2)) / S;

    // --- frames ---

    [Fact]
    public void AUnitCrossesAnIrregularBorderOnAGeneratedWorld()
    {
        var sim = Simulation.NewGame(TestContent.Load(), GameSetup.Default);
        var home = sim.Tiles[sim.StartTileId];
        home.Inventory.Add("copper_ore", 10_000);
        home.Inventory.Add("iron_ore", 10_000);
        home.Inventory.Add("steel", 1_000);
        home.Inventory.Add("iron_plate", 1_000);
        home.Inventory.Add("copper_plate", 1_000);
        var (x, y) = Open(home, 3, 1);
        var unit = Scout(sim, home, x + 1, y);
        Assert.Equal(home.Id, unit.TileId);

        // Two tiles away, through whatever lies between.
        var far = sim.Tiles.First(t => !home.Neighbors.Contains(t.Id) && t != home
            && t.Neighbors.Any(n => home.Neighbors.Contains(n)));
        var (gx, gy) = (far.CenterX, far.CenterY);
        var target = Enumerable.Range(0, far.Grid.Width * far.Grid.Height)
            .Select(i => (X: i % far.Grid.Width, Y: i / far.Grid.Width))
            .Where(c => far.Grid.InBounds(c.X, c.Y))
            .MinBy(c => Math.Abs(far.OriginX + c.X - gx) + Math.Abs(far.OriginY + c.Y - gy));
        Move(sim, unit, far, target.X, target.Y);
        sim.Step();
        Assert.True(unit.Moving);

        var visited = new List<int> { unit.TileId };
        for (int i = 0; i < 20_000 && unit.Moving; i++)
        {
            sim.Step();
            if (unit.TileId != visited[^1])
                visited.Add(unit.TileId);
        }
        Assert.False(unit.Moving, "never arrived");
        Assert.Equal(far.Id, unit.TileId);
        Assert.True(CellsTo(unit, target.X, target.Y, far) < 2, $"stopped {CellsTo(unit, target.X, target.Y, far):F2} cells away");
        // Every hop was to a neighbour.
        for (int i = 1; i < visited.Count; i++)
            Assert.Contains(visited[i], sim.Tiles[visited[i - 1]].Neighbors);
    }

    [Fact]
    public void FramesCostItemsAndAreRefundedOnRemoval()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        long iron = Items(tile, "iron_ore");

        sim.Enqueue(new PlaceFrameCommand(Me, tile.Id, 4, 4));
        sim.Enqueue(new PlaceFrameCommand(Me, tile.Id, 4, 4)); // already framed: no second charge
        sim.Step();
        Assert.True(tile.Grid.HasFrame(4, 4));
        Assert.Equal(iron - 2, Items(tile, "iron_ore"));

        sim.Enqueue(new RemoveFrameCommand(Me, tile.Id, 4, 4));
        sim.Step();
        Assert.False(tile.Grid.HasFrame(4, 4));
        Assert.Equal(iron, Items(tile, "iron_ore"));
    }

    [Fact]
    public void FramesNeedPresenceAndACellInsideTheTile()
    {
        var sim = NewSim();
        var tile = Tile(sim, size: 10);
        Assert.Equal(PlaceResult.OutOfBounds, UnitAssembly.CheckFrame(sim, tile, Me, 10, 0));
        Assert.Equal(PlaceResult.NoPresence, UnitAssembly.CheckFrame(sim, tile, Them, 1, 1));
    }

    [Fact]
    public void FramesAndBuildingsOverlap()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Place(sim, tile, "thruster", 5, 5);
        Frame(sim, tile, 5, 5);
        sim.Step();
        Assert.True(tile.Grid.HasFrame(5, 5));
        Assert.NotNull(tile.Grid.BuildingAt(5, 5));
    }

    // --- activation ---

    [Fact]
    public void TriggeringTheCoreLiftsTheFramedBlocksOffAsAUnit()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        var unit = Scout(sim, tile);

        Assert.Equal(0, tile.Grid.BuildingCount);
        Assert.Equal(0, tile.Grid.FrameCount);
        Assert.Equal(3, unit.Cells.Count);
        Assert.Equal(2, unit.Thrusters.Count);
        Assert.Equal(3, unit.Blocks.Count);
        Assert.Equal(Me, unit.Owner);
        // The blocks are symmetric about the core, so the centre of mass is the core's cell centre.
        Assert.InRange(unit.X, (int)(10.4 * S), (int)(10.6 * S));
        Assert.InRange(unit.Y, (int)(10.4 * S), (int)(10.6 * S));
        Assert.False(unit.Moving);
    }

    [Fact]
    public void MassIsFrameAndBuildingCosts()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        var unit = Scout(sim, tile);
        // Thrusters cost 15 each, the core 15, and 3 frame cells at 2.
        Assert.Equal(15 * 3 + 2 * 3, unit.Mass);
    }

    [Fact]
    public void AUnitAtRestStaysPut()
    {
        var sim = NewSim();
        var unit = Scout(sim, Tile(sim));
        var (x, y, angle) = (unit.X, unit.Y, unit.Angle);
        Run(sim, 120);
        Assert.Equal((x, y, angle), (unit.X, unit.Y, unit.Angle));
    }

    [Fact]
    public void ActivationIsRefusedWhenTheFrameCutsABuildingInHalf()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Harness.Ore(tile, "iron_ore", 5, 5);
        Place(sim, tile, "drill", 5, 5); // 2x2
        Frame(sim, tile, 4, 4, 2, 2);    // covers only part of the drill
        Place(sim, tile, "unit_core", 4, 4);
        sim.Step();
        Assert.Equal(AssembleResult.PartialBuilding, UnitAssembly.CheckActivate(sim, tile, Me, 4, 4, out _));
    }

    [Fact]
    public void ActivationIsRefusedForTheWrongBlocks()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Assert.Equal(AssembleResult.NoCore, UnitAssembly.CheckActivate(sim, tile, Me, 3, 3, out _));

        Place(sim, tile, "unit_core", 3, 3);
        sim.Step();
        Assert.Equal(AssembleResult.NoFrame, UnitAssembly.CheckActivate(sim, tile, Me, 3, 3, out _));

        Frame(sim, tile, 3, 3, 3);
        Place(sim, tile, "unit_core", 5, 3);
        sim.Step();
        Assert.Equal(AssembleResult.MultipleCores, UnitAssembly.CheckActivate(sim, tile, Me, 3, 3, out _));
        Assert.Equal(AssembleResult.NotOwner, UnitAssembly.CheckActivate(sim, tile, Them, 3, 3, out _));
    }

    [Fact]
    public void ActivationIsRefusedForTheTileCoreAndForPorts()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Frame(sim, tile, 3, 3, 4, 3);
        Place(sim, tile, "unit_core", 3, 3);
        Place(sim, tile, "core", 4, 3);
        sim.Step();
        Assert.Equal(AssembleResult.ContainsTileCore, UnitAssembly.CheckActivate(sim, tile, Me, 3, 3, out _));

        var sim2 = NewSim();
        var tile2 = Tile(sim2);
        Frame(sim2, tile2, 3, 3, 3);
        Place(sim2, tile2, "unit_core", 3, 3);
        Place(sim2, tile2, "export_port", 4, 3, 0);
        sim2.Step();
        // Ports are only legal in a port zone, and a tile with no neighbours has none, so the placement is refused.
        Assert.Null(tile2.Grid.BuildingAt(4, 3));

        // A neighbour to the east gives tile 3 a port zone along its east side.
        var sim3 = NewSim();
        var tile3 = Tile(sim3, size: 6);
        Tile(sim3, 1, 0, size: 6);
        Frame(sim3, tile3, 2, 2, 3);
        Place(sim3, tile3, "unit_core", 2, 2);
        Place(sim3, tile3, "export_port", 3, 2, 0);
        sim3.Step();
        Assert.Equal(AssembleResult.InvalidBlock, UnitAssembly.CheckActivate(sim3, tile3, Me, 2, 2, out _));
    }

    [Fact]
    public void AFrameBiggerThanTheCapIsRefused()
    {
        var sim = NewSim();
        var tile = Tile(sim, size: 30);
        Frame(sim, tile, 0, 0, 9, 8); // 72 cells
        Place(sim, tile, "unit_core", 0, 0);
        sim.Step();
        Assert.Equal(AssembleResult.TooLarge, UnitAssembly.CheckActivate(sim, tile, Me, 0, 0, out _));
    }

    [Fact]
    public void ItemsHeldByLiftedBuildingsGoBackToTheTile()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Harness.Ore(tile, "iron_ore", 12, 10);
        Frame(sim, tile, 10, 10, 4, 2);
        Place(sim, tile, "unit_core", 10, 10);
        Place(sim, tile, "thruster", 11, 10);
        Place(sim, tile, "drill", 12, 10);
        Run(sim, 61); // one iron ore in the drill's output buffer
        long before = Items(tile, "iron_ore");

        sim.Enqueue(new ActivateUnitCommand(Me, tile.Id, 10, 10));
        sim.Step();
        Assert.Single(sim.Units);
        Assert.True(Items(tile, "iron_ore") > before);
    }

    // --- moving ---

    [Fact]
    public void AUnitFlipsTowardsItsGoalAndStopsThere()
    {
        var sim = NewSim();
        var tile = Tile(sim, size: 30);
        var unit = Scout(sim, tile, 20, 10);
        Move(sim, unit, tile, 8, 10);

        Assert.True(RunUntil(sim, () => !unit.Moving, 1500), "never arrived");
        Run(sim, 200); // let it coast to a standstill
        Assert.True(CellsTo(unit, 8, 10) < 1.5, $"stopped {CellsTo(unit, 8, 10):F2} cells away");
        Assert.Equal((0, 0), (unit.VX, unit.VY));
    }

    [Fact]
    public void AUnitTurnsItsEngineTowardsItsTarget()
    {
        var sim = NewSim();
        var tile = Tile(sim, size: 30);
        var unit = Scout(sim, tile, 20, 10);
        Move(sim, unit, tile, 2, 10); // due west: the engines point east at first
        Run(sim, 200);
        // Engines face the way it flies, so the unit has come round to face west (half a turn).
        Assert.InRange(Math.Abs(Trig.Diff(unit.Angle, Trig.Turn / 2)), 0, Trig.Turn / 8);
        Assert.True(unit.X < 18 * S, "should be well on its way");
    }

    [Fact]
    public void AUnitFliesAcrossATileEdge()
    {
        var sim = NewSim();
        var a = Tile(sim, 0, 0, size: 20);
        var b = Tile(sim, 1, 0, size: 20);
        var unit = Scout(sim, a, 15, 10);
        Move(sim, unit, b, 10, 10);

        Assert.True(RunUntil(sim, () => unit.TileId == b.Id, 1500), "never crossed");
        Assert.True(unit.X - b.OriginX * S < 6 * S, "arrives at the west edge of the next tile");
        Assert.True(RunUntil(sim, () => !unit.Moving, 2000), "never arrived");
        Run(sim, 200);
        Assert.True(CellsTo(unit, 10, 10, b) < 1.5);
        Assert.Equal(b.Id, unit.TileId);
    }

    [Fact]
    public void TheTileAUnitIsOnIsTheTileUnderItsCentreAndItsPathIsContinuous()
    {
        var sim = NewSim();
        var a = Tile(sim, 0, 0, size: 20);
        var b = Tile(sim, 1, 0, size: 20);
        var unit = Scout(sim, a, 12, 10);
        Move(sim, unit, b, 10, 10);

        long maxStep = 0;
        int px = unit.X, py = unit.Y;
        for (int i = 0; i < 2500 && unit.Moving; i++)
        {
            sim.Step();
            // Global coordinates: crossing the border is no jump, just another step.
            maxStep = Math.Max(maxStep, Math.Abs((long)unit.X - px) + Math.Abs((long)unit.Y - py));
            (px, py) = (unit.X, unit.Y);
            Assert.Equal(sim.Map.TileAtCell(unit.X / S, unit.Y / S), unit.TileId);
        }
        Assert.False(unit.Moving);
        Assert.True(maxStep < S, $"jumped {maxStep / (double)S:F2} cells in a tick");
    }

    [Fact]
    public void UnitsOnEitherSideOfABorderCollide()
    {
        var sim = NewSim();
        var a = Tile(sim, 0, 0, size: 20);
        var b = Tile(sim, 1, 0, size: 20);
        var left = Scout(sim, a, 17, 10);
        var right = Scout(sim, b, 3, 10);
        // Each heads for the other's side: they meet at the border.
        Move(sim, left, b, 5, 10);
        Move(sim, right, a, 15, 10);
        long closest = long.MaxValue;
        for (int i = 0; i < 300; i++)
        {
            sim.Step();
            closest = Math.Min(closest, Math.Abs((long)left.X - right.X) + Math.Abs((long)left.Y - right.Y));
        }
        // Their bodies are three cells long; without collisions their centres would pass right through each other.
        Assert.True(closest > S / 2, $"centres came within {closest / (double)S:F2} cells");
    }

    [Fact]
    public void AUnitStopsAtTheEdgeOfTheWorld()
    {
        var sim = NewSim();
        var a = Tile(sim, 0, 0, size: 20);
        var unit = Scout(sim, a, 12, 10);
        // Flat out for the westmost cell: there is nothing past it, so the unit is held at the border.
        Move(sim, unit, a, 0, 10);
        for (int i = 0; i < 900; i++)
        {
            sim.Step();
            Assert.True(unit.X >= 0, "left the world");
            Assert.Equal(a.Id, unit.TileId);
        }
    }

    [Fact]
    public void AUnitRoutesThroughSeveralTilesInAnyDirection()
    {
        var sim = NewSim();
        var tiles = new TileState[3, 3];
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
                tiles[x, y] = Tile(sim, x, y, size: 16);
        var unit = Scout(sim, tiles[2, 2], 8, 8);

        Move(sim, unit, tiles[0, 0], 8, 8);
        Assert.True(RunUntil(sim, () => !unit.Moving, 6000), "never arrived");
        Assert.Equal(tiles[0, 0].Id, unit.TileId);

        Move(sim, unit, tiles[2, 0], 8, 8);
        Assert.True(RunUntil(sim, () => !unit.Moving, 4000), "never arrived");
        Assert.Equal(tiles[2, 0].Id, unit.TileId);
    }

    [Fact]
    public void UnitsCanCrossTilesTheirOwnerDoesNotOwn()
    {
        var sim = NewSim();
        var a = Tile(sim, 0, 0, size: 16);
        Tile(sim, 1, 0, size: 16, owner: TileState.Unowned);
        var c = Tile(sim, 2, 0, size: 16);
        var unit = Scout(sim, a, 8, 8);
        Move(sim, unit, c, 8, 8);
        Assert.True(RunUntil(sim, () => !unit.Moving, 6000), "never arrived");
        Assert.Equal(c.Id, unit.TileId);
    }

    [Fact]
    public void OrdersFromOthersToNowhereOrToUnitsWithoutEnginesAreIgnored()
    {
        var sim = NewSim();
        var a = Tile(sim, 0, 0, size: 20);
        var island = Tile(sim, 5, 5, size: 20); // nothing between
        var unit = Scout(sim, a);

        Move(sim, unit, a, 3, 3, player: Them);
        Move(sim, unit, island, 2, 2);
        Move(sim, unit, a, 99, 99);
        sim.Step();
        Assert.False(unit.Moving);

        // A block with a core but no engine has no locomotion, so it can't lift off at all (UD-003).
        Frame(sim, a, 2, 2);
        Place(sim, a, "unit_core", 2, 2);
        sim.Step();
        Assert.Equal(AssembleResult.NoLocomotion, UnitAssembly.CheckActivate(sim, a, Me, 2, 2, out _));
    }

    [Fact]
    public void AUnitFliesRoundABuildingInItsWay()
    {
        var sim = NewSim();
        var tile = Tile(sim, size: 30);
        Place(sim, tile, "core", 14, 9); // 3x3 square straight across the line y=10
        sim.Step();
        var unit = Scout(sim, tile, 6, 10);
        Move(sim, unit, tile, 25, 10);

        // Every cell of the unit stays out of the building the whole way.
        bool touched = false;
        bool arrived = RunUntil(sim, () =>
        {
            foreach (var c in unit.Cells)
            {
                var (cx, cy) = unit.CellWorld(c);
                if (cx > 14.0 * S && cx < 17.0 * S && cy > 9.0 * S && cy < 12.0 * S)
                    touched = true;
            }
            return !unit.Moving;
        }, 2500);
        Assert.True(arrived, "never arrived");
        Assert.False(touched, "flew through the building");
        Run(sim, 200);
        Assert.True(CellsTo(unit, 25, 10) < 1.5, $"stopped {CellsTo(unit, 25, 10):F2} cells away");
    }

    [Fact]
    public void AUnitFindsItsWayThroughAGapInAWall()
    {
        var sim = NewSim();
        var tile = Tile(sim, size: 30);
        for (int y = 0; y < 30; y += 2)
        {
            if (y is >= 12 and <= 16)
                continue; // a gap wide enough for the unit
            Place(sim, tile, "smelter", 15, y, 0, "smelt_iron");
        }
        sim.Step();
        var unit = Scout(sim, tile, 6, 4);
        Move(sim, unit, tile, 25, 26);
        Assert.True(RunUntil(sim, () => !unit.Moving, 4000), "never arrived");
        Run(sim, 200);
        Assert.True(CellsTo(unit, 25, 26) < 1.5, $"stopped {CellsTo(unit, 25, 26):F2} cells away");
    }

    [Fact]
    public void AUnitStopsAtAWallWithNoWayRound()
    {
        var sim = NewSim();
        var tile = Tile(sim, size: 30);
        for (int y = 0; y < 30; y += 2)
            Place(sim, tile, "smelter", 15, y, 0, "smelt_iron");
        sim.Step();
        var unit = Scout(sim, tile, 6, 10);
        Move(sim, unit, tile, 25, 10);
        Run(sim, 900);
        Assert.True(unit.X < 15 * S, "passed through the wall");
        Assert.True(unit.Moving);
    }

    [Fact]
    public void PlacingABuildingOnAUnitsPathMakesItPlanAgain()
    {
        var sim = NewSim();
        var tile = Tile(sim, size: 30);
        var unit = Scout(sim, tile, 6, 10);
        Move(sim, unit, tile, 25, 10);
        Run(sim, 30);
        Place(sim, tile, "core", 14, 9);
        Assert.True(RunUntil(sim, () => !unit.Moving, 2500), "never arrived");
        Run(sim, 200);
        Assert.True(CellsTo(unit, 25, 10) < 1.5);
    }

    [Fact]
    public void UnitsPushEachOtherApart()
    {
        var sim = NewSim();
        var tile = Tile(sim, size: 30);
        var a = Scout(sim, tile, 5, 5);
        LayScout(sim, tile, 5, 14);
        sim.Enqueue(new ActivateUnitCommand(Me, tile.Id, 5, 14));
        sim.Step();
        var b = sim.Units.Single(u => u.Id != a.Id);

        sim.Enqueue(new MoveUnitsCommand(Me, [a.Id], tile.Id, 20, 10));
        sim.Enqueue(new MoveUnitsCommand(Me, [b.Id], tile.Id, 20, 10));
        Run(sim, 1500);

        double apart = Math.Sqrt(Math.Pow(a.X - (double)b.X, 2) + Math.Pow(a.Y - (double)b.Y, 2)) / S;
        Assert.True(apart > 1.2, $"overlapping: {apart:F2} cells apart");
    }

    // --- landing ---

    [Fact]
    public void ALandedUnitPutsItsBlocksBackWhereItStands()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        var unit = Scout(sim, tile);

        sim.Enqueue(new DeployUnitCommand(Me, unit.Id));
        sim.Step();

        Assert.Empty(sim.Units);
        Assert.Equal("unit_core", tile.Grid.BuildingAt(10, 10)!.DefId);
        Assert.Equal("thruster", tile.Grid.BuildingAt(9, 10)!.DefId);
        Assert.Equal("thruster", tile.Grid.BuildingAt(11, 10)!.DefId);
        Assert.Equal(3, tile.Grid.FrameCount);
    }

    [Fact]
    public void BlocksKeepTheirHitPointsAndOwnerThroughLiftOffAndLanding()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        LayScout(sim, tile, 10, 10);
        var thruster = tile.Grid.BuildingAt(9, 10)!;
        Assert.Equal(Me, thruster.Owner);
        Assert.Equal(sim.Content.Buildings["thruster"].Hp, thruster.Hp);

        sim.Enqueue(new ActivateUnitCommand(Me, tile.Id, 10, 10));
        sim.Step();
        var unit = Assert.Single(sim.Units);
        Assert.Equal(sim.Content.Buildings["thruster"].Hp, unit.Blocks.Single(b => b.GX == -1).Hp);

        sim.Enqueue(new DeployUnitCommand(Me, unit.Id));
        sim.Step();
        var landed = tile.Grid.BuildingAt(9, 10)!;
        Assert.Equal(sim.Content.Buildings["thruster"].Hp, landed.Hp);
        Assert.Equal(Me, landed.Owner);
        Assert.Equal(sim.Content.Buildings["unit_core"].Hp, tile.Grid.BuildingAt(10, 10)!.Hp);
    }

    [Fact]
    public void ATurnedUnitLandsTurned()
    {
        var sim = NewSim();
        var tile = Tile(sim, size: 30);
        var unit = Scout(sim, tile, 20, 10);
        Move(sim, unit, tile, 6, 10);
        Assert.True(RunUntil(sim, () => !unit.Moving, 2000));
        Run(sim, 300);

        // It lands turned to the nearest quarter of however it ended up facing.
        int quarters = ((unit.Angle + Trig.Quarter / 2) / Trig.Quarter) & 3;
        Assert.NotEqual(0, quarters);
        sim.Enqueue(new DeployUnitCommand(Me, unit.Id));
        sim.Step();
        Assert.Empty(sim.Units);

        var thrusters = tile.Grid.Buildings.Where(b => b.DefId == "thruster").ToList();
        Assert.Equal(2, thrusters.Count);
        Assert.All(thrusters, t => Assert.Equal(quarters, t.Rotation));
        Assert.Equal(1, tile.Grid.Buildings.Count(b => b.DefId == "unit_core"));
    }

    [Fact]
    public void LandingIsBlockedByBuildingsInTheWay()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        var unit = Scout(sim, tile);
        Frame(sim, tile, 11, 10); // landing needs bare ground
        sim.Step();

        sim.Enqueue(new DeployUnitCommand(Me, unit.Id));
        sim.Step();
        Assert.Single(sim.Units);
        Assert.Equal(DeployResult.Blocked, UnitAssembly.CheckDeploy(sim, Me, unit.Id, out _));
    }

    // --- misc ---

    [Fact]
    public void ResolveCellWalksAcrossNeighbours()
    {
        var sim = NewSim();
        var a = Tile(sim, 0, 0, size: 10);
        var b = Tile(sim, 1, 0, size: 10);
        Assert.Equal((b, 3, 4), sim.ResolveCell(a, 13, 4));
        Assert.Equal((a, 9, 4), sim.ResolveCell(b, -1, 4));
        Assert.Null(sim.ResolveCell(a, -1, 0));
        Assert.Null(sim.ResolveCell(a, 5, 10));
    }

    [Fact]
    public void TrigMatchesTheRealThing()
    {
        for (int a = 0; a < Trig.Turn; a += 97)
        {
            double r = a * 2 * Math.PI / Trig.Turn;
            // The table has 1024 steps a turn, so allow for the angle being rounded down.
            Assert.InRange(Trig.Sin(a), Math.Sin(r) * Trig.One - 30, Math.Sin(r) * Trig.One + 30);
            Assert.InRange(Trig.Cos(a), Math.Cos(r) * Trig.One - 30, Math.Cos(r) * Trig.One + 30);
        }
        Assert.Equal(0, Trig.Sin(0));
        Assert.Equal(Trig.One, Trig.Cos(0));
        Assert.Equal(Trig.One, Trig.Sin(Trig.Quarter));

        foreach (var (x, y) in new[] { (1000, 0), (0, 1000), (-1000, 0), (0, -1000), (700, 700), (-300, 900), (-800, -50), (123456, -654321) })
        {
            int expected = (int)Math.Round(Math.Atan2(y, x) / (2 * Math.PI) * Trig.Turn);
            Assert.InRange(Math.Abs(Trig.Diff(Trig.Atan2(y, x), expected)), 0, 8);
        }
        Assert.Equal(200, Trig.Diff(100, 65536 - 100));
        Assert.Equal(-200, Trig.Diff(65536 - 100, 100));
    }

    [Fact]
    public void UnitSimulationIsDeterministic()
    {
        ulong RunOnce()
        {
            var sim = NewSim();
            var a = Tile(sim, 0, 0, size: 20);
            Tile(sim, 1, 0, size: 20);
            var unit = Scout(sim, a, 15, 10);
            Move(sim, unit, sim.Tiles[1], 6, 3);
            Run(sim, 700);
            return sim.StateHash();
        }
        Assert.Equal(RunOnce(), RunOnce());
    }
}
