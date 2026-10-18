using static Wunderwaffe.Sim.Core.IntMath;

namespace Wunderwaffe.Sim.World;

public enum MoveResult
{
    Ok,
    NoSuchTile,
    OutOfBounds,
    NoUnits,

    /// <summary>None of the units has an engine strong enough to move.</summary>
    CannotMove,

    /// <summary>No unit's locomotion can reach any part of the target tile (UD-004).</summary>
    Unreachable,
}

/// <summary>What a unit was told to do (UC-001). Attack and AttackMove are combat's; movement treats them as moves.</summary>
public enum OrderKind
{
    Move,

    /// <summary>Stop and stay put. Fires but never chases.</summary>
    Hold,

    /// <summary>Go back and forth between where the order was given and its target.</summary>
    Patrol,

    /// <summary>Fall back to the nearest tile the player owns, without fighting.</summary>
    Retreat,
    Attack,
    AttackMove,
}

/// <summary>
/// A unit's standing order. <paramref name="TileId"/>/<paramref name="X"/>/<paramref name="Y"/> is the target cell in
/// that tile's coordinates (shared by every unit given the order; each unit's own formation slot is
/// <see cref="Unit.SlotX"/>/<see cref="Unit.SlotY"/>). <paramref name="TargetId"/> is the unit to attack, or -1.
/// <paramref name="BackTileId"/>/<paramref name="BackX"/>/<paramref name="BackY"/> is a patrol's other end: the cell
/// this unit stood on when it got the order.
/// </summary>
public sealed record UnitOrder(OrderKind Kind, int TileId = -1, int X = 0, int Y = 0, int TargetId = -1,
    int BackTileId = -1, int BackX = 0, int BackY = 0);

public enum RallyResult
{
    Ok,
    NoSuchTile,
    NoSuchBuilding,
    NotOwner,
    OutOfBounds,
    NoSuchSquad,
}

/// <summary>
/// Orders, squads, formations, stances and factory rally points (UC-001 to UC-006). An order turns into a route of
/// tiles through terrain the unit's locomotion can cross (<see cref="UnitNav"/>) and a goal point; the physics step
/// (<see cref="UnitPhysics"/>) flies the unit along it. <see cref="Step"/> runs the stances at the start of the unit
/// phase. Read-only checks are public so the UI can preview with the same rules the commands apply.
/// </summary>
public static class UnitSim
{
    const int S = Unit.CellScale;

    /// <summary>Least distance between neighbouring formation slots, in sub-cells.</summary>
    const int MinSpacing = 2 * S;

    /// <summary>Space left between the hulls of neighbouring units in a formation.</summary>
    const int Gap = S / 2;

    const int BoxColumns = 4;

    /// <summary>How far, in cells, a defensive unit chases from its anchor or course (UC-004).</summary>
    public const int LeashCells = 12;

    // ---------------------------------------------------------------- orders

    /// <summary>Sends units to a cell (a move order). Same as <see cref="Issue"/> with <see cref="OrderKind.Move"/>.</summary>
    public static MoveResult Order(Simulation sim, int player, IReadOnlyList<int> unitIds, int tileId, int cellX, int cellY) =>
        Issue(sim, player, unitIds, OrderKind.Move, tileId, cellX, cellY);

    /// <summary>
    /// Gives units an order. Moves, patrols and attacks send them to the cell in formation: the squad's when they are
    /// all one squad's, else a box. Hold and retreat ignore the cell. Units that can't move, or whose locomotion can't
    /// get there, keep their old order.
    /// </summary>
    internal static MoveResult Issue(Simulation sim, int player, IReadOnlyList<int> unitIds, OrderKind kind, int tileId,
        int cellX, int cellY, int targetId = -1)
    {
        var units = Owned(sim, player, unitIds);
        if (kind == OrderKind.Hold)
        {
            foreach (var u in units)
                Hold(u);
            return units.Count == 0 ? MoveResult.NoUnits : MoveResult.Ok;
        }
        if (kind == OrderKind.Retreat)
            return Retreat(sim, units);

        if (sim.TryGetTile(tileId) is not { } dest)
            return MoveResult.NoSuchTile;
        if (!dest.Grid.InBounds(cellX, cellY))
            return MoveResult.OutOfBounds;

        var squad = units.Count > 0 && units.All(u => u.SquadId != 0 && u.SquadId == units[0].SquadId) ? sim.Squads.Of(units[0]) : null;
        var order = new UnitOrder(kind, tileId, cellX, cellY, targetId);
        if (squad != null)
        {
            squad.Order = order;
            units = [.. units.OrderBy(u => squad.MemberList.IndexOf(u.Id))];
        }
        return MoveGroup(sim, units, order, squad?.Formation ?? Formation.Box, squad?.Custom ?? []);
    }

