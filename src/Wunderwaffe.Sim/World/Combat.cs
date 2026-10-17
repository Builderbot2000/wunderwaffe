using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using static Wunderwaffe.Sim.Core.IntMath;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// Real-time combat (CB-001), tick phase 4: shields recharge, units and turrets resupply (FB-018), attack orders steer,
/// weapons pick targets and fire, repair blocks heal (CB-005), then shots fly and hit. Everyone sees everything (OQ-29).
/// Damage is per block (UD-007): a building is one block; a unit's blocks each have their own hit points. A destroyed
/// building leaves a wreck (CB-006) and the items it held are lost. A destroyed unit block stays attached as an inert
/// wreck, so a unit never splits in two; destroying its unit core destroys the whole unit, and its blocks fall as
/// wrecks where it was. Integer maths only, visiting tiles, buildings, units and shots in id order (NF-004).
/// </summary>
public static class Combat
{
    const int S = Unit.CellScale;

    /// <summary>Ticks between target searches for a weapon whose target is still valid; staggered by id.</summary>
    const int RetargetTicks = 15;

    /// <summary>Ticks between attack-order updates for a unit; staggered by id.</summary>
    const int OrderTicks = 10;

    /// <summary>Ticks between repair passes.</summary>
    const int RepairTicks = 30;

    /// <summary>Ticks between two under-attack alerts for the same tile.</summary>
    const int AlertTicks = 5 * Simulation.TicksPerSecond;

    /// <summary>A projectile is tested for hits every this many sub-cells of its flight, so it can't skip a cell.</summary>
    const long SampleStep = S / 3;

    /// <summary>A unit attacking stops once its target is within this share of its weapon range, in percent.</summary>
    const int EngagePercent = 85;

    // ---- Queries (public and read-only, for the UI, AI and derived stats) ----

    /// <summary>Damage per second of one weapon block before resistances; 0 for anything else (for derived unit stats).</summary>
    public static int Dps(BuildingDef def) =>
        def.Weapon is { } w ? w.Damage * Simulation.TicksPerSecond / w.Cooldown : 0;

    /// <summary>A weapon's state on a tile, or null for buildings that don't fire.</summary>
    public static WeaponStatus? StatusOf(PlacedBuilding b) => b.Kind == BuildingKind.Weapon ? b.Combat!.Status : null;

    /// <summary>
    /// How the unit's live weapons are doing, for its warning (CB-004): <see cref="WeaponStatus.Firing"/> if any fires,
    /// else <see cref="WeaponStatus.NoAmmo"/> if any has a target but no ammo, else the first weapon's status. Null for
    /// a unit with no live weapons.
    /// </summary>
    public static WeaponStatus? UnitWeaponStatus(Simulation sim, Unit u)
    {
        WeaponStatus? first = null;
        bool dry = false;
        for (int i = 0; i < u.Blocks.Count; i++)
        {
            if (u.Blocks[i].Hp <= 0 || u.BlockCombat[i] is not { } c || sim.Content.Buildings[u.Blocks[i].DefId].Kind != BuildingKind.Weapon)
                continue;
            if (c.Status == WeaponStatus.Firing)
                return WeaponStatus.Firing;
            dry |= c.Status == WeaponStatus.NoAmmo;
            first ??= c.Status;
        }
        return dry ? WeaponStatus.NoAmmo : first;
    }

    /// <summary>Longest range of the unit's live weapons, in cells; 0 if it has none (UD-007: lost weapons can't fire).</summary>
    public static int WeaponRange(Simulation sim, Unit u)
    {
        int range = 0;
        foreach (var blk in u.Blocks)
        {
            if (blk.Hp > 0 && sim.Content.Buildings[blk.DefId].Weapon is { } w)
                range = Math.Max(range, w.Range);
        }
        return range;
    }

    /// <summary>
    /// IO-006: the link between two tiles is disrupted while a destroyed port's wreck sits in either side's zone for
    /// the other. Clearing the wreck and building a new port restores it.
    /// </summary>
    public static bool LinkDisrupted(Simulation sim, TileState a, TileState b) => PortWreck(sim, a, b.Id) || PortWreck(sim, b, a.Id);

    static bool PortWreck(Simulation sim, TileState tile, int partner)
    {
        foreach (var w in tile.Grid.Wrecks)
        {
            if (sim.Content.Buildings[w.DefId].Kind is BuildingKind.ExportPort or BuildingKind.ImportPort
                && tile.Grid.ZoneAt(w.X, w.Y) == partner)
                return true;
        }
        return false;
    }

    /// <summary>A unit block's centre in world sub-cells, given the unit's position and heading.</summary>
    public static (long X, long Y) BlockWorld(Unit u, UnitBlock blk)
    {
        var c0 = u.Cells[0];
        long lx = c0.OffX + (long)(blk.GX - c0.GX) * S + (long)(blk.Width - 1) * S / 2;
        long ly = c0.OffY + (long)(blk.GY - c0.GY) * S + (long)(blk.Height - 1) * S / 2;
        var (rx, ry) = Trig.Rotate(lx, ly, u.Angle);
        return (u.X + rx, u.Y + ry);
    }

