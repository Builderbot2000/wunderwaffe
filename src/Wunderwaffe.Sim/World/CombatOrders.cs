namespace Wunderwaffe.Sim.World;

/// <summary>Combat order kinds. Movement's <c>OrderKind.Attack</c>/<c>AttackMove</c> replace these at merge.</summary>
internal enum CombatOrderKind
{
    /// <summary>Destroy one target: a unit (<see cref="CombatOrder.TargetId"/>) or the building on a cell.</summary>
    Attack,

    /// <summary>Move to a cell, stopping to fight anything in weapon range on the way.</summary>
    AttackMove,
}

/// <summary>
/// An attack or attack-move order. <see cref="X"/>/<see cref="Y"/> is a cell in <see cref="TileId"/>'s own coordinates:
/// the destination, or the building to attack when <see cref="TargetId"/> is -1.
/// </summary>
internal sealed record CombatOrder(CombatOrderKind Kind, int TileId, int X, int Y, int TargetId = -1);

/// <summary>
/// The seam between combat and unit movement (silo E). Movement owns orders, stances and steering; combat decides
/// what to shoot and asks to chase it through here. Until movement's <c>UnitSim.Issue</c>/<c>MayChase</c>/<c>Chase</c>/
/// <c>Resume</c>/<c>CanFire</c> and <c>Unit.Order</c>/<c>Stance</c> are merged, this keeps the order on
/// <see cref="Unit.AttackOrder"/> and moves units with plain move orders. At merge each method becomes a one-line
/// call into <c>UnitSim</c>, and <see cref="Unit.AttackOrder"/> goes.
/// </summary>
internal static class CombatOrders
{
    public static MoveResult Issue(Simulation sim, int player, IReadOnlyList<int> unitIds, CombatOrderKind kind,
        int tileId, int cellX, int cellY, int targetId = -1)
    {
        var result = UnitSim.Order(sim, player, unitIds, tileId, cellX, cellY);
        if (result != MoveResult.Ok)
            return result;
        var order = new CombatOrder(kind, tileId, cellX, cellY, targetId);
        foreach (int id in unitIds)
        {
            if (sim.UnitById(id) is { } u && u.Owner == player)
                u.AttackOrder = order;
        }
        return result;
    }

    public static CombatOrder? OrderOf(Unit u) => u.AttackOrder;

    /// <summary>Whether stance and orders let the unit leave its post to chase a point. No stances yet: always.</summary>
    public static bool MayChase(Simulation sim, Unit u, long worldX, long worldY) => true;

    /// <summary>Whether stance and orders let the unit fire. No stances yet: always.</summary>
    public static bool CanFire(Unit u) => true;

    /// <summary>Flies toward a point without dropping the standing order.</summary>
    public static void Chase(Simulation sim, Unit u, long worldX, long worldY)
    {
        int gx = (int)Core.IntMath.FloorDiv(worldX, Unit.CellScale), gy = (int)Core.IntMath.FloorDiv(worldY, Unit.CellScale);
        if (sim.TileAtCell(gx, gy) is not { } tile)
            return;
        var keep = u.AttackOrder;
        UnitSim.Order(sim, u.Owner, [u.Id], tile.Id, gx - tile.OriginX, gy - tile.OriginY);
        u.AttackOrder = keep;
    }

    /// <summary>Stops where it is, keeping the order: in range of what it is fighting.</summary>
    public static void Halt(Unit u) => u.Route = null;

    /// <summary>Goes back to the standing order: on to an attack-move's destination, or idle once an attack is over.</summary>
    public static void Resume(Simulation sim, Unit u)
    {
        if (u.AttackOrder is { Kind: CombatOrderKind.AttackMove } o)
        {
            UnitSim.Order(sim, u.Owner, [u.Id], o.TileId, o.X, o.Y);
            u.AttackOrder = o;
            return;
        }
        u.AttackOrder = null;
        u.Route = null;
    }

    /// <summary>Ends the order (it was completed or replaced by a plain move).</summary>
    public static void Clear(Unit u) => u.AttackOrder = null;
}
