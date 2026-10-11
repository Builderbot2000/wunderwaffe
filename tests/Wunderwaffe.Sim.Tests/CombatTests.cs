using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

/// <summary>Combat (CB-001/002/004/005/006, UD-007, FB-010, FB-018, IO-006) on two hand-built 30-cell tiles side by side.</summary>
public class CombatTests
{
    /// <summary>My tile at world x 0-29, theirs at 30-59, both stocked.</summary>
    static (Simulation Sim, TileState Mine, TileState Theirs) Arena()
    {
        var sim = NewSim();
        var mine = Tile(sim, 0, 0, owner: Me);
        var theirs = Tile(sim, 1, 0, owner: Them);
        mine.Inventory.Add("shell", 1000);
        theirs.Inventory.Add("shell", 1000);
        return (sim, mine, theirs);
    }

    static void Put(Simulation sim, TileState tile, int player, string id, int x, int y, int rotation = 0, string? recipe = null) =>
        sim.Enqueue(new PlaceBuildingCommand(player, tile.Id, id, x, y, rotation, recipe));

    /// <summary>
    /// A small gunship lifted off at (x, y): a thruster pushing east, the unit core, a gun and optionally an ammo rack,
    /// in a row from x - 1.
    /// </summary>
    static Unit Gunship(Simulation sim, TileState tile, int player, int x, int y, string gun = "gun_turret", bool rack = false)
    {
        int cells = rack ? 4 : 3;
        for (int i = 0; i < cells; i++)
            sim.Enqueue(new PlaceFrameCommand(player, tile.Id, x - 1 + i, y));
        Put(sim, tile, player, "thruster", x - 1, y);
        Put(sim, tile, player, "unit_core", x, y);
        Put(sim, tile, player, gun, x + 1, y);
        if (rack)
            Put(sim, tile, player, "ammo_rack", x + 2, y);
        sim.Step();
        sim.Enqueue(new ActivateUnitCommand(player, tile.Id, x, y));
        sim.Step();
        var unit = sim.Units[^1];
        Assert.Equal(player, unit.Owner);
        return unit;
    }

    static PlacedBuilding At(TileState tile, int x, int y) => tile.Grid.BuildingAt(x, y)!;

    static int Block(Unit u, string defId) => u.Blocks.ToList().FindIndex(b => b.DefId == defId);

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

    // --- content and queries ---

    [Fact]
    public void WeaponsHaveDpsAndResistancesScaleDamage()
    {
        var content = TestContent.Load();
        Assert.Equal(12 * 60 / 20, Combat.Dps(content.Buildings["gun_turret"]));
        Assert.Equal(0, Combat.Dps(content.Buildings["wall"]));
        // CB-002: base x (100 - resist) / 100.
        Assert.Equal(6, content.Buildings["wall"].DamageTaken(12, "kinetic"));
        Assert.Equal(12, content.Buildings["smelter"].DamageTaken(12, "kinetic"));
    }

    [Fact]
    public void AGunshipBlueprintCountsItsDpsAndBuildsTheGunLast()
    {
        var content = TestContent.Load();
        var bp = new Blueprint(
            [new BlueprintBlock("gun_turret", 0, 0, 0), new BlueprintBlock("unit_core", 1, 0, 0), new BlueprintBlock("thruster", 2, 0, 0)],
            [new BlueprintCell(0, 0), new BlueprintCell(1, 0), new BlueprintCell(2, 0)]);
        Assert.Equal(Combat.Dps(content.Buildings["gun_turret"]), UnitStats.Of(content, bp).Dps);
        // UD-006: frame, then systems, then armament.
        var last = UnitFactory.StepsOf(content, bp, 0, 0)[^1];
        Assert.Equal((BuildStage.Armament, 0), (last.Stage, last.Block));
    }

    [Fact]
    public void AWeaponWithoutAWeaponBlockIsRejected()
    {
        string buildings = """
            [{ "id": "gun", "name": "Gun", "menu": "production", "kind": "weapon", "width": 1, "height": 1, "power": 0, "hp": 10, "cost": {}, "resist": { "acid": 10 } }]
            """;
        var ex = Assert.Throws<Content.ContentException>(() => Content.ContentDb.FromJson("[]", "[]", buildings));
        Assert.Contains(ex.Errors, e => e.Contains("'weapon' block"));
        Assert.Contains(ex.Errors, e => e.Contains("'acid'"));
    }