    // ---- Damage API (internal: the sim changes only through commands and its own step) ----

    /// <summary>
    /// Damages a building after its resistances (CB-002). Returns true if that destroyed it.
    /// <paramref name="attacker"/> is the shooting unit, or 0, for the alert.
    /// </summary>
    internal static bool DamageBuilding(Simulation sim, TileState tile, PlacedBuilding b, int amount, string type, int attacker = 0)
    {
        int dmg = sim.Content.Buildings[b.DefId].DamageTaken(amount, type);
        if (dmg <= 0)
            return false;
        Attacked(sim, b.Owner, tile.Id, attacker);
        b.Hp -= dmg;
        if (b.Hp > 0)
            return false;
        DestroyBuilding(sim, tile, b);
        return true;
    }

    /// <summary>
    /// Removes a building at once, leaving a wreck in its cells (CB-006). The items it held are lost. A tile core goes
    /// the same way; what that means for the tile is territory's and victory's business.
    /// </summary>
    internal static void DestroyBuilding(Simulation sim, TileState tile, PlacedBuilding b)
    {
        b.Hp = 0;
        tile.Grid.Remove(b);
        tile.Grid.AddWreck(b.DefId, b.Owner, b.X, b.Y, b.Width, b.Height, b.Rotation);
    }

    /// <summary>
    /// Damages one block of a unit (UD-007). A destroyed block stops working and the unit's derived stats are
    /// recomputed; destroying the unit core destroys the unit at the end of this combat step. True if the block died.
    /// </summary>
    internal static bool DamageBlock(Simulation sim, Unit u, int index, int amount, string type, int attacker = 0)
    {
        var blk = u.Blocks[index];
        if (blk.Hp <= 0)
            return false;
        int dmg = sim.Content.Buildings[blk.DefId].DamageTaken(amount, type);
        if (dmg <= 0)
            return false;
        Attacked(sim, u.Owner, u.TileId, attacker);
        blk.Hp = Math.Max(0, blk.Hp - dmg);
        if (blk.Hp > 0)
            return false;
        if (u.BlockCombat[index] is { } c)
        {
            c.Status = WeaponStatus.NoTarget;
            c.Target = Target.None;
        }
        u.RecomputeStats(sim.Content.Buildings);
        return true;
    }

    /// <summary>
    /// Removes a unit. Each of its blocks, dead or alive, falls as a wreck where it lies, turned to the nearest quarter
    /// turn, if its cells are free buildable ground of the tile below; the rest are lost, as is its cargo.
    /// </summary>
    internal static void DestroyUnit(Simulation sim, Unit u)
    {
        var tile = sim.Tiles[u.TileId];
        var grid = tile.Grid;
        int q = ((u.Angle + Trig.Quarter / 2) / Trig.Quarter) & 3;
        var (rx, ry) = Trig.Rotate(u.CoreOffX, u.CoreOffY, u.Angle);
        int coreX = (int)FloorDiv(u.X + rx, S) - tile.OriginX, coreY = (int)FloorDiv(u.Y + ry, S) - tile.OriginY;
        foreach (var b in u.Blocks)
        {
            var (ax, ay) = UnitAssembly.Turn(b.GX - u.CoreGX, b.GY - u.CoreGY, q);
            var (bx, by) = UnitAssembly.Turn(b.GX + b.Width - 1 - u.CoreGX, b.GY + b.Height - 1 - u.CoreGY, q);
            int x = coreX + Math.Min(ax, bx), y = coreY + Math.Min(ay, by), w = Math.Abs(ax - bx) + 1, h = Math.Abs(ay - by) + 1;
            if (grid.InBounds(x, y, w, h) && grid.Buildable(x, y, w, h) && grid.IsFree(x, y, w, h) && grid.WreckFree(x, y, w, h))
                grid.AddWreck(b.DefId, u.Owner, x, y, w, h, (b.Rotation + q) & 3);
        }
        sim.RemoveUnit(u);
    }

    /// <summary>
    /// Deconstructs the wreck on a cell (CB-006), like removing a building: anyone with presence on the tile may, and
    /// <see cref="Balance.WreckRefundPercent"/> of its cost goes to the player's nearest core there, or is lost.
    /// </summary>
    internal static bool ClearWreck(Simulation sim, TileState tile, int player, int x, int y)
    {
        if (!Territory.HasPresence(sim, player, tile) || tile.Grid.WreckAt(x, y) is not { } w)
            return false;
        tile.Grid.RemoveWreck(w);
        Funds.Refund(tile, player, w.X, w.Y, w.Width, w.Height, sim.Content.Buildings[w.DefId].Cost, Balance.WreckRefundPercent);
        return true;
    }