    static List<Unit> Owned(Simulation sim, int player, IReadOnlyList<int> unitIds) =>
        [.. unitIds.Distinct().Select(sim.UnitById).Where(u => u != null && u.Owner == player).Select(u => u!)];

    /// <summary>Sends units to their formation slots around the order's target cell.</summary>
    static MoveResult MoveGroup(Simulation sim, List<Unit> units, UnitOrder order, Formation formation, (int X, int Y)[] custom)
    {
        if (units.Count == 0)
            return MoveResult.NoUnits;
        var movable = units.Where(u => u.MaxSpeed > 0).ToList();
        if (movable.Count == 0)
            return MoveResult.CannotMove;

        var dest = sim.Tiles[order.TileId];
        var (gx, gy) = dest.ToGlobal(order.X, order.Y);
        long cx = (long)gx * S + S / 2, cy = (long)gy * S + S / 2;
        long mx = movable.Sum(u => (long)u.X) / movable.Count, my = movable.Sum(u => (long)u.Y) / movable.Count;
        int heading = cx == mx && cy == my ? 0 : Trig.Atan2(cy - my, cx - mx);
        var slots = Slots(formation, custom, movable, heading);

        var result = MoveResult.Unreachable;
        for (int i = 0; i < movable.Count; i++)
        {
            var u = movable[i];
            var back = sim.Tiles[u.TileId];
            var own = order with
            {
                BackTileId = order.Kind == OrderKind.Patrol ? back.Id : -1,
                BackX = order.Kind == OrderKind.Patrol ? (int)FloorDiv(u.X, S) - back.OriginX : 0,
                BackY = order.Kind == OrderKind.Patrol ? (int)FloorDiv(u.Y, S) - back.OriginY : 0,
            };
            if (!Goto(sim, u, cx + slots[i].X, cy + slots[i].Y, dest, (gx, gy)))
                continue;
            u.Order = own;
            u.SlotX = u.DestX;
            u.SlotY = u.DestY;
            u.Chasing = false;
            u.PatrolReturning = false;
            result = MoveResult.Ok;
        }
        return result;
    }

    /// <summary>
    /// Routes a unit to world sub-cell (x, y), in whatever tile covers it; if no tile does, to cell
    /// <paramref name="fallback"/> of <paramref name="dest"/>. False, leaving the unit alone, if its locomotion can't
    /// reach that tile. A goal it can't stand on moves to the nearest reachable cell of the tile.
    /// </summary>
    internal static bool Goto(Simulation sim, Unit u, long x, long y, TileState dest, (int X, int Y) fallback)
    {
        var goal = ((int)FloorDiv(x, S), (int)FloorDiv(y, S));
        if (sim.TileAtCell(goal.Item1, goal.Item2) is { } t)
        {
            dest = t;
        }
        else
        {
            goal = fallback;
            (x, y) = ((long)fallback.X * S + S / 2, (long)fallback.Y * S + S / 2);
        }
        var asked = goal;
        var from = ((int)FloorDiv(u.X, S), (int)FloorDiv(u.Y, S));
        if (!UnitNav.FindRoute(sim, u.Locomotion, UnitPath.Clearance(u), from, dest, ref goal, out var tiles, out var regions))
            return false;
        if (goal != asked)
            (x, y) = ((long)goal.Item1 * S + S / 2, (long)goal.Item2 * S + S / 2);

        u.Route = tiles;
        u.RouteRegions = regions;
        u.RouteIndex = 0;
        u.PathKey = null;
        u.Portal = null;
        u.DestTileId = dest.Id;
        u.DestX = (int)x;
        u.DestY = (int)y;
        return true;
    }

