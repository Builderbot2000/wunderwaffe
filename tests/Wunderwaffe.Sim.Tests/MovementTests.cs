using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

/// <summary>Locomotion (UD-004), terrain-aware routes (UC-005, UC-007, IO-003), orders, squads, formations, stances, rally.</summary>
public class MovementTests
{
    const int S = Unit.CellScale;

    /// <summary>The standard scout (<see cref="Harness.LayScout"/>) plus <paramref name="extra"/> blocks to the right; lifted off.</summary>
    static Unit Scout(Simulation sim, TileState tile, int x, int y, params string[] extra)
    {
        LayUnit(sim, tile, x, y, Me, ["thruster", .. extra]);
        return LiftOff(sim, tile, x, y);
    }

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

    static double CellsTo(Unit u, TileState on, int cx, int cy) =>
        Math.Sqrt(Math.Pow(u.X - (cx + on.OriginX + 0.5) * S, 2) + Math.Pow(u.Y - (cy + on.OriginY + 0.5) * S, 2)) / S;

    static double Dist(long ax, long ay, long bx, long by) => Math.Sqrt(Math.Pow(ax - bx, 2) + Math.Pow(ay - by, 2)) / S;

    // --- locomotion classes (UD-004) ---

    [Theory]
    [InlineData(new[] { "thruster", "core" }, LocomotionResult.Ok, LocomotionClass.Ground)]
    [InlineData(new[] { "thruster", "wheels" }, LocomotionResult.Ok, LocomotionClass.Ground)]
    [InlineData(new[] { "thruster", "hull" }, LocomotionResult.Ok, LocomotionClass.Naval)]
    [InlineData(new[] { "thruster", "rotor", "hull", "wheels" }, LocomotionResult.Ok, LocomotionClass.Air)]
    [InlineData(new[] { "hull", "wheels" }, LocomotionResult.Conflicting, LocomotionClass.Naval)]
    [InlineData(new[] { "core", "drill" }, LocomotionResult.None, LocomotionClass.Ground)]
    public void BlocksDecideTheLocomotionClass(string[] blocks, LocomotionResult result, LocomotionClass cls)
    {
        var defs = TestContent.Load().Buildings;
        Assert.Equal(result, Locomotion.Check(defs, blocks, out var got));
        Assert.Equal(cls, got);
        Assert.Equal(result == LocomotionResult.Ok ? cls : null, Locomotion.Of(defs, blocks));
    }

    [Fact]
    public void TerrainSaysWhoCrossesIt()
    {
        var terrain = TestContent.Load().TerrainList.ToDictionary(t => t.Id);
        Assert.True(Locomotion.CanCross(LocomotionClass.Ground, terrain["shallow_water"]));
        Assert.True(Locomotion.CanCross(LocomotionClass.Ground, terrain["river"]));
        Assert.False(Locomotion.CanCross(LocomotionClass.Ground, terrain["deep_water"]));
        Assert.False(Locomotion.CanCross(LocomotionClass.Ground, terrain["peak"]));
        Assert.True(Locomotion.CanCross(LocomotionClass.Naval, terrain["deep_water"]));
        Assert.True(Locomotion.CanCross(LocomotionClass.Naval, terrain["river"]));
        Assert.False(Locomotion.CanCross(LocomotionClass.Naval, terrain["plains"]));
        Assert.True(terrain.Values.All(t => Locomotion.CanCross(LocomotionClass.Air, t)));
    }