    // --- turrets (FB-010, CB-001, CB-004) ---

    [Fact]
    public void ATurretShootsAnEnemyUnitApartAndItsBlocksFallAsWrecks()
    {
        var (sim, mine, theirs) = Arena();
        Put(sim, mine, Me, "supply_depot", 20, 14);
        Put(sim, mine, Me, "gun_turret", 25, 15);
        var enemy = Gunship(sim, theirs, Them, 3, 15);
        int enemyId = enemy.Id;

        Assert.True(RunUntil(sim, () => sim.UnitById(enemyId) == null, 3000), "the enemy unit should be destroyed");
        Assert.True(theirs.Grid.WreckCount > 0);
        Assert.NotNull(theirs.Grid.WreckAt(3, 15));
    }

    [Fact]
    public void ATurretWithoutAmmoDoesNotFireAndSaysSo()
    {
        var (sim, mine, theirs) = Arena();
        Put(sim, mine, Me, "gun_turret", 25, 15);
        var enemy = Gunship(sim, theirs, Them, 3, 15);
        Run(sim, 120);

        Assert.Equal(WeaponStatus.NoAmmo, Combat.StatusOf(At(mine, 25, 15)));
        Assert.Empty(sim.Projectiles);
        Assert.All(enemy.Blocks, b => Assert.Equal(TestContent.Load().Buildings[b.DefId].Hp, b.Hp));
    }

    [Fact]
    public void LogisticsLoadATurretsAmmo()
    {
        // Drill -> smelter -> munitions works -> turret, each dumping straight into the next (CB-004: turrets from logistics).
        var (sim, mine, _) = Arena();
        Ore(mine, "iron_ore", 2, 2);
        Put(sim, mine, Me, "drill", 2, 2);
        Put(sim, mine, Me, "smelter", 4, 2, recipe: "smelt_iron");
        Put(sim, mine, Me, "munitions_works", 6, 2);
        Put(sim, mine, Me, "gun_turret", 8, 2);
        Assert.True(RunUntil(sim, () => At(mine, 8, 2).Combat!.Ammo > 0, 1800), "shells should reach the turret");
    }

    [Fact]
    public void ALaserTurretNeedsNoAmmo()
    {
        var (sim, mine, theirs) = Arena();
        Put(sim, mine, Me, "laser_turret", 25, 15);
        var enemy = Gunship(sim, theirs, Them, 3, 15);
        Assert.True(RunUntil(sim, () => sim.Projectiles.Any(p => p.Beam), 60), "it fires beams");
        Run(sim, 60);
        Assert.Equal(WeaponStatus.Firing, Combat.StatusOf(At(mine, 25, 15)));
        Assert.True(enemy.Blocks[0].Hp < 120, "and they hurt");
    }

    [Fact]
    public void AShieldStopsShotsFromOutsideIt()
    {
        var (sim, mine, theirs) = Arena();
        Put(sim, mine, Me, "supply_depot", 20, 14);
        Put(sim, mine, Me, "gun_turret", 27, 15);
        Put(sim, theirs, Them, "smelter", 4, 15, recipe: "smelt_iron");
        Put(sim, theirs, Them, "shield_generator", 8, 15);
        Run(sim, 600);

        Assert.Equal(WeaponStatus.Firing, Combat.StatusOf(At(mine, 27, 15)));
        Assert.Equal(250, At(theirs, 4, 15).Hp);
        Assert.True(At(theirs, 8, 15).Combat!.Charge < 400, "the shield took the hits");
    }

    // --- per-block damage (UD-007) ---