    /// <summary>Plans the route to the unit's current goal again, from where it is now (it drifted off its route).</summary>
    internal static void Replan(Simulation sim, Unit u)
    {
        var dest = sim.Tiles[u.DestTileId];
        var cell = ((int)FloorDiv(u.DestX, S), (int)FloorDiv(u.DestY, S));
        if (!Goto(sim, u, u.DestX, u.DestY, dest, cell))
            u.Route = null;
    }

    static void Hold(Unit u)
    {
        u.Order = new UnitOrder(OrderKind.Hold);
        u.Route = null;
        u.Chasing = false;
        (u.AnchorX, u.AnchorY) = (u.X, u.Y);
    }

    /// <summary>Each unit falls back to the owned tile it can reach in the fewest crossings, in a box around its centre.</summary>
    static MoveResult Retreat(Simulation sim, List<Unit> units)
    {
        if (units.Count == 0)
            return MoveResult.NoUnits;
        var result = MoveResult.Unreachable;
        foreach (var group in units.Where(u => u.MaxSpeed > 0).GroupBy(u => RetreatTile(sim, u)).Where(g => g.Key != null).OrderBy(g => g.Key!.Id))
        {
            var home = group.Key!;
            var r = MoveGroup(sim, [.. group], new UnitOrder(OrderKind.Retreat, home.Id, home.CenterX - home.OriginX, home.CenterY - home.OriginY),
                Formation.Box, []);
            if (r == MoveResult.Ok)
                result = r;
        }
        return result;
    }

    /// <summary>The tile a unit retreats to: of those its owner holds that it can reach, the fewest crossings away (ties: lowest id).</summary>
    public static TileState? RetreatTile(Simulation sim, Unit u)
    {
        TileState? best = null;
        int bestSteps = int.MaxValue;
        var from = ((int)FloorDiv(u.X, S), (int)FloorDiv(u.Y, S));
        foreach (var t in sim.Tiles.Where(t => t.Owner == u.Owner))
        {
            var goal = (t.CenterX, t.CenterY);
            if (UnitNav.FindRoute(sim, u.Locomotion, UnitPath.Clearance(u), from, t, ref goal, out var tiles, out _) && tiles.Length < bestSteps)
                (best, bestSteps) = (t, tiles.Length);
        }
        return best;
    }

    /// <summary>
    /// Formation slots (UC-003): offsets from the target point, in sub-cells, one per unit in order, spaced by the
    /// units' sizes and turned to face <paramref name="heading"/>. A box stays square to the map.
    /// </summary>
    public static List<(long X, long Y)> Slots(Formation formation, (int X, int Y)[] custom, IReadOnlyList<Unit> units, int heading)
    {
        int n = units.Count;
        long spacing = Math.Max(MinSpacing, 2L * units.Max(u => u.Radius) + Gap);
        var local = new List<(long F, long R)>(n);
        switch (formation)
        {
            case Formation.Box:
            {
                int columns = Math.Min(n, BoxColumns), rows = (n + BoxColumns - 1) / BoxColumns;
                // A grid centred on the target, so a lone unit lands exactly on it.
                return [.. Enumerable.Range(0, n).Select(i =>
                    ((i % columns * 2 - (columns - 1)) * spacing / 2, (i / columns * 2 - (rows - 1)) * spacing / 2))];
            }
            case Formation.Line or Formation.Column:
            {
                long pos = 0;
                for (int i = 0; i < n; i++)
                {
                    if (i > 0)
                        pos += units[i - 1].Radius + Gap + units[i].Radius;
                    local.Add(formation == Formation.Line ? (0, pos) : (-pos, 0));
                }
                long half = pos / 2;
                local = [.. local.Select(p => formation == Formation.Line ? (p.F, p.R - half) : (p.F + half, p.R))];
                break;
            }
            case Formation.Wedge:
            {
                for (int i = 0; i < n; i++)
                {
                    long rank = (i + 1) / 2;
                    local.Add((-rank * spacing, (i % 2 == 1 ? -1 : 1) * rank * spacing));
                }
                long meanF = local.Sum(p => p.F) / n;
                local = [.. local.Select(p => (p.F - meanF, p.R))];
                break;
            }
            case Formation.Custom:
            {
                long back = custom.Length == 0 ? 0 : custom.Min(c => c.X) * (long)S;
                for (int i = 0; i < n; i++)
                    local.Add(i < custom.Length ? (custom[i].X * (long)S, custom[i].Y * (long)S) : (back - (i - custom.Length + 1) * spacing, 0));
                break;
            }
        }
        return [.. local.Select(p => Trig.Rotate(p.F, p.R, heading))];
    }