    /// <summary>
    /// Something of <paramref name="victim"/>'s on a tile took damage: raises the under-attack alert (UI-003), at
    /// most once per tile and victim every <see cref="AlertTicks"/>. <paramref name="attacker"/> is the shooting unit, or 0.
    /// </summary>
    static void Attacked(Simulation sim, int victim, int tileId, int attacker)
    {
        if (victim >= 0 && sim.ThrottleAlert(tileId, victim, AlertTicks))
            sim.Raise(GameEventKind.TileUnderAttack, victim, tileId, unitId: attacker > 0 ? attacker : -1);
    }

    // ---- The step ----

    internal static void Step(Simulation sim)
    {
        sim.RemoveSpentProjectiles();
        var dead = new List<Unit>();

        Recharge(sim);
        ResourceRadius.Step(sim);
        foreach (var u in sim.Units)
            UpdateOrder(sim, u);

        var targets = new TargetIndex(sim);
        FocusSquads(sim, targets);
        foreach (var tile in sim.Tiles)
        {
            if (tile.Grid.CombatBuildings.Count == 0)
                continue;
            foreach (var b in tile.Grid.CombatBuildings)
            {
                if (b.Kind == BuildingKind.Weapon)
                {
                    var (x, y) = ResourceRadius.Centre(tile, b);
                    Fire(sim, targets, b.Combat!, sim.Content.Buildings[b.DefId], b.Owner, x, y, b.Id, null, Target.None, CombatPower.Has(tile, b), TargetMode.Nearest);
                }
            }
        }
        foreach (var u in sim.Units)
            FireUnit(sim, targets, u);

        if (sim.Tick % RepairTicks == 0)
            Repair(sim);

        Fly(sim, targets, dead);
        foreach (var u in dead)
            DestroyUnit(sim, u);
    }

    /// <summary>Shields regain <see cref="ShieldDef.Regen"/> charge a second, spread evenly over the ticks.</summary>
    static void Recharge(Simulation sim)
    {
        long t = sim.Tick % Simulation.TicksPerSecond;
        int Gain(ShieldDef s) => (int)((t + 1) * s.Regen / Simulation.TicksPerSecond - t * s.Regen / Simulation.TicksPerSecond);
        foreach (var tile in sim.Tiles)
        {
            foreach (var b in tile.Grid.CombatBuildings)
            {
                if (sim.Content.Buildings[b.DefId].Shield is { } s && CombatPower.Has(tile, b))
                    b.Combat!.Charge = Math.Min(s.Capacity, b.Combat.Charge + Gain(s));
            }
        }
        foreach (var u in sim.Units)
        {
            for (int i = 0; i < u.Blocks.Count; i++)
            {
                if (u.Blocks[i].Hp > 0 && sim.Content.Buildings[u.Blocks[i].DefId].Shield is { } s)
                    u.BlockCombat[i]!.Charge = Math.Min(s.Capacity, u.BlockCombat[i]!.Charge + Gain(s));
            }
        }
    }

    // ---- Orders ----

    /// <summary>
    /// Steers a unit with an attack order: chases its target until in range, then holds there; an attack-move halts
    /// while anything is in range and carries on when it is clear.
    /// </summary>
    static void UpdateOrder(Simulation sim, Unit u)
    {
        // Abstract units (UC-009) are world-map nodes on tiles nobody contests: no chasing.
        if (u.Order is not { Kind: OrderKind.Attack or OrderKind.AttackMove } order || (sim.Tick + u.Id) % OrderTicks != 0 || sim.IsAbstract(u))
            return;
        int range = WeaponRange(sim, u);
        if (order.Kind == OrderKind.Attack)
        {
            if (Resolve(sim, OrderTarget(sim, u, order)) is not { } pos)
            {
                EndOrder(u);
                return;
            }
            long reach = (long)range * S * EngagePercent / 100;
            long dx = pos.X - u.X, dy = pos.Y - u.Y;
            if (range > 0 && dx * dx + dy * dy <= reach * reach)
                UnitSim.Halt(u);
            else if (UnitSim.MayChase(sim, u, pos.X, pos.Y))
                UnitSim.Chase(sim, u, pos.X, pos.Y);
            return;
        }

        bool engaged = false;
        for (int i = 0; i < u.Blocks.Count; i++)
            engaged |= u.Blocks[i].Hp > 0 && u.BlockCombat[i] is { Status: WeaponStatus.Firing };
        var dest = sim.Tiles[order.TileId];
        var (gx, gy) = dest.ToGlobal(order.X, order.Y);
        long ex = (long)gx * S + S / 2 - u.X, ey = (long)gy * S + S / 2 - u.Y;
        if (engaged)
            UnitSim.Halt(u);
        else if (ex * ex + ey * ey <= 4L * S * S && !u.Moving)
            EndOrder(u);
        else if (!u.Moving)
            UnitSim.Resume(sim, u);
    }

    /// <summary>An attack is over (target gone) or an attack-move got there: the unit stays and anchors where it is.</summary>
    static void EndOrder(Unit u)
    {
        u.Order = null;
        u.Chasing = false;
        u.Route = null;
        (u.AnchorX, u.AnchorY) = (u.X, u.Y);
    }