    [Fact]
    public void LosingBlocksDisablesWhatTheyDid()
    {
        var (sim, mine, _) = Arena();
        var unit = Gunship(sim, mine, Me, 10, 10);
        int speed = unit.MaxSpeed;
        Assert.True(speed > 0);
        Assert.Equal(10, Combat.WeaponRange(sim, unit));

        Combat.DamageBlock(sim, unit, Block(unit, "thruster"), 10_000, "kinetic");
        Combat.DamageBlock(sim, unit, Block(unit, "gun_turret"), 10_000, "kinetic");
        Assert.Equal(0, unit.MaxSpeed);
        Assert.Equal(0, Combat.WeaponRange(sim, unit));
        Assert.Null(Combat.UnitWeaponStatus(sim, unit));
        // Dead blocks stay attached, so the unit doesn't split; it still lives on its core.
        Assert.Equal(3, unit.Blocks.Count);
        Assert.NotNull(sim.UnitById(unit.Id));
    }

    // --- wrecks (CB-006) ---

    [Fact]
    public void ADestroyedBuildingLeavesAWreckThatBlocksBuildingAndSalvagesForAShare()
    {
        var (sim, mine, _) = Arena();
        Put(sim, mine, Me, "smelter", 5, 5, recipe: "smelt_iron");
        sim.Step();
        var smelter = At(mine, 5, 5);
        smelter.Machine!.Input["iron_ore"] = 3;

        long ore = Items(mine, "iron_ore");
        Assert.True(Combat.DamageBuilding(sim, mine, smelter, 10_000, "kinetic"));
        Assert.Null(mine.Grid.BuildingAt(5, 5));
        Assert.NotNull(mine.Grid.WreckAt(6, 6));
        Assert.Equal(ore, Items(mine, "iron_ore")); // the items it held are lost
        Assert.Equal(PlaceResult.Wreck, Placement.Check(sim, mine, Me, "conveyor", 6, 6, 0, null, out _));

        long copper = Items(mine, "copper_ore");
        sim.Enqueue(new RemoveBuildingCommand(Me, mine.Id, 6, 5));
        sim.Step();
        Assert.Null(mine.Grid.WreckAt(6, 6));
        Assert.Equal(copper + 20 * Balance.WreckRefundPercent / 100, Items(mine, "copper_ore"));
        Assert.Equal(PlaceResult.Ok, Placement.Check(sim, mine, Me, "conveyor", 6, 6, 0, null, out _));
    }

    [Fact]
    public void TerritoryDestructionLeavesAWreckToo()
    {
        // Circle wipes and debug destroy go through Territory.DestroyBuilding, which forwards to combat (CB-006).
        var (sim, mine, _) = Arena();
        Put(sim, mine, Me, "wall", 5, 5);
        sim.Step();
        sim.Enqueue(new DebugDestroyCommand(Me, mine.Id, 5, 5));
        sim.Step();
        Assert.Null(mine.Grid.BuildingAt(5, 5));
        Assert.Equal("wall", mine.Grid.WreckAt(5, 5)?.DefId);
    }

    [Fact]
    public void UnitsFlyOverWrecks()
    {
        var (sim, mine, _) = Arena();
        for (int y = 0; y < 30; y++)
            mine.Grid.AddWreck("wall", Me, 15, y, 1, 1, 0);
        var unit = Gunship(sim, mine, Me, 5, 10);
        sim.Enqueue(new MoveUnitsCommand(Me, [unit.Id], mine.Id, 25, 10));
        Assert.True(RunUntil(sim, () => !unit.Moving, 2000));
        Assert.True(unit.X > 20 * Unit.CellScale);
    }

    // --- orders ---

    [Fact]
    public void AnAttackOrderChasesABuildingAndDestroysIt()
    {
        var (sim, mine, theirs) = Arena();
        Put(sim, mine, Me, "supply_depot", 10, 14);
        var unit = Gunship(sim, mine, Me, 12, 17, rack: true);
        Put(sim, theirs, Them, "smelter", 12, 16, recipe: "smelt_iron");
        Run(sim, 61); // load up inside the depot's radius
        Assert.True(unit.Cargo.Get("shell") > 0);

        sim.Enqueue(new AttackBuildingCommand(Me, [unit.Id], theirs.Id, 12, 16));
        Assert.True(RunUntil(sim, () => theirs.Grid.BuildingAt(12, 16) == null, 3000), "the smelter should fall");
        Assert.NotNull(theirs.Grid.WreckAt(12, 16));
    }