    /// <summary>The physics step reached the unit's goal. Patrols turn round; moves end, leaving it anchored there.</summary>
    internal static void Arrived(Simulation sim, Unit u)
    {
        if (u.Chasing)
            return;
        switch (u.Order?.Kind)
        {
            case OrderKind.Patrol when u.Order is { } o:
                u.PatrolReturning = !u.PatrolReturning;
                var back = sim.Tiles[o.BackTileId];
                var (bx, by) = back.ToGlobal(o.BackX, o.BackY);
                if (u.PatrolReturning)
                    Goto(sim, u, (long)bx * S + S / 2, (long)by * S + S / 2, back, (bx, by));
                else
                    Goto(sim, u, u.SlotX, u.SlotY, sim.Tiles[o.TileId], sim.Tiles[o.TileId].ToGlobal(o.X, o.Y));
                break;
            case OrderKind.Move or OrderKind.Retreat or OrderKind.AttackMove:
                u.Order = null;
                (u.AnchorX, u.AnchorY) = (u.X, u.Y);
                break;
            case null:
                (u.AnchorX, u.AnchorY) = (u.X, u.Y);
                break;
        }
    }

    // ---------------------------------------------------------------- stances and combat hooks

    /// <summary>Gives units a stance (UC-004). Anchors them where they stand; a chase the old stance began ends.</summary>
    internal static void SetStance(Simulation sim, int player, IReadOnlyList<int> unitIds, Stance stance)
    {
        if (!Enum.IsDefined(stance))
            return;
        foreach (var u in Owned(sim, player, unitIds))
        {
            u.Stance = stance;
            (u.AnchorX, u.AnchorY) = (u.X, u.Y);
            if (u.Chasing && u.Order?.Kind is not (OrderKind.Attack or OrderKind.AttackMove))
            {
                if (u.Order == null)
                {
                    u.Chasing = false;
                    u.Route = null;
                }
                else
                {
                    Resume(sim, u);
                }
            }
        }
    }

    /// <summary>Gives units a targeting mode (CB-007).</summary>
    internal static void SetTargeting(Simulation sim, int player, IReadOnlyList<int> unitIds, TargetMode mode)
    {
        if (!Enum.IsDefined(mode))
            return;
        foreach (var u in Owned(sim, player, unitIds))
            u.Targeting = mode;
    }

    /// <summary>
    /// Whether a travelling unit that meets enemies stops to fight (aggressive, defensive) or keeps its course
    /// (passive, hold fire), UC-004 / UC-009. Abstract travel asks this when a node meets enemy units.
    /// </summary>
    public static bool StopsToFight(Unit u) => u.Stance is Stance.Aggressive or Stance.Defensive;

    /// <summary>
    /// Whether a unit's weapons fire: always on an attack or attack-move order; otherwise not on hold-fire stance and
    /// not while retreating.
    /// </summary>
    public static bool CanFire(Unit u) =>
        u.Order?.Kind is OrderKind.Attack or OrderKind.AttackMove || (u.Stance != Stance.HoldFire && u.Order?.Kind != OrderKind.Retreat);

    /// <summary>
    /// Whether combat may send the unit after the target of an attack order at world sub-cell (x, y). An order is the
    /// player's, so any stance carries it out (UC-004); only a unit that can't move can't.
    /// </summary>
    public static bool MayChase(Simulation sim, Unit u, long x, long y) => u.MaxSpeed > 0;

    /// <summary>Sends the unit toward world sub-cell (x, y) without dropping its order (combat chasing a target).</summary>
    public static void Chase(Simulation sim, Unit u, long x, long y)
    {
        // Already heading there (within a cell): leave the plan alone rather than re-routing every tick.
        if (u.Chasing && u.Route != null && Within(x, y, u.DestX, u.DestY, 1))
            return;
        var cell = ((int)FloorDiv(x, S), (int)FloorDiv(y, S));
        if (sim.TileAtCell(cell.Item1, cell.Item2) is { } t && Goto(sim, u, x, y, t, cell))
            u.Chasing = true;
    }

    /// <summary>Stops the unit where it is (in weapon range, say) without dropping its order; <see cref="Resume"/> carries on.</summary>
    public static void Halt(Unit u) => u.Route = null;