    /// <summary>The target an attack order names: a unit's nearest live block, or the building on a cell.</summary>
    static Target OrderTarget(Simulation sim, Unit u, UnitOrder order)
    {
        if (order.TargetId > 0)
            return UnitTarget(sim, u, order.TargetId);
        var tile = sim.Tiles[order.TileId];
        return tile.Grid.BuildingAt(order.X, order.Y) is { } b && b.Owner != u.Owner
            ? new Target(0, -1, tile.Id, b.Id) : Target.None;
    }

    /// <summary>The nearest live block of enemy unit <paramref name="id"/>, or none.</summary>
    static Target UnitTarget(Simulation sim, Unit u, int id)
    {
        if (sim.UnitById(id) is not { } t || t.Owner == u.Owner)
            return Target.None;
        int best = NearestBlock(t, u.X, u.Y, out _);
        return best < 0 ? Target.None : new Target(t.Id, best, -1, 0);
    }

    /// <summary>
    /// What the unit fires at first when in range: its attack order's target, else its squad's focus (UC-002): the
    /// squad's attack target, or the one it picked by its targeting mode (CB-007).
    /// </summary>
    static Target Preferred(Simulation sim, Unit u)
    {
        if (u.Order is { Kind: OrderKind.Attack } o)
            return OrderTarget(sim, u, o);
        return sim.Squads.Of(u) switch
        {
            { TargetId: > 0 } squad => UnitTarget(sim, u, squad.TargetId),
            { FocusId: > 0 } squad => UnitTarget(sim, u, squad.FocusId),
            _ => Target.None,
        };
    }

    /// <summary>
    /// Every <see cref="RetargetTicks"/> (staggered by squad id), or as soon as its focus is gone or out of every
    /// member's reach, each squad without an attack target picks a focus by its targeting mode among the enemy units
    /// its members have in weapon range: best score, then nearest the squad's centre, then lowest id (CB-007).
    /// </summary>
    static void FocusSquads(Simulation sim, TargetIndex targets)
    {
        foreach (var squad in sim.Squads.All)
        {
            var members = squad.Members.Select(sim.UnitById).Where(m => m != null && !sim.IsAbstract(m)).Select(m => m!).ToList();
            if (squad.TargetId > 0 || members.Count == 0)
            {
                squad.FocusId = -1;
                continue;
            }
            var reachable = new SortedDictionary<int, Unit>();
            foreach (var m in members)
            {
                long range = (long)WeaponRange(sim, m) * S;
                if (range > 0)
                    targets.EnemiesNear(m.Owner, m.X, m.Y, range, reachable);
            }
            bool keep = squad.FocusId > 0 && reachable.ContainsKey(squad.FocusId);
            if (keep && (sim.Tick + squad.Id) % RetargetTicks != 0)
                continue;
            long cx = members.Sum(m => (long)m.X) / members.Count, cy = members.Sum(m => (long)m.Y) / members.Count;
            squad.FocusId = targets.Best(reachable.Values, squad.Targeting, cx, cy)?.Id ?? -1;
        }
    }

    // ---- Firing ----

    static void FireUnit(Simulation sim, TargetIndex targets, Unit u)
    {
        if (sim.IsAbstract(u))
            return;
        var preferred = Preferred(sim, u);
        // The stance decides, but an attack order fires whatever it is (UC-004).
        bool canFire = UnitSim.CanFire(u);
        for (int i = 0; i < u.Blocks.Count; i++)
        {
            var blk = u.Blocks[i];
            if (blk.Hp <= 0 || u.BlockCombat[i] is not { } state || sim.Content.Buildings[blk.DefId] is not { Weapon: not null } def)
                continue;
            if (!canFire)
            {
                state.Status = WeaponStatus.HoldFire;
                state.Target = Target.None;
                continue;
            }
            // Out of loaded shots: reload from the cargo at once; the resource radius tops up on its own pass.
            if (def.Weapon.Ammo is { } ammo && state.Ammo == 0)
                state.Ammo += ResourceRadius.Take(u.Cargo, ammo, def.Weapon.Magazine);
            var (x, y) = BlockWorld(u, blk);
            Fire(sim, targets, state, def, u.Owner, x, y, u.Id * 64 + i, u, preferred, powered: true, u.Targeting);
        }
    }