    [Fact]
    public void AnAttackOnAUnitEndsWhenItDies()
    {
        var (sim, mine, theirs) = Arena();
        Put(sim, mine, Me, "supply_depot", 10, 14);
        var unit = Gunship(sim, mine, Me, 12, 17, rack: true);
        var enemy = Gunship(sim, theirs, Them, 15, 5);
        Run(sim, 61);

        sim.Enqueue(new AttackCommand(Me, [unit.Id], enemy.Id));
        Assert.True(RunUntil(sim, () => sim.UnitById(enemy.Id) == null, 4000), "the enemy unit should die");
        Run(sim, 20);
        Assert.Null(unit.Order);
    }

    [Fact]
    public void AMoveOrderCancelsAnAttack()
    {
        var (sim, mine, theirs) = Arena();
        var unit = Gunship(sim, mine, Me, 12, 17);
        Put(sim, theirs, Them, "smelter", 12, 16, recipe: "smelt_iron");
        sim.Step();
        sim.Enqueue(new AttackBuildingCommand(Me, [unit.Id], theirs.Id, 12, 16));
        sim.Step();
        Assert.Equal(OrderKind.Attack, unit.Order?.Kind);
        // One shared order per unit: the move replaces the attack.
        sim.Enqueue(new MoveUnitsCommand(Me, [unit.Id], mine.Id, 5, 5));
        sim.Step();
        Assert.Equal(OrderKind.Move, unit.Order?.Kind);
    }

    [Fact]
    public void AttackMoveStopsToFightThenCarriesOn()
    {
        var (sim, mine, theirs) = Arena();
        Put(sim, mine, Me, "supply_depot", 4, 14);
        var unit = Gunship(sim, mine, Me, 4, 17, rack: true);
        Put(sim, theirs, Them, "conveyor", 2, 2);
        Run(sim, 61);

        sim.Enqueue(new AttackMoveCommand(Me, [unit.Id], theirs.Id, 10, 4));
        Assert.True(RunUntil(sim, () => theirs.Grid.BuildingAt(2, 2) == null, 3000), "it fights what it passes");
        Assert.True(RunUntil(sim, () => unit.Order == null, 3000), "then finishes the move");
        Assert.Equal(theirs.Id, unit.TileId);
    }

    // --- resource radius (FB-018) ---

    [Fact]
    public void UnitsResupplyInsideAFriendlyRadiusOnly()
    {
        var (sim, mine, _) = Arena();
        var near = Gunship(sim, mine, Me, 5, 5, rack: true);
        var far = Gunship(sim, mine, Me, 25, 25, rack: true);
        Put(sim, mine, Me, "supply_depot", 5, 8);
        Run(sim, 61);

        int gun = Block(near, "gun_turret");
        Assert.Equal(20, near.BlockCombat[gun]!.Ammo);
        Assert.Equal(40, near.Cargo.Get("shell"));
        Assert.Equal(0, far.BlockCombat[gun]!.Ammo);
        Assert.Same(mine.Inventory, ResourceRadius.SupplierFor(sim, near));
        Assert.Null(ResourceRadius.SupplierFor(sim, far));
        Assert.True(ResourceRadius.Covers(sim, Me, near.X, near.Y));
    }

    [Fact]
    public void AUnitOutOfAmmoStopsFiringAndWarns()
    {
        var (sim, mine, theirs) = Arena();
        var unit = Gunship(sim, mine, Me, 25, 15);
        Put(sim, theirs, Them, "smelter", 2, 15, recipe: "smelt_iron");
        Run(sim, 60);
        Assert.Equal(WeaponStatus.NoAmmo, Combat.UnitWeaponStatus(sim, unit));
        Assert.Equal(250, At(theirs, 2, 15).Hp);

        // Back inside a friendly radius it refills and opens fire (CB-004).
        Put(sim, mine, Me, "supply_depot", 20, 14);
        Assert.True(RunUntil(sim, () => At(theirs, 2, 15).Hp < 250, 120));
        Assert.Equal(WeaponStatus.Firing, Combat.UnitWeaponStatus(sim, unit));
    }