    /// <summary>Ends a chase: back to the standing order, or for an idle unit, back to its anchor.</summary>
    public static void Resume(Simulation sim, Unit u)
    {
        u.Chasing = false;
        if (u.Order is { Kind: not OrderKind.Hold } o)
        {
            var dest = sim.Tiles[o.TileId];
            if (o.Kind == OrderKind.Patrol && u.PatrolReturning)
            {
                var back = sim.Tiles[o.BackTileId];
                var (bx, by) = back.ToGlobal(o.BackX, o.BackY);
                Goto(sim, u, (long)bx * S + S / 2, (long)by * S + S / 2, back, (bx, by));
            }
            else
            {
                Goto(sim, u, u.SlotX, u.SlotY, dest, dest.ToGlobal(o.X, o.Y));
            }
        }
        else if (u.Order == null)
        {
            ReturnToAnchor(sim, u);
        }
        else
        {
            u.Route = null;
        }
    }

    static void ReturnToAnchor(Simulation sim, Unit u)
    {
        if (u.Route != null && u.DestX == u.AnchorX && u.DestY == u.AnchorY)
            return;
        var cell = ((int)FloorDiv(u.AnchorX, S), (int)FloorDiv(u.AnchorY, S));
        if (sim.TileAtCell(cell.Item1, cell.Item2) is { } t && !Goto(sim, u, u.AnchorX, u.AnchorY, t, cell))
            u.Route = null;
    }

    static bool Within(long x, long y, long cx, long cy, int cells)
    {
        long dx = x - cx, dy = y - cy, r = (long)cells * S;
        return dx * dx + dy * dy <= r * r;
    }

    /// <summary>How often, in ticks, each unit's stance looks for something to fight (staggered by unit id).</summary>
    const int EngageTicks = 10;

    /// <summary>A unit stops closing in once its target is this share of its weapon range away.</summary>
    const int EngagePercent = 85;

    /// <summary>Start of the unit phase: drops squad members that are gone, then runs the stances.</summary>
    internal static void Step(Simulation sim)
    {
        foreach (var s in sim.Squads.All)
            s.MemberList.RemoveAll(id => sim.UnitById(id) is not { } m || m.SquadId != s.Id);

        foreach (var u in sim.Units)
        {
            if ((sim.Tick + u.Id) % EngageTicks == 0)
                Engage(sim, u);
        }
    }

    /// <summary>
    /// What a unit's stance does on its own (UC-004). Aggressive: with no order, or once stopped to fight, goes for the
    /// nearest enemy anywhere on its tile; on its way somewhere, stops for enemies in weapon range. Defensive: goes for
    /// enemies it can reach within <see cref="LeashCells"/> of its anchor (where it stood, or where it left its course),
    /// never further, then returns or carries on. Passive and hold fire never move on their own. Attack orders are
    /// combat's, and hold and retreat orders don't fight.
    /// </summary>
    static void Engage(Simulation sim, Unit u)
    {
        if (u.MaxSpeed <= 0 || u.Stance is Stance.Passive or Stance.HoldFire || sim.IsAbstract(u)
            || u.Order?.Kind is OrderKind.Hold or OrderKind.Retreat or OrderKind.Attack or OrderKind.AttackMove)
            return;
        int range = Combat.WeaponRange(sim, u);
        if (range <= 0)
            return;
        long reach = (long)range * S * EngagePercent / 100;

        (long X, long Y)? target;
        if (u.Stance == Stance.Aggressive)
        {
            bool wide = u.Order == null || u.Route == null || u.Chasing;
            target = wide ? NearestOnTile(sim, u, buildings: u.Order == null) : NearestUnit(sim, u, u.X, u.Y, (long)range * S);
        }
        else
        {
            if (u.Order != null && !u.Chasing)
                (u.AnchorX, u.AnchorY) = (u.X, u.Y);
            target = NearestUnit(sim, u, u.AnchorX, u.AnchorY, ((long)LeashCells + range) * S);
        }

        if (target is not { } at)
        {
            if (!u.Chasing)
                return;
            if (u.Stance == Stance.Aggressive && u.Order == null)
            {
                // Nothing left on the tile: it stays where the fight ended.
                u.Chasing = false;
                u.Route = null;
                (u.AnchorX, u.AnchorY) = (u.X, u.Y);
            }
            else
            {
                Resume(sim, u);
            }
            return;
        }

        long dx = at.X - u.X, dy = at.Y - u.Y, dist = ISqrt(dx * dx + dy * dy);
        if (dist <= reach)
        {
            Halt(u);
            u.Chasing = true;
            return;
        }
        // Close in to just inside weapon range; a defensive unit no further than its leash from its anchor.
        long gx = at.X - dx * reach / dist, gy = at.Y - dy * reach / dist;
        if (u.Stance == Stance.Defensive)
        {
            long ax = gx - u.AnchorX, ay = gy - u.AnchorY, ad = ISqrt(ax * ax + ay * ay), leash = (long)LeashCells * S;
            if (ad > leash)
                (gx, gy) = (u.AnchorX + ax * leash / ad, u.AnchorY + ay * leash / ad);
        }
        Chase(sim, u, gx, gy);
        u.Chasing = true;
    }