    /// <summary>
    /// One weapon's tick: cool down, keep or find a target in range, check ammo or power, and fire.
    /// <paramref name="stagger"/> spreads target searches over ticks. Units carry their own power for now.
    /// </summary>
    static void Fire(Simulation sim, TargetIndex targets, CombatState state, BuildingDef def, int owner, long x, long y,
        int stagger, Unit? shooter, Target preferred, bool powered, TargetMode mode)
    {
        var w = def.Weapon!;
        if (state.Cooldown > 0)
            state.Cooldown--;
        long range = (long)w.Range * S;

        // Keep the current target while it is valid and in range; look again now and then for something closer. With
        // nothing to aim at it only looks every RetargetTicks too: idle weapons are most of them, and searching is the cost.
        var aim = Resolve(sim, state.Target);
        bool lost = !state.Target.IsNone && (aim == null || !InRange(aim.Value, x, y, range));
        bool search = lost || (sim.Tick + stagger) % RetargetTicks == 0;
        if (!preferred.IsNone && Resolve(sim, preferred) is { } p && InRange(p, x, y, range))
        {
            state.Target = preferred;
            aim = p;
        }
        else if (search)
        {
            state.Target = targets.Pick(owner, x, y, range, mode);
            aim = Resolve(sim, state.Target);
        }
        if (aim is not { } at)
        {
            state.Target = Target.None;
            state.Status = WeaponStatus.NoTarget;
            return;
        }

        if (w.Ammo == null ? !powered : state.Ammo <= 0)
        {
            state.Status = w.Ammo == null ? WeaponStatus.NoPower : WeaponStatus.NoAmmo;
            return;
        }
        state.Status = WeaponStatus.Firing;
        if (state.Cooldown > 0)
            return;

        state.Cooldown = w.Cooldown;
        if (w.Ammo != null)
            state.Ammo--;
        Shoot(sim, def, w, owner, x, y, at, shooter?.Id ?? 0);
    }

    static bool InRange((long X, long Y, long VX, long VY) at, long x, long y, long range)
    {
        long dx = at.X - x, dy = at.Y - y;
        return dx * dx + dy * dy <= range * range;
    }

    /// <summary>Spawns a shot from (x, y) at the target, leading a moving target by its flight time.</summary>
    static void Shoot(Simulation sim, BuildingDef def, WeaponDef w, int owner, long x, long y, (long X, long Y, long VX, long VY) at, int shooter)
    {
        long range = (long)w.Range * S;
        long tx = at.X, ty = at.Y;
        long speed = (long)w.Speed * S / Simulation.TicksPerSecond;
        if (speed > 0)
        {
            long ticks = ISqrt((tx - x) * (tx - x) + (ty - y) * (ty - y)) / speed;
            tx += at.VX * ticks;
            ty += at.VY * ticks;
        }
        long dx = tx - x, dy = ty - y;
        long len = Math.Max(1, ISqrt(dx * dx + dy * dy));
        // A beam reaches its full range; a shell flies a little past its aim point so a dodge doesn't stop it dead.
        long step = speed > 0 ? speed : range;
        sim.AddProjectile(new Projectile
        {
            Id = sim.NextProjectileId,
            Owner = owner,
            WeaponId = def.Id,
            X = x,
            Y = y,
            FromX = x,
            FromY = y,
            VX = dx * step / len,
            VY = dy * step / len,
            Damage = w.Damage,
            Type = w.Type,
            Beam = speed == 0,
            Life = speed > 0 ? (int)(range * 5 / 4 / speed) + 1 : 1,
            Shooter = shooter,
        });
    }

    /// <summary>Position and velocity of a target, or null if it is gone (destroyed, or a block that died).</summary>
    static (long X, long Y, long VX, long VY)? Resolve(Simulation sim, Target t)
    {
        if (t.Unit > 0)
        {
            if (sim.UnitById(t.Unit) is not { } u || t.Block < 0 || t.Block >= u.Blocks.Count || u.Blocks[t.Block].Hp <= 0)
                return null;
            var (x, y) = BlockWorld(u, u.Blocks[t.Block]);
            return (x, y, u.VX, u.VY);
        }
        if (t.Building > 0 && sim.TryGetTile(t.Tile) is { } tile && tile.Grid.BuildingById(t.Building) is { } b)
        {
            var (x, y) = ResourceRadius.Centre(tile, b);
            return (x, y, 0, 0);
        }
        return null;
    }

    /// <summary>Index of the live block of <paramref name="u"/> nearest a point, or -1.</summary>
    static int NearestBlock(Unit u, long x, long y, out long dist2)
    {
        int best = -1;
        dist2 = long.MaxValue;
        for (int i = 0; i < u.Blocks.Count; i++)
        {
            if (u.Blocks[i].Hp <= 0)
                continue;
            var (bx, by) = BlockWorld(u, u.Blocks[i]);
            long d = (bx - x) * (bx - x) + (by - y) * (by - y);
            if (d < dist2)
                (best, dist2) = (i, d);
        }
        return best;
    }

    // ---- Repair (CB-005) ----