    [Fact]
    public void AUnitGetsItsClassFromItsBlocks()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        Assert.Equal(LocomotionClass.Air, Scout(sim, tile, 5, 5, "rotor").Locomotion);
        Assert.Equal(LocomotionClass.Ground, Scout(sim, tile, 5, 10).Locomotion);
    }

    [Fact]
    public void LiftOffRefusesAHullWithWheels()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        LayUnit(sim, tile, 5, 5, Me, "hull", "wheels");
        Assert.Equal(AssembleResult.ConflictingLocomotion, UnitAssembly.CheckActivate(sim, tile, Me, 5, 5, out _));

        // Swapping the wheels for a rotor makes it an air unit.
        sim.Enqueue(new RemoveBuildingCommand(Me, tile.Id, 8, 5));
        Place(sim, tile, "rotor", 8, 5);
        sim.Step();
        sim.Enqueue(new ActivateUnitCommand(Me, tile.Id, 5, 5));
        sim.Step();
        Assert.Equal(LocomotionClass.Air, Assert.Single(sim.Units).Locomotion);
    }

    // --- terrain-aware routes on a generated world (UC-005, UC-007, IO-003) ---

    static (Simulation Sim, (int X, int Y) Land, (int X, int Y) Deep) WorldWithSea()
    {
        var content = TestContent.Load();
        int deepId = content.TerrainList.ToList().FindIndex(t => t.Id == "deep_water");
        for (ulong seed = 3037; ; seed++)
        {
            var sim = Simulation.NewGame(content, GameSetup.Default with { Seed = seed });
            var home = sim.Tiles[sim.StartTileId];
            var map = sim.Map;
            (int, int)? deep = null;
            for (int y = 0; y < map.Height && deep == null; y += 3)
            {
                for (int x = 0; x < map.Width && deep == null; x += 3)
                {
                    if (map.TileAtCell(x, y) >= 0 && map.TerrainAt(x, y) == deepId)
                        deep = (x, y);
                }
            }
            if (deep is { } d)
                return (sim, home.ToGlobal(Open(home, 3, 3).X + 1, Open(home, 3, 3).Y + 1), d);
        }
    }

    [Fact]
    public void RoutesKeepToTerrainTheLocomotionCanCross()
    {
        var (sim, land, deep) = WorldWithSea();
        var deepTile = sim.TileAtCell(deep.X, deep.Y)!;
        long fx = (long)land.X * S + S / 2, fy = (long)land.Y * S + S / 2;

        // Air goes exactly where it is sent.
        var air = UnitNav.Plan(sim, LocomotionClass.Air, 0, 100, fx, fy, deepTile.Id, deep.X - deepTile.OriginX, deep.Y - deepTile.OriginY);
        Assert.NotNull(air);
        Assert.Equal(deep, air.Waypoints[^1]);

        // Ground stops short of deep water, and every waypoint is on ground it can cross.
        var ground = UnitNav.Plan(sim, LocomotionClass.Ground, 1, 100, fx, fy, deepTile.Id, deep.X - deepTile.OriginX, deep.Y - deepTile.OriginY);
        if (ground != null)
        {
            Assert.NotEqual(deep, ground.Waypoints[^1]);
            Assert.All(ground.Waypoints, p => Assert.True(Locomotion.CanCross(sim, LocomotionClass.Ground, p.X, p.Y), $"{p} is not ground"));
            Assert.All(ground.Tiles, t => Assert.True(t >= 0));
        }

        // Naval from the sea can't reach the land cell itself.
        var landTile = sim.TileAtCell(land.X, land.Y)!;
        var naval = UnitNav.Plan(sim, LocomotionClass.Naval, 0, 100, (long)deep.X * S + S / 2, (long)deep.Y * S + S / 2,
            landTile.Id, land.X - landTile.OriginX, land.Y - landTile.OriginY);
        if (naval != null)
            Assert.All(naval.Waypoints, p => Assert.True(Locomotion.CanCross(sim, LocomotionClass.Naval, p.X, p.Y), $"{p} is not water"));
    }

    [Fact]
    public void AGroundUnitSentOutToSeaStopsAtTheShore()
    {
        var (sim, land, deep) = WorldWithSea();
        var home = sim.Tiles[sim.StartTileId];
        foreach (var item in new[] { "copper_ore", "iron_ore", "copper_plate", "iron_plate", "steel" })
            Fund(sim, home, item, 10_000);
        var unit = Scout(sim, home, land.X - home.OriginX, land.Y - home.OriginY);
        var deepTile = sim.TileAtCell(deep.X, deep.Y)!;
        var plan = UnitNav.Plan(sim, unit, deepTile.Id, deep.X - deepTile.OriginX, deep.Y - deepTile.OriginY);

        sim.Enqueue(new MoveUnitsCommand(Me, [unit.Id], deepTile.Id, deep.X - deepTile.OriginX, deep.Y - deepTile.OriginY));
        sim.Step();
        if (plan == null)
        {
            Assert.False(unit.Moving);
            return;
        }
        Assert.True(unit.Moving);
        for (int i = 0; i < 30_000 && unit.Moving; i++)
        {
            sim.Step();
            Assert.True(Locomotion.CanCross(sim, LocomotionClass.Ground, unit.X / S, unit.Y / S), $"centre on impassable terrain at tick {sim.Tick}");
        }
    }

    // --- orders (UC-001) ---

    [Fact]
    public void HoldStopsAUnitAndKeepsItThere()
    {
        var sim = NewSim();
        var tile = Tile(sim, size: 40);
        var unit = Scout(sim, tile, 5, 5);
        sim.Enqueue(new MoveUnitsCommand(Me, [unit.Id], tile.Id, 35, 35));
        Run(sim, 60);
        Assert.Equal(OrderKind.Move, unit.Order?.Kind);
        sim.Enqueue(new HoldUnitsCommand(Me, [unit.Id]));
        Run(sim, 300);
        Assert.False(unit.Moving);
        Assert.Equal(OrderKind.Hold, unit.Order?.Kind);
        Assert.True(Math.Abs(unit.VX) + Math.Abs(unit.VY) < S / 100);
        Assert.False(UnitSim.MayChase(sim, unit, unit.X + S, unit.Y));
    }

    [Fact]
    public void APatrolGoesBackAndForth()
    {
        var sim = NewSim();
        var tile = Tile(sim, size: 40);
        var unit = Scout(sim, tile, 8, 8);
        sim.Enqueue(new PatrolUnitsCommand(Me, [unit.Id], tile.Id, 30, 8));
        Assert.True(RunUntil(sim, () => CellsTo(unit, tile, 30, 8) < 1.5, 3000), "never reached the far end");
        Assert.True(RunUntil(sim, () => CellsTo(unit, tile, 8, 8) < 1.5, 3000), "never came back");
        Assert.True(RunUntil(sim, () => CellsTo(unit, tile, 30, 8) < 1.5, 3000), "stopped patrolling");
        Assert.Equal(OrderKind.Patrol, unit.Order?.Kind);
    }

    [Fact]
    public void RetreatFallsBackToAnOwnedTile()
    {
        var sim = NewSim();
        var home = Tile(sim, 0, 0, size: 16);
        var away = Tile(sim, 1, 0, size: 16, owner: TileState.Unowned);
        var unit = Scout(sim, home, 8, 8);
        sim.Enqueue(new MoveUnitsCommand(Me, [unit.Id], away.Id, 8, 8));
        Assert.True(RunUntil(sim, () => !unit.Moving, 4000));
        Assert.Equal(away.Id, unit.TileId);

        Assert.Equal(home, UnitSim.RetreatTile(sim, unit));
        sim.Enqueue(new RetreatUnitsCommand(Me, [unit.Id]));
        sim.Step();
        Assert.Equal(OrderKind.Retreat, unit.Order?.Kind);
        Assert.False(UnitSim.CanFire(unit));
        Assert.True(RunUntil(sim, () => !unit.Moving, 4000));
        Assert.Equal(home.Id, unit.TileId);
        Assert.Null(unit.Order);
    }

    // --- squads and formations (UC-002, UC-003) ---

    [Fact]
    public void ASquadSharesItsOrderAndFormation()
    {
        var sim = NewSim();
        var tile = Tile(sim, size: 60);
        var units = new[] { Scout(sim, tile, 5, 5), Scout(sim, tile, 5, 8), Scout(sim, tile, 5, 11, "thruster", "thruster") };
        int[] ids = [.. units.Select(u => u.Id)];
        ulong before = sim.StateHash();
        sim.Enqueue(new CreateSquadCommand(Me, "Alpha", ids));
        sim.Enqueue(new SetFormationCommand(Me, 1, Formation.Line));
        sim.Step();
        Assert.NotEqual(before, sim.StateHash());
        var squad = sim.Squads.ById(1)!;
        Assert.Equal("Alpha", squad.Name);
        Assert.Equal(ids, squad.Members);
        Assert.All(units, u => Assert.Equal(1, u.SquadId));

        sim.Enqueue(new MoveUnitsCommand(Me, ids, tile.Id, 45, 30));
        sim.Step();
        Assert.Equal(new UnitOrder(OrderKind.Move, tile.Id, 45, 30), squad.Order);
        var lineSlots = units.Select(u => (u.DestX, u.DestY)).ToList();
        Assert.Equal(3, lineSlots.Distinct().Count());

        // Re-forming on the way keeps the order and the target, and moves the slots.
        Run(sim, 30);
        sim.Enqueue(new SetFormationCommand(Me, 1, Formation.Column));
        sim.Step();
        Assert.All(units, u => Assert.Equal(new UnitOrder(OrderKind.Move, tile.Id, 45, 30), u.Order));
        Assert.All(units, u => Assert.True(u.Moving));
        Assert.NotEqual(lineSlots, units.Select(u => (u.DestX, u.DestY)).ToList());
        Assert.True(RunUntil(sim, () => units.All(u => !u.Moving), 6000), "never arrived");
        // A column: one behind another, centred on the target.
        double spread = units.Max(u => Dist(u.X, u.Y, units[0].X, units[0].Y));
        Assert.True(spread > 4, $"column spread {spread:F1} cells");
    }

    [Fact]
    public void FormationSlotsMakeRoomForBigUnits()
    {
        var sim = NewSim();
        var tile = Tile(sim, size: 40);
        var small = Scout(sim, tile, 5, 5);
        var big = Scout(sim, tile, 5, 10, "thruster", "thruster", "thruster", "thruster");
        var slots = UnitSim.Slots(Formation.Line, [], [small, big, small], 0);
        // Heading east, a line runs north-south; neighbours sit at least their radii apart.
        long gap = slots[1].Y - slots[0].Y;
        Assert.True(gap >= small.Radius + big.Radius, $"gap {gap} < radii {small.Radius + big.Radius}");
        Assert.Equal(0, slots.Sum(s => s.Y) / S); // centred

        var wedge = UnitSim.Slots(Formation.Wedge, [], [small, small, small], 0);
        Assert.True(wedge[0].X > wedge[1].X && wedge[1].X == wedge[2].X && wedge[1].Y == -wedge[2].Y, "point of the wedge leads");

        var custom = UnitSim.Slots(Formation.Custom, [(2, 0), (0, 3)], [small, small, small], Trig.Quarter);
        Assert.Equal((0L, 2L * S), custom[0]); // forward is south when heading a quarter turn
        Assert.True(custom[2].Y < custom[1].Y, "extra units queue up behind");
    }

    [Fact]
    public void SquadsCanBeRenamedReassignedAndDisbanded()
    {
        var sim = NewSim();
        var tile = Tile(sim, size: 40);
        var a = Scout(sim, tile, 5, 5);
        var b = Scout(sim, tile, 5, 8);
        Assert.Equal(SquadResult.BadName, UnitSim.CheckCreateSquad(sim, Me, " ", [a.Id]));
        Assert.Equal(SquadResult.NoUnits, UnitSim.CheckCreateSquad(sim, Them, "x", [a.Id]));
        sim.Enqueue(new CreateSquadCommand(Me, "One", [a.Id]));
        sim.Enqueue(new CreateSquadCommand(Me, "Two", [b.Id]));
        sim.Step();
        Assert.Equal(SquadResult.NotOwner, UnitSim.CheckSquad(sim, Them, 1));
        Assert.Equal(SquadResult.BadFormation, UnitSim.CheckFormation(sim, Me, 1, Formation.Custom, [1]));

        sim.Enqueue(new RenameSquadCommand(Me, 1, "Alpha"));
        sim.Enqueue(new AssignSquadCommand(Me, 1, [b.Id]));
        sim.Step();
        Assert.Equal("Alpha", sim.Squads.ById(1)!.Name);
        Assert.Equal([a.Id, b.Id], sim.Squads.ById(1)!.Members);
        Assert.Empty(sim.Squads.ById(2)!.Members);

        sim.Enqueue(new DisbandSquadCommand(Me, 1));
        sim.Step();
        Assert.Null(sim.Squads.ById(1));
        Assert.Equal(0, a.SquadId);
        Assert.Equal(0, b.SquadId);
    }

    [Fact]
    public void LandedUnitsLeaveTheirSquad()
    {
        var sim = NewSim();
        var tile = Tile(sim, size: 40);
        var a = Scout(sim, tile, 5, 5);
        sim.Enqueue(new CreateSquadCommand(Me, "One", [a.Id]));
        sim.Step();
        sim.Enqueue(new DeployUnitCommand(Me, a.Id));
        Run(sim, 2);
        Assert.Empty(sim.Squads.ById(1)!.Members);
    }

    // --- stances (UC-004) ---

    [Fact]
    public void ADefensiveUnitReturnsToItsAnchorPastTheLeash()
    {
        var sim = NewSim();
        var tile = Tile(sim, size: 60);
        var unit = Scout(sim, tile, 10, 10);
        sim.Enqueue(new SetStanceCommand(Me, [unit.Id], Stance.Defensive));
        sim.Step();
        var (ax, ay) = (unit.AnchorX, unit.AnchorY);
        Assert.True(UnitSim.MayChase(sim, unit, ax + 5L * S, ay));
        Assert.False(UnitSim.MayChase(sim, unit, ax + (UnitSim.LeashCells + 2L) * S, ay));

        // Combat chases something far off; past the leash the unit gives up and goes home.
        UnitSim.Chase(sim, unit, ax + 40L * S, ay);
        Assert.True(unit.Chasing);
        double farthest = 0;
        for (int i = 0; i < 3000; i++)
        {
            sim.Step();
            farthest = Math.Max(farthest, Dist(unit.X, unit.Y, ax, ay));
        }
        Assert.False(unit.Chasing);
        Assert.True(farthest < UnitSim.LeashCells + 6, $"went {farthest:F1} cells out");
        Assert.True(Dist(unit.X, unit.Y, ax, ay) < 2, "didn't come back to its anchor");
    }

    [Fact]
    public void HoldFireNeverFiresOrChases()
    {
        var sim = NewSim();
        var tile = Tile(sim);
        var unit = Scout(sim, tile, 10, 10);
        Assert.True(UnitSim.CanFire(unit));
        sim.Enqueue(new SetStanceCommand(Me, [unit.Id], Stance.HoldFire));
        sim.Step();
        Assert.False(UnitSim.CanFire(unit));
        Assert.False(UnitSim.MayChase(sim, unit, unit.X + S, unit.Y));
    }

    [Fact]
    public void AnEscortFollowsItsCharge()
    {
        var sim = NewSim();
        var tile = Tile(sim, size: 60);
        var lead = Scout(sim, tile, 10, 10);
        var escort = Scout(sim, tile, 10, 14);
        sim.Enqueue(new SetStanceCommand(Me, [escort.Id], Stance.Escort, lead.Id));
        sim.Enqueue(new MoveUnitsCommand(Me, [lead.Id], tile.Id, 45, 45));
        sim.Step();
        Assert.Equal(Stance.Escort, escort.Stance);
        Assert.True(RunUntil(sim, () => !lead.Moving, 4000));
        Run(sim, 1500);
        Assert.True(Dist(escort.X, escort.Y, lead.X, lead.Y) < 10, $"escort {Dist(escort.X, escort.Y, lead.X, lead.Y):F1} cells away");
    }

    // --- factory rally (UC-006) ---

    [Fact]
    public void ANewUnitGoesToItsRallyPointAndJoinsItsSquad()
    {
        var sim = NewSim();
        var tile = Tile(sim, size: 40);
        var first = Scout(sim, tile, 5, 5);
        sim.Enqueue(new CreateSquadCommand(Me, "Reserve", [first.Id]));
        sim.Step();

        LayScout(sim, tile, 10, 20);
        Assert.Equal(RallyResult.NoSuchSquad, UnitSim.CheckRally(sim, Me, tile.Id, 10, 20, tile.Id, 30, 30, 9));
        Assert.Equal(RallyResult.NotOwner, UnitSim.CheckRally(sim, Them, tile.Id, 10, 20, -1, 0, 0, 0));
        sim.Enqueue(new SetRallyCommand(Me, tile.Id, 10, 20, tile.Id, 30, 30, 1));
        sim.Step();
        Assert.Single(sim.Squads.Rallies);

        sim.Enqueue(new ActivateUnitCommand(Me, tile.Id, 10, 20));
        sim.Step();
        var unit = sim.Units[^1];
        Assert.Equal(1, unit.SquadId);
        Assert.Equal(new UnitOrder(OrderKind.Move, tile.Id, 30, 30), unit.Order);
        Assert.Empty(sim.Squads.Rallies); // the unit core was the factory, and it's gone
        Assert.True(RunUntil(sim, () => !unit.Moving, 3000));
        Assert.True(CellsTo(unit, tile, 30, 30) < 1.5);
    }

    // --- abstract travel (UC-009) ---

    [Fact]
    public void TheTravelPlanMatchesTheFlight()
    {
        var sim = NewSim();
        var tiles = Enumerable.Range(0, 3).Select(x => Tile(sim, x, 0, size: 24)).ToArray();
        var unit = Scout(sim, tiles[0], 6, 12);
        var plan = UnitNav.Plan(sim, unit, tiles[2].Id, 18, 12);
        Assert.NotNull(plan);
        Assert.Equal([tiles[1].Id, tiles[2].Id], plan.Tiles);
        Assert.Equal(tiles[2].ToGlobal(18, 12), plan.Waypoints[^1]);

        sim.Enqueue(new MoveUnitsCommand(Me, [unit.Id], tiles[2].Id, 18, 12));
        sim.Step();
        var points = UnitSim.AbstractWaypoints(sim, unit);
        Assert.Equal((unit.DestX, unit.DestY), points[^1]);
        Assert.Equal(unit.MaxSpeed, UnitSim.CruiseSpeed(unit));

        long start = sim.Tick;
        Assert.True(RunUntil(sim, () => !unit.Moving, 6000));
        long took = sim.Tick - start;
        Assert.InRange(plan.Ticks, took * 7 / 10, took * 13 / 10);
    }
}