    /// <summary>Centre of the nearest live enemy unit within <paramref name="radius"/> sub-cells of (x, y), ties to the lowest id.</summary>
    static (long X, long Y)? NearestUnit(Simulation sim, Unit u, long x, long y, long radius)
    {
        (long X, long Y)? best = null;
        long bestD = radius * radius + 1;
        foreach (var e in sim.Units)
        {
            if (e.Owner == u.Owner || sim.IsAbstract(e))
                continue;
            long dx = e.X - x, dy = e.Y - y, d = dx * dx + dy * dy;
            if (d < bestD)
                (best, bestD) = ((e.X, e.Y), d);
        }
        return best;
    }

    /// <summary>
    /// The nearest enemy unit on the unit's tile, else, if <paramref name="buildings"/>, the nearest enemy building there
    /// (UC-004 aggressive: an idle unit goes for anything; one stopped on its way fights units, then carries on).
    /// </summary>
    static (long X, long Y)? NearestOnTile(Simulation sim, Unit u, bool buildings)
    {
        (long X, long Y)? best = null;
        long bestD = long.MaxValue;
        foreach (var e in sim.Units)
        {
            if (e.Owner == u.Owner || e.TileId != u.TileId || sim.IsAbstract(e))
                continue;
            long dx = e.X - (long)u.X, dy = e.Y - (long)u.Y, d = dx * dx + dy * dy;
            if (d < bestD)
                (best, bestD) = ((e.X, e.Y), d);
        }
        if (best != null || !buildings)
            return best;
        var tile = sim.Tiles[u.TileId];
        foreach (var b in tile.Grid.Buildings)
        {
            if (b.Owner == u.Owner)
                continue;
            long bx = (long)(tile.OriginX + b.X) * S + (long)b.Width * S / 2, by = (long)(tile.OriginY + b.Y) * S + (long)b.Height * S / 2;
            long dx = bx - u.X, dy = by - u.Y, d = dx * dx + dy * dy;
            if (d < bestD)
                (best, bestD) = ((bx, by), d);
        }
        return best;
    }

    // ---------------------------------------------------------------- squads

    public static SquadResult CheckCreateSquad(Simulation sim, int player, string? name, IReadOnlyList<int> unitIds)
    {
        if (!Squads.ValidName(name))
            return SquadResult.BadName;
        return Owned(sim, player, unitIds).Count == 0 ? SquadResult.NoUnits : SquadResult.Ok;
    }

    internal static void CreateSquad(Simulation sim, int player, string name, IReadOnlyList<int> unitIds)
    {
        if (CheckCreateSquad(sim, player, name, unitIds) != SquadResult.Ok)
            return;
        var units = Owned(sim, player, unitIds);
        var squad = sim.Squads.Create(player, name.Trim());
        squad.Stance = units[0].Stance;
        squad.Targeting = units[0].Targeting;
        foreach (var u in units)
            Join(sim, squad, u);
    }

    /// <summary>The squad exists and is the player's.</summary>
    public static SquadResult CheckSquad(Simulation sim, int player, int squadId) =>
        sim.Squads.ById(squadId) is not { } s ? SquadResult.NoSuchSquad
        : s.Owner != player ? SquadResult.NotOwner
        : SquadResult.Ok;