    /// <summary>
    /// Each repair block heals friendly damaged buildings and unit blocks in reach, nearest cells first, and revives a
    /// friendly unit's destroyed blocks for their full cost. It pays <see cref="RepairDef.Cost"/> per
    /// <see cref="RepairDef.Per"/> hit points from its owner's cores on its tile (on a unit: cargo, then the resource radius).
    /// </summary>
    static void Repair(Simulation sim)
    {
        foreach (var tile in sim.Tiles)
        {
            foreach (var b in tile.Grid.CombatBuildings)
            {
                if (sim.Content.Buildings[b.DefId].Repair is { } r && tile.Owner == b.Owner && CombatPower.Has(tile, b))
                {
                    var (x, y) = ResourceRadius.Centre(tile, b);
                    var owner = b.Owner;
                    Heal(sim, r, b.Combat!, owner, x, y, cost => Funds.TryPay(tile, owner, cost));
                }
            }
        }
        foreach (var u in sim.Units)
        {
            for (int i = 0; i < u.Blocks.Count; i++)
            {
                if (u.Blocks[i].Hp > 0 && sim.Content.Buildings[u.Blocks[i].DefId].Repair is { } r)
                {
                    var (x, y) = BlockWorld(u, u.Blocks[i]);
                    var spare = ResourceRadius.SupplierFor(sim, u);
                    Heal(sim, r, u.BlockCombat[i]!, u.Owner, x, y, cost => u.Cargo.TryTake(cost) || spare?.TryTake(cost) == true);
                }
            }
        }
    }

    /// <param name="pay">Pays a cost (a repair batch or a block to revive), all or nothing; false if it can't.</param>
    static void Heal(Simulation sim, RepairDef r, CombatState state, int owner, long x, long y, Func<IReadOnlyDictionary<string, int>, bool> pay)
    {
        int budget = r.Rate * RepairTicks / Simulation.TicksPerSecond;
        long reach = (long)r.Radius * S;

        // Pays for up to `hp` hit points from prepaid credit, buying more as needed; returns what it could cover.
        int Afford(int hp)
        {
            while (state.RepairDebt < hp && pay(r.Cost))
                state.RepairDebt += r.Per;
            int got = Math.Min(hp, state.RepairDebt);
            state.RepairDebt -= got;
            return got;
        }

        // Buildings on tiles in reach, cell by cell from the top-left, each once.
        var defs = sim.Content.Buildings;
        int r0 = r.Radius;
        long gx0 = FloorDiv(x, S), gy0 = FloorDiv(y, S);
        var seen = new HashSet<(int, int)>();
        for (long gy = gy0 - r0; gy <= gy0 + r0 && budget > 0; gy++)
        {
            for (long gx = gx0 - r0; gx <= gx0 + r0 && budget > 0; gx++)
            {
                if (sim.TileAtCell((int)gx, (int)gy) is not { } t
                    || t.Grid.BuildingAt((int)gx - t.OriginX, (int)gy - t.OriginY) is not { } b
                    || b.Owner != owner || !seen.Add((t.Id, b.Id)))
                    continue;
                int missing = defs[b.DefId].Hp - b.Hp;
                var (cx, cy) = ResourceRadius.Centre(t, b);
                if (missing <= 0 || (cx - x) * (cx - x) + (cy - y) * (cy - y) > reach * reach)
                    continue;
                int healed = Afford(Math.Min(missing, budget));
                b.Hp += healed;
                budget -= healed;
                if (healed == 0)
                    return;
            }
        }

        foreach (var u in sim.Units)
        {
            if (u.Owner != owner || budget <= 0)
                continue;
            long dx = u.X - x, dy = u.Y - y, far = reach + u.Radius;
            if (dx * dx + dy * dy > far * far)
                continue;
            bool revived = false;
            for (int i = 0; i < u.Blocks.Count && budget > 0; i++)
            {
                var blk = u.Blocks[i];
                var def = defs[blk.DefId];
                var (bx, by) = BlockWorld(u, blk);
                if (blk.Hp >= def.Hp || (bx - x) * (bx - x) + (by - y) * (by - y) > reach * reach)
                    continue;
                if (blk.Hp <= 0)
                {
                    // Rebuilding a destroyed block costs the block itself.
                    if (!pay(def.Cost))
                        continue;
                    blk.Hp = 1;
                    revived = true;
                }
                int healed = Afford(Math.Min(def.Hp - blk.Hp, budget));
                blk.Hp += healed;
                budget -= healed;
            }
            if (revived)
                u.RecomputeStats(defs);
        }
    }

    // ---- Shots in flight ----

    /// <summary>
    /// Moves every live shot along its path in steps of <see cref="SampleStep"/>, stopping at the first thing it meets:
    /// an enemy shield it crosses into, an enemy building's cell, or an enemy unit's live block (its exact square in the
    /// unit's frame). Friendly blocks, bare frame and wrecks don't stop shots.
    /// </summary>
    static void Fly(Simulation sim, TargetIndex targets, List<Unit> dead)
    {
        var shields = Shields(sim);
        foreach (var p in sim.Projectiles)
        {
            if (p.Done)
                continue;
            long len = Math.Max(1, ISqrt(p.VX * p.VX + p.VY * p.VY));
            int steps = (int)((len + SampleStep - 1) / SampleStep);
            long sx = p.X, sy = p.Y;
            for (int k = 1; k <= steps && !p.Done; k++)
            {
                long x = sx + p.VX * k / steps, y = sy + p.VY * k / steps;
                p.X = x;
                p.Y = y;
                if (HitShield(shields, p, x, y) || HitBuilding(sim, p, x, y) || HitUnit(sim, targets, p, x, y, dead))
                    p.Done = true;
            }
            if (--p.Life <= 0)
                p.Done = true;
        }
    }

