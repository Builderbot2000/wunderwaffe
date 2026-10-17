using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

/// <summary>UC-004 stances and CB-007 targeting modes, on one unowned 64-cell tile both players have funds on.</summary>
public class StanceTests
{
    const int S = Unit.CellScale;
    const int Range = 10; // gun_turret

    static (Simulation Sim, TileState Tile) Field()
    {
        var sim = NewSim();
        var tile = Tile(sim, size: 64, owner: TileState.Unowned);
        Bank(sim, tile, Me, 63, 63);
        Bank(sim, tile, Them, 0, 63);
        return (sim, tile);
    }

    /// <summary>A gunship with its core at (x, y): thruster, core, the given blocks, thruster. No ammo unless stocked.</summary>
    static Unit Ship(Simulation sim, TileState tile, int player, int x, int y, params string[] blocks)
    {
        LayUnit(sim, tile, x, y, player, [.. blocks, "thruster"]);
        return LiftOff(sim, tile, x, y, player);
    }

    static Unit Gunship(Simulation sim, TileState tile, int player, int x, int y) => Ship(sim, tile, player, x, y, "gun_turret");

    static void SetStance(Simulation sim, Unit u, Stance stance)
    {
        sim.Enqueue(new SetStanceCommand(u.Owner, [u.Id], stance));
        sim.Step();
    }

    static double Dist(Unit a, long x, long y) => Math.Sqrt(Math.Pow(a.X - x, 2) + Math.Pow(a.Y - y, 2)) / S;

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

    /// <summary>Shells in my funds and a supply depot of mine at (x, y), whose radius loads my units' guns (FB-018).</summary>
    static void Depot(Simulation sim, TileState tile, int x, int y)
    {
        Fund(sim, tile, "shell", 1000, Me);
        sim.Enqueue(new PlaceBuildingCommand(Me, tile.Id, "supply_depot", x, y));
        sim.Step();
    }

    static int Gun(Unit u) => u.Blocks.ToList().FindIndex(b => b.DefId == "gun_turret");

    // --- stances (UC-004) ---

    [Fact]
    public void TravellingAggressiveAndDefensiveUnitsStopToFightPassiveAndHoldFireKeepCourse()
    {
        var (sim, tile) = Field();
        var u = Gunship(sim, tile, Me, 10, 20);
        foreach (var (stance, stops) in new[] { (Stance.Aggressive, true), (Stance.Defensive, true), (Stance.Passive, false), (Stance.HoldFire, false) })
        {
            SetStance(sim, u, stance);
            Assert.Equal(stops, UnitSim.StopsToFight(u));
        }
    }

    [Fact]
    public void AnIdleAggressiveUnitSeeksTheNearestEnemyAnywhereOnItsTile()
    {
        var (sim, tile) = Field();
        var me = Gunship(sim, tile, Me, 5, 10);
        var far = Ship(sim, tile, Them, 50, 40);
        var farther = Ship(sim, tile, Them, 55, 55);
        Assert.Equal(Stance.Aggressive, me.Stance);
        Assert.True(Dist(me, far.X, far.Y) > 3 * Range);

        Assert.True(RunUntil(sim, () => Dist(me, far.X, far.Y) <= Range, 4000), "it should close in on the nearest enemy");
        Assert.True(me.Chasing);
        Run(sim, 200);
        Assert.True(Dist(me, far.X, far.Y) <= Range, "and stay in range");
        Assert.True(Dist(me, farther.X, farther.Y) > Dist(me, far.X, far.Y));
    }

    [Fact]
    public void AnAggressiveUnitOnAMoveStopsToFightThenCarriesOn()
    {
        var r = Trip(Stance.Aggressive, enemy: true);
        Assert.True(r.Stopped, "it should stop to fight");
        Assert.True(r.Chased);
        Assert.True(r.Arrived, $"then finish its move once the enemy is dead: {r}");
    }