    internal static void DisbandSquad(Simulation sim, int player, int squadId)
    {
        if (CheckSquad(sim, player, squadId) != SquadResult.Ok)
            return;
        var squad = sim.Squads.ById(squadId)!;
        foreach (int id in squad.MemberList)
        {
            if (sim.UnitById(id) is { } u)
                u.SquadId = 0;
        }
        sim.Squads.Remove(squad);
    }

    /// <summary>Moves units into a squad (out of any other), or out of every squad when <paramref name="squadId"/> is 0.</summary>
    internal static void AssignSquad(Simulation sim, int player, int squadId, IReadOnlyList<int> unitIds)
    {
        if (squadId != 0 && CheckSquad(sim, player, squadId) != SquadResult.Ok)
            return;
        var squad = squadId == 0 ? null : sim.Squads.ById(squadId);
        foreach (var u in Owned(sim, player, unitIds))
        {
            if (squad != null)
                Join(sim, squad, u);
            else
                Leave(sim, u);
        }
    }

    static void Join(Simulation sim, Squad squad, Unit u)
    {
        if (u.SquadId == squad.Id)
            return;
        Leave(sim, u);
        u.SquadId = squad.Id;
        u.Stance = squad.Stance;
        u.Targeting = squad.Targeting;
        (u.AnchorX, u.AnchorY) = (u.X, u.Y);
        squad.MemberList.Add(u.Id);
    }

    static void Leave(Simulation sim, Unit u)
    {
        sim.Squads.Of(u)?.MemberList.Remove(u.Id);
        u.SquadId = 0;
    }

    internal static void RenameSquad(Simulation sim, int player, int squadId, string name)
    {
        if (CheckSquad(sim, player, squadId) == SquadResult.Ok && Squads.ValidName(name))
            sim.Squads.ById(squadId)!.Name = name.Trim();
    }

    /// <summary>
    /// Whether a formation can be set. <paramref name="custom"/> (custom only) is flattened slot pairs: forward, right,
    /// in cells, each within <see cref="LeashCells"/>.
    /// </summary>
    public static SquadResult CheckFormation(Simulation sim, int player, int squadId, Formation formation, int[]? custom)
    {
        var r = CheckSquad(sim, player, squadId);
        if (r != SquadResult.Ok)
            return r;
        if (!Enum.IsDefined(formation))
            return SquadResult.BadFormation;
        if (formation == Formation.Custom && (custom is not { Length: > 0 } || custom.Length % 2 != 0
            || custom.Length / 2 > Squads.MaxCustomSlots || custom.Any(v => Math.Abs(v) > LeashCells)))
            return SquadResult.BadFormation;
        return SquadResult.Ok;
    }

    /// <summary>Changes a squad's formation; a squad on its way somewhere re-forms and carries on (UC-003).</summary>
    internal static void SetFormation(Simulation sim, int player, int squadId, Formation formation, int[]? custom)
    {
        if (CheckFormation(sim, player, squadId, formation, custom) != SquadResult.Ok)
            return;
        var squad = sim.Squads.ById(squadId)!;
        squad.Formation = formation;
        squad.Custom = formation == Formation.Custom ? [.. Enumerable.Range(0, custom!.Length / 2).Select(i => (custom[2 * i], custom[2 * i + 1]))] : [];

        // Re-form members still carrying out the squad's order; finished or diverted members stay where they are.
        if (squad.Order is not { Kind: OrderKind.Move or OrderKind.Patrol or OrderKind.AttackMove or OrderKind.Attack } order)
            return;
        var units = squad.MemberList.Select(sim.UnitById).Where(u => u?.Order is { } o && o.Kind == order.Kind && o.TileId == order.TileId
            && o.X == order.X && o.Y == order.Y).Select(u => u!).ToList();
        if (units.Count > 0)
            MoveGroup(sim, units, order, squad.Formation, squad.Custom);
    }

    internal static void SetSquadStance(Simulation sim, int player, int squadId, Stance stance)
    {
        if (CheckSquad(sim, player, squadId) != SquadResult.Ok || !Enum.IsDefined(stance))
            return;
        var squad = sim.Squads.ById(squadId)!;
        squad.Stance = stance;
        SetStance(sim, player, squad.MemberList, stance);
    }