    readonly record struct ShieldBubble(long X, long Y, long R, int Owner, CombatState State);

    static List<ShieldBubble> Shields(Simulation sim)
    {
        var list = new List<ShieldBubble>();
        foreach (var tile in sim.Tiles)
        {
            foreach (var b in tile.Grid.CombatBuildings)
            {
                if (sim.Content.Buildings[b.DefId].Shield is { } s && b.Combat!.Charge > 0)
                {
                    var (x, y) = ResourceRadius.Centre(tile, b);
                    list.Add(new(x, y, (long)s.Radius * S, b.Owner, b.Combat));
                }
            }
        }
        foreach (var u in sim.Units)
        {
            for (int i = 0; i < u.Blocks.Count; i++)
            {
                if (u.Blocks[i].Hp > 0 && sim.Content.Buildings[u.Blocks[i].DefId].Shield is { } s && u.BlockCombat[i]!.Charge > 0)
                    list.Add(new(u.X, u.Y, (long)s.Radius * S, u.Owner, u.BlockCombat[i]!));
            }
        }
        return list;
    }

    /// <summary>An enemy shot crossing into a charged bubble spends charge instead (a shot fired inside isn't stopped).</summary>
    static bool HitShield(List<ShieldBubble> shields, Projectile p, long x, long y)
    {
        foreach (var s in shields)
        {
            if (s.Owner == p.Owner || s.State.Charge <= 0)
                continue;
            long dx = x - s.X, dy = y - s.Y, fx = p.FromX - s.X, fy = p.FromY - s.Y, r2 = s.R * s.R;
            if (dx * dx + dy * dy > r2 || fx * fx + fy * fy <= r2)
                continue;
            s.State.Charge = Math.Max(0, s.State.Charge - p.Damage);
            return true;
        }
        return false;
    }

    static bool HitBuilding(Simulation sim, Projectile p, long x, long y)
    {
        int gx = (int)FloorDiv(x, S), gy = (int)FloorDiv(y, S);
        if (sim.TileAtCell(gx, gy) is not { } tile || tile.Grid.BuildingAt(gx - tile.OriginX, gy - tile.OriginY) is not { } b
            || b.Owner == p.Owner)
            return false;
        DamageBuilding(sim, tile, b, p.Damage, p.Type, p.Shooter);
        return true;
    }