    [Fact]
    public void APassiveUnitKeepsItsCourseAndSpeedPastAnEnemy()
    {
        var alone = Trip(Stance.Passive, enemy: false);
        var r = Trip(Stance.Passive, enemy: true);
        Assert.True(r.Arrived);
        Assert.True(r.Fired, "it shoots what comes into range");
        Assert.False(r.Chased);
        Assert.False(r.Stopped);
        Assert.True(r.Drift < 1.5, $"drifted {r.Drift:F1} cells off its course");
        Assert.InRange(r.Ticks, alone.Ticks - 30, alone.Ticks + 30);
    }

    /// <summary>
    /// A gunship with shells and the given stance moves from (5, 30) to (58, 30) of a fresh field, optionally past a
    /// bare enemy scout at (30, 36): ticks taken, most cells off its course line, whether it fired, chased, or stood
    /// still on the way, and whether it got there.
    /// </summary>
    static (long Ticks, double Drift, bool Fired, bool Chased, bool Stopped, bool Arrived) Trip(Stance stance, bool enemy)
    {
        var (sim, tile) = Field();
        var me = Gunship(sim, tile, Me, 5, 30);
        Depot(sim, tile, 28, 24);
        SetStance(sim, me, stance);
        if (enemy)
            Ship(sim, tile, Them, 30, 36);
        sim.Enqueue(new MoveUnitsCommand(Me, [me.Id], tile.Id, 58, 30));
        sim.Step();
        long start = sim.Tick, line = (tile.OriginY + 30L) * S + S / 2;
        double drift = 0;
        bool fired = false, chased = false, stopped = false;
        while (me.Order != null && sim.Tick - start < 8000)
        {
            sim.Step();
            drift = Math.Max(drift, Math.Abs(me.Y - line) / (double)S);
            fired |= me.BlockCombat[Gun(me)]!.Status == WeaponStatus.Firing;
            chased |= me.Chasing;
            stopped |= me.Order != null && me.Route == null;
        }
        bool arrived = me.Order == null && Dist(me, (tile.OriginX + 58L) * S + S / 2, line) < 2;
        return (sim.Tick - start, drift, fired, chased, stopped, arrived);
    }

    [Fact]
    public void ADefensiveUnitChasesNoFurtherThanItsLeashAndGoesBack()
    {
        var (sim, tile) = Field();
        var me = Gunship(sim, tile, Me, 10, 30);
        SetStance(sim, me, Stance.Defensive);
        var (ax, ay) = (me.AnchorX, me.AnchorY);
        // Out of weapon range, but in reach from within the leash.
        var enemy = Ship(sim, tile, Them, 10 + Range + UnitSim.LeashCells - 4, 30);
        SetStance(sim, enemy, Stance.HoldFire);
        Assert.True(RunUntil(sim, () => me.Chasing, 100));
        Assert.True(RunUntil(sim, () => Dist(me, enemy.X, enemy.Y) <= Range, 1000), "it should close in");

        // The enemy runs off across the tile; the defender follows only to its leash.
        sim.Enqueue(new MoveUnitsCommand(Them, [enemy.Id], tile.Id, 58, 30));
        double farthest = 0;
        for (int i = 0; i < 3000; i++)
        {
            sim.Step();
            farthest = Math.Max(farthest, Dist(me, ax, ay));
        }
        Assert.True(farthest > UnitSim.LeashCells / 2, $"it should chase a little ({farthest:F1} cells)");
        Assert.True(farthest < UnitSim.LeashCells + 1.5, $"went {farthest:F1} cells out");
        Assert.False(me.Chasing);
        Assert.True(Dist(me, ax, ay) < 2, "it should come back to its anchor");
    }