    internal static void SetSquadTargeting(Simulation sim, int player, int squadId, TargetMode mode)
    {
        if (CheckSquad(sim, player, squadId) != SquadResult.Ok || !Enum.IsDefined(mode))
            return;
        var squad = sim.Squads.ById(squadId)!;
        squad.Targeting = mode;
        SetTargeting(sim, player, squad.MemberList, mode);
    }

    // ---------------------------------------------------------------- factory rally (UC-006)

    /// <summary>
    /// Whether the player can set the rally point and auto-assign squad of the building at (x, y) of tile
    /// <paramref name="tileId"/>. A rally tile of -1 means none; a squad of 0 means none.
    /// </summary>
    public static RallyResult CheckRally(Simulation sim, int player, int tileId, int x, int y, int rallyTileId, int rallyX, int rallyY, int squadId)
    {
        if (sim.TryGetTile(tileId) is not { } tile)
            return RallyResult.NoSuchTile;
        if (tile.Grid.BuildingAt(x, y) is not { } b)
            return RallyResult.NoSuchBuilding;
        if (b.Owner != player && tile.Owner != player)
            return RallyResult.NotOwner;
        if (rallyTileId != -1 && (sim.TryGetTile(rallyTileId) is not { } rt || !rt.Grid.InBounds(rallyX, rallyY)))
            return RallyResult.OutOfBounds;
        if (squadId != 0 && CheckSquad(sim, player, squadId) != SquadResult.Ok)
            return RallyResult.NoSuchSquad;
        return RallyResult.Ok;
    }

    internal static void SetRally(Simulation sim, int player, int tileId, int x, int y, int rallyTileId, int rallyX, int rallyY, int squadId)
    {
        if (CheckRally(sim, player, tileId, x, y, rallyTileId, rallyX, rallyY, squadId) != RallyResult.Ok)
            return;
        var b = sim.Tiles[tileId].Grid.BuildingAt(x, y)!;
        if (rallyTileId == -1 && squadId == 0)
            sim.Squads.ClearRally(tileId, b.Id);
        else
            sim.Squads.SetRally(new Rally(tileId, b.Id, player, rallyTileId, rallyX, rallyY, squadId));
    }

    /// <summary>
    /// A factory finished a unit (UC-006): it joins the factory's squad and heads for its rally point. Called by unit
    /// production; today by lift-off, with the unit core as the factory (its setting is then used up).
    /// </summary>
    internal static void OnUnitBuilt(Simulation sim, Unit unit, int factoryTileId, int factoryBuildingId)
    {
        if (sim.Squads.RallyOf(factoryTileId, factoryBuildingId) is not { } rally || rally.Owner != unit.Owner)
            return;
        if (sim.Squads.ById(rally.SquadId) is { } squad && squad.Owner == unit.Owner)
            Join(sim, squad, unit);
        if (rally.RallyTileId >= 0)
            MoveGroup(sim, [unit], new UnitOrder(OrderKind.Move, rally.RallyTileId, rally.RallyX, rally.RallyY), Formation.Box, []);
        if (!sim.Tiles[factoryTileId].Grid.Buildings.Any(b => b.Id == factoryBuildingId))
            sim.Squads.ClearRally(factoryTileId, factoryBuildingId);
    }

    // ---------------------------------------------------------------- abstract travel (UC-009)

    /// <summary>
    /// The terrain-legal path from the unit to its current goal, in world sub-cells, ending at the goal itself; empty
    /// when it has no goal or can't get there. For abstract travel (UC-009), at <see cref="CruiseSpeed"/>.
    /// </summary>
    public static (long X, long Y)[] AbstractWaypoints(Simulation sim, Unit u)
    {
        if (u.Route == null)
            return [];
        var dest = sim.Tiles[u.DestTileId];
        int cx = (int)FloorDiv(u.DestX, S) - dest.OriginX, cy = (int)FloorDiv(u.DestY, S) - dest.OriginY;
        if (UnitNav.Plan(sim, u, dest.Id, cx, cy) is not { } plan)
            return [];
        var points = plan.Waypoints.Select(p => ((long)p.X * S + S / 2, (long)p.Y * S + S / 2)).ToList();
        points[^1] = (u.DestX, u.DestY);
        return [.. points];
    }

    /// <summary>Speed the unit flies at between waypoints, in sub-cells per tick (the controller aims for its top speed).</summary>
    public static int CruiseSpeed(Unit u) => u.MaxSpeed;
}