    static bool HitUnit(Simulation sim, TargetIndex targets, Projectile p, long x, long y, List<Unit> dead)
    {
        foreach (var u in targets.UnitsNear(x, y))
        {
            if (u.Owner == p.Owner || dead.Contains(u))
                continue;
            long dx = x - u.X, dy = y - u.Y, far = u.Radius + S / 2;
            if (dx * dx + dy * dy > far * far)
                continue;
            // Into the unit's frame, then the cell under the point.
            var (lx, ly) = Trig.Rotate(dx, dy, -u.Angle);
            var c0 = u.Cells[0];
            long gx = FloorDiv(lx - c0.OffX + S / 2, S) + c0.GX, gy = FloorDiv(ly - c0.OffY + S / 2, S) + c0.GY;
            for (int i = 0; i < u.Blocks.Count; i++)
            {
                var blk = u.Blocks[i];
                if (blk.Hp <= 0 || gx < blk.GX || gy < blk.GY || gx >= blk.GX + blk.Width || gy >= blk.GY + blk.Height)
                    continue;
                if (DamageBlock(sim, u, i, p.Damage, p.Type, p.Shooter) && i == u.CoreBlock)
                    dead.Add(u);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Units bucketed by area for the combat step, so shots and target searches only look at units nearby; and the
    /// target search itself. Built once per step, after orders and before units move.
    /// </summary>
    sealed class TargetIndex
    {
        const int Bucket = 8 * S;
        readonly Simulation _sim;
        readonly Dictionary<(long, long), List<Unit>> _buckets = [];
        static readonly List<Unit> Empty = [];

        public TargetIndex(Simulation sim)
        {
            _sim = sim;
            foreach (var u in sim.Units)
            {
                // Abstract units (UC-009) can't be seen or hit until they materialise.
                if (sim.IsAbstract(u))
                    continue;
                long r = u.Radius + S;
                for (long by = FloorDiv(u.Y - r, Bucket); by <= FloorDiv(u.Y + r, Bucket); by++)
                {
                    for (long bx = FloorDiv(u.X - r, Bucket); bx <= FloorDiv(u.X + r, Bucket); bx++)
                    {
                        if (!_buckets.TryGetValue((bx, by), out var list))
                            _buckets[(bx, by)] = list = [];
                        list.Add(u);
                    }
                }
            }
        }

        public List<Unit> UnitsNear(long x, long y) => _buckets.GetValueOrDefault((FloorDiv(x, Bucket), FloorDiv(y, Bucket))) ?? Empty;

        readonly Dictionary<Unit, (int Blocks, int Weapons)> _counts = [];

        /// <summary>Live blocks and live weapon blocks of a unit, counted once per step.</summary>
        (int Blocks, int Weapons) Counts(Unit u)
        {
            if (_counts.TryGetValue(u, out var c))
                return c;
            for (int i = 0; i < u.Blocks.Count; i++)
            {
                if (u.Blocks[i].Hp > 0)
                    c = (c.Blocks + 1, c.Weapons + (_sim.Content.Buildings[u.Blocks[i].DefId].Weapon != null ? 1 : 0));
            }
            return _counts[u] = c;
        }

        int Score(Unit u, TargetMode mode) => mode switch
        {
            TargetMode.MostBlocks => Counts(u).Blocks,
            TargetMode.MostWeapons => Counts(u).Weapons,
            _ => 0,
        };

        /// <summary>Adds the enemies of <paramref name="owner"/> with a live block within <paramref name="range"/> of (x, y), by id.</summary>
        public void EnemiesNear(int owner, long x, long y, long range, SortedDictionary<int, Unit> into)
        {
            for (long by = FloorDiv(y - range, Bucket); by <= FloorDiv(y + range, Bucket); by++)
            {
                for (long bx = FloorDiv(x - range, Bucket); bx <= FloorDiv(x + range, Bucket); bx++)
                {
                    foreach (var u in _buckets.GetValueOrDefault((bx, by)) ?? Empty)
                    {
                        if (u.Owner != owner && !into.ContainsKey(u.Id) && NearestBlock(u, x, y, out long d) >= 0 && d <= range * range)
                            into.Add(u.Id, u);
                    }
                }
            }
        }

        /// <summary>The unit with the best score by <paramref name="mode"/>, then the nearest (x, y), then the lowest id.</summary>
        public Unit? Best(IEnumerable<Unit> units, TargetMode mode, long x, long y)
        {
            Unit? best = null;
            int bestScore = -1;
            long bestD = long.MaxValue;
            foreach (var u in units)
            {
                int score = Score(u, mode);
                long dx = u.X - x, dy = u.Y - y, d = dx * dx + dy * dy;
                if (score > bestScore || (score == bestScore && (d < bestD || (d == bestD && u.Id < best!.Id))))
                    (best, bestScore, bestD) = (u, score, d);
            }
            return best;
        }

        /// <summary>
        /// What a weapon at (x, y) fires at among enemies in range: the live unit block of the unit with the best score by
        /// <paramref name="mode"/> (CB-007; units are the threat), nearest block first, ties to the nearer unit then the
        /// lowest id; else the nearest enemy building, the first cell row by row on ties.
        /// </summary>
        public Target Pick(int owner, long x, long y, long range, TargetMode mode)
        {
            Target best = Target.None;
            long bestD = range * range + 1;
            int bestScore = -1;
            var seen = new HashSet<Unit>();
            for (long by = FloorDiv(y - range, Bucket); by <= FloorDiv(y + range, Bucket); by++)
            {
                for (long bx = FloorDiv(x - range, Bucket); bx <= FloorDiv(x + range, Bucket); bx++)
                {
                    foreach (var u in _buckets.GetValueOrDefault((bx, by)) ?? Empty)
                    {
                        if (u.Owner == owner || !seen.Add(u))
                            continue;
                        int i = NearestBlock(u, x, y, out long d);
                        if (i < 0 || d > range * range)
                            continue;
                        int score = Score(u, mode);
                        if (score > bestScore || (score == bestScore && (d < bestD || (d == bestD && u.Id < best.Unit))))
                            (best, bestD, bestScore) = (new Target(u.Id, i, -1, 0), d, score);
                    }
                }
            }
            if (!best.IsNone)
                return best;

            int r = (int)(range / S);
            long gx0 = FloorDiv(x, S), gy0 = FloorDiv(y, S);
            for (long gy = gy0 - r; gy <= gy0 + r; gy++)
            {
                for (long gx = gx0 - r; gx <= gx0 + r; gx++)
                {
                    if (_sim.TileAtCell((int)gx, (int)gy) is not { } t
                        || t.Grid.BuildingAt((int)gx - t.OriginX, (int)gy - t.OriginY) is not { } b || b.Owner == owner)
                        continue;
                    long cx = gx * S + S / 2 - x, cy = gy * S + S / 2 - y, d = cx * cx + cy * cy;
                    if (d < bestD)
                        (best, bestD) = (new Target(0, -1, t.Id, b.Id), d);
                }
            }
            return best;
        }
    }
}

/// <summary>Combat's link to power (FB-005): weapons, shields and repair blocks on a tile work while powered.</summary>
internal static class CombatPower
{
    public static bool Has(TileState tile, PlacedBuilding b) => PowerNet.IsPowered(tile, b);
}