    [Fact]
    public void AUnitWithStorageSuppliesItsFriends()
    {
        var (sim, mine, _) = Arena();
        // A supply depot riding on a unit: core, thruster (UD-003 needs locomotion), depot (2x2) on frame.
        for (int y = 20; y < 22; y++)
            for (int x = 20; x < 23; x++)
                sim.Enqueue(new PlaceFrameCommand(Me, mine.Id, x, y));
        Put(sim, mine, Me, "unit_core", 20, 20);
        Put(sim, mine, Me, "thruster", 20, 21);
        Put(sim, mine, Me, "supply_depot", 21, 20);
        sim.Step();
        sim.Enqueue(new ActivateUnitCommand(Me, mine.Id, 20, 20));
        sim.Step();
        var carrier = sim.Units[^1];
        carrier.Cargo.Add("shell", 30);
        var gunship = Gunship(sim, mine, Me, 24, 25);
        Run(sim, 61);

        Assert.Same(carrier.Cargo, ResourceRadius.SupplierFor(sim, gunship));
        Assert.Equal(20, gunship.BlockCombat[Block(gunship, "gun_turret")]!.Ammo);
        Assert.Equal(10, carrier.Cargo.Get("shell"));
    }

    // --- repair (CB-005) ---

    [Fact]
    public void ARepairBayHealsAndRebuildsForACost()
    {
        var (sim, mine, _) = Arena();
        Put(sim, mine, Me, "smelter", 5, 5, recipe: "smelt_iron");
        Put(sim, mine, Me, "repair_bay", 8, 5);
        var unit = Gunship(sim, mine, Me, 10, 9);
        var smelter = At(mine, 5, 5);
        smelter.Hp = 50;
        int thruster = Block(unit, "thruster");
        Combat.DamageBlock(sim, unit, thruster, 10_000, "kinetic");
        Assert.Equal(0, unit.MaxSpeed);

        long plates = Items(mine, "iron_plate");
        Run(sim, 1200);
        Assert.Equal(250, smelter.Hp);
        Assert.Equal(120, unit.Blocks[thruster].Hp);
        Assert.True(unit.MaxSpeed > 0, "the rebuilt engine works again");
        Assert.True(Items(mine, "iron_plate") < plates, "repair costs plates");
    }

    // --- interdiction (IO-006) ---

    [Fact]
    public void DestroyingAPortDisruptsItsLink()
    {
        var (sim, mine, theirs) = Arena();
        int x = Enumerable.Range(0, 30).First(i => mine.Grid.ZoneAt(29, i) == theirs.Id);
        Put(sim, mine, Me, "export_port", 29, x);
        sim.Step();
        Assert.False(Combat.LinkDisrupted(sim, mine, theirs));
        Combat.DestroyBuilding(sim, mine, At(mine, 29, x));
        Assert.True(Combat.LinkDisrupted(sim, mine, theirs));
        Assert.True(Combat.LinkDisrupted(sim, theirs, mine));

        sim.Enqueue(new RemoveBuildingCommand(Me, mine.Id, 29, x));
        sim.Step();
        Assert.False(Combat.LinkDisrupted(sim, mine, theirs));
    }

    // --- determinism ---

    [Fact]
    public void ABattleIsDeterministic()
    {
        ulong Battle()
        {
            var (sim, mine, theirs) = Arena();
            Put(sim, mine, Me, "supply_depot", 20, 14);
            Put(sim, mine, Me, "gun_turret", 25, 15);
            Put(sim, mine, Me, "laser_turret", 25, 17);
            Put(sim, theirs, Them, "supply_depot", 5, 14);
            Put(sim, theirs, Them, "gun_turret", 4, 16);
            var a = Gunship(sim, theirs, Them, 3, 20, rack: true);
            var b = Gunship(sim, mine, Me, 20, 20, rack: true);
            sim.Enqueue(new AttackCommand(Me, [b.Id], a.Id));
            Run(sim, 1200);
            return sim.StateHash();
        }
        Assert.Equal(Battle(), Battle());
    }
}