    [Fact]
    public void AHoldFireUnitNeitherFiresNorMovesUntilOrdered()
    {
        var (sim, tile) = Field();
        var me = Gunship(sim, tile, Me, 10, 30);
        Depot(sim, tile, 10, 26);
        Run(sim, 61);
        SetStance(sim, me, Stance.HoldFire);
        var enemy = Ship(sim, tile, Them, 18, 30, "gun_turret");
        SetStance(sim, enemy, Stance.Passive);
        var (x, y) = (me.X, me.Y);
        Run(sim, 400);
        Assert.False(UnitSim.CanFire(me));
        Assert.Equal(WeaponStatus.HoldFire, me.BlockCombat[Gun(me)]!.Status);
        Assert.True(Dist(me, x, y) < 1, $"it never moves on its own: {Dist(me, x, y):F2} {me.Route != null} {me.Order}");
        Assert.False(me.Chasing);

        // An order is carried out whatever the stance.
        sim.Enqueue(new AttackCommand(Me, [me.Id], enemy.Id));
        Assert.True(RunUntil(sim, () => me.BlockCombat[Gun(me)]!.Status == WeaponStatus.Firing, 200));
    }

    // --- targeting modes (CB-007) ---

    /// <summary>
    /// My gunship at (10, 30) and three enemies in its range: a bare scout nearest, a two-gun ship, and a farther
    /// ship with the most blocks but no weapon. All hold fire.
    /// </summary>
    static (Simulation Sim, TileState Tile, Unit Me, Unit Scout, Unit Guns, Unit Bulk) Targets()
    {
        var (sim, tile) = Field();
        var me = Gunship(sim, tile, Me, 10, 30);
        var scout = Ship(sim, tile, Them, 15, 30);
        var guns = Ship(sim, tile, Them, 14, 34, "gun_turret", "gun_turret");
        var bulk = Ship(sim, tile, Them, 14, 24, "ammo_rack", "ammo_rack", "ammo_rack", "ammo_rack");
        foreach (var u in new[] { me, scout, guns, bulk })
            SetStance(sim, u, Stance.HoldFire);
        SetStance(sim, me, Stance.Passive);
        return (sim, tile, me, scout, guns, bulk);
    }

    static int TargetOf(Unit u) => u.BlockCombat[Gun(u)]!.Target.Unit;

    [Fact]
    public void WeaponsPickTheirTargetByTheUnitsMode()
    {
        var (sim, _, me, scout, guns, bulk) = Targets();
        Run(sim, 20);
        Assert.Equal(TargetMode.Nearest, me.Targeting);
        Assert.Equal(scout.Id, TargetOf(me));

        sim.Enqueue(new SetTargetingCommand(Me, [me.Id], TargetMode.MostWeapons));
        Run(sim, 20);
        Assert.Equal(TargetMode.MostWeapons, me.Targeting);
        Assert.Equal(guns.Id, TargetOf(me));

        sim.Enqueue(new SetTargetingCommand(Me, [me.Id], TargetMode.MostBlocks));
        Run(sim, 20);
        Assert.Equal(bulk.Id, TargetOf(me));

        // An attack order overrides the mode.
        sim.Enqueue(new AttackCommand(Me, [me.Id], scout.Id));
        Run(sim, 2);
        Assert.Equal(scout.Id, TargetOf(me));
    }

    [Fact]
    public void ASquadFocusesFireOnTheTargetItsModePicks()
    {
        var (sim, tile, me, _, guns, bulk) = Targets();
        var wing = Gunship(sim, tile, Me, 10, 36);
        SetStance(sim, wing, Stance.Passive);
        sim.Enqueue(new CreateSquadCommand(Me, "Pair", [me.Id, wing.Id]));
        sim.Step();
        var squad = sim.Squads.ById(me.SquadId)!;
        sim.Enqueue(new SetSquadTargetingCommand(Me, squad.Id, TargetMode.MostWeapons));
        Run(sim, 20);
        Assert.Equal(TargetMode.MostWeapons, squad.Targeting);
        Assert.Equal(TargetMode.MostWeapons, wing.Targeting);
        Assert.Equal(guns.Id, squad.FocusId);
        Assert.Equal(guns.Id, TargetOf(me));
        Assert.Equal(guns.Id, TargetOf(wing));

        sim.Enqueue(new SetSquadTargetingCommand(Me, squad.Id, TargetMode.MostBlocks));
        Run(sim, 20);
        Assert.Equal(bulk.Id, squad.FocusId);
    }
}
