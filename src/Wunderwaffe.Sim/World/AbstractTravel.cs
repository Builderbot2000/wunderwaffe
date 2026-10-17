using static Wunderwaffe.Sim.Core.IntMath;

namespace Wunderwaffe.Sim.World;

/// <summary>An abstract unit's travel plan: points in world sub-cells, and the one it is heading for.</summary>
public sealed class AbstractNode((long X, long Y)[] waypoints, int[]? route)
{
    public IReadOnlyList<(long X, long Y)> Waypoints { get; internal set; } = waypoints;
    public int Next { get; internal set; }

    /// <summary>The order the waypoints were planned for; a new move order (a new route array) replans them.</summary>
    internal int[]? PlannedFor { get; set; } = route;
}

/// <summary>
/// Abstract travel (UC-005, UC-009, OQ-15, OQ-36): only viewed and Contested tiles are rendered (full sim); a unit
/// anywhere else is a world-map node with no physics. A unit flying physically turns into a node the moment it
/// crosses the rendered tile's border (<see cref="Crossed"/>, called from unit physics), with no stop or wait; a unit
/// ordered on a tile that isn't rendered is a node from the start. A node follows its order's route as a line of
/// waypoints (border crossing points, then the destination) at its cruise speed, keeping <see cref="Unit.X"/>,
/// <see cref="Unit.Y"/> and <see cref="Unit.TileId"/> exact, and materialises at that position, keeping its order, as
/// soon as it is on a rendered tile: one its route passes through, or one turned Contested by enemy units. Meeting enemy
/// units interrupts travel: by its stance (<see cref="UnitSim.StopsToFight"/>) the unit keeps its course or halts to
/// fight, and resumes its order once no enemy unit is left on its tile. A node that has stopped stays one, exactly
/// where it is, until its tile is opened. Waypoints and speed come from movement
/// (<see cref="UnitSim.AbstractWaypoints"/>, <see cref="UnitSim.CruiseSpeed"/>).
/// </summary>
public static class AbstractTravel
{
    const int S = Unit.CellScale;

    /// <summary>How far abstract travel time may stray from the full sim's, in percent (UC-009); tests hold it to this.</summary>
    public const int TolerancePercent = 15;

    /// <summary>Cruise speed of an abstract unit, sub-cells per tick: what the flight controller flies at.</summary>
    public static int Speed(Unit u) => UnitSim.CruiseSpeed(u);

    /// <summary>
    /// Terrain-legal waypoints from the unit's position to its order's destination (movement's route). If movement finds
    /// none from here (a start cell outside every region at the unit's clearance, say), the straight line to the
    /// destination, so a node never stops at the border it has just crossed.
    /// </summary>
    public static (long X, long Y)[] Plan(Simulation sim, Unit u)
    {
        var points = UnitSim.AbstractWaypoints(sim, u);
        return points.Length > 0 || u.Route == null ? points : [(u.DestX, u.DestY)];
    }

    /// <summary>Ticks an abstract unit takes to finish its order from where it is; -1 if it can't move or has no order.</summary>
    public static long Eta(Simulation sim, Unit u)
    {
        if (u.Route == null || Speed(u) <= 0)
            return -1;
        long length = 0, x = u.X, y = u.Y;
        foreach (var (px, py) in Plan(sim, u))
        {
            length += Dist(x, y, px, py);
            (x, y) = (px, py);
        }
        return (length + Speed(u) - 1) / Speed(u);
    }

    /// <summary>True while the unit is halted on its way, fighting the enemy units it met (UC-009).</summary>
    public static bool IsInterrupted(Simulation sim, Unit u) => sim.InterruptedUnits.ContainsKey(u.Id);

    static long Dist(long ax, long ay, long bx, long by) => ISqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));

    /// <summary>Moves every abstract unit along its waypoints, in id order.</summary>
    internal static void Step(Simulation sim)
    {
        foreach (var (id, node) in sim.AbstractNodes)
        {
            if (sim.UnitById(id) is not { } u || u.Route == null)
                continue;
            if (!ReferenceEquals(node.PlannedFor, u.Route))
                Replan(sim, u, node);
            long left = Speed(u);
            long x = u.X, y = u.Y;
            while (left > 0 && node.Next < node.Waypoints.Count)
            {
                var (px, py) = node.Waypoints[node.Next];
                long d = Dist(x, y, px, py);
                if (d <= left)
                {
                    (x, y) = (px, py);
                    left -= d;
                    node.Next++;
                }
                else
                {
                    x += (px - x) * left / d;
                    y += (py - y) * left / d;
                    left = 0;
                }
            }
            (u.X, u.Y) = ((int)x, (int)y);
            UpdateTile(sim, u, node);
            if (node.Next >= node.Waypoints.Count && u.Route != null)
            {
                // As the physics step: moves end, patrols turn round (a new route, replanned next tick).
                u.Route = null;
                UnitSim.Arrived(sim, u);
            }
        }
    }

    /// <summary>Follows the unit across borders as physics does, replanning if the straight line clipped another tile.</summary>
    static void UpdateTile(Simulation sim, Unit u, AbstractNode node)
    {
        int id = sim.Map.TileAtCell(u.X / S, u.Y / S);
        if (id < 0 || id == u.TileId)
            return;
        u.TileId = id;
        if (u.RouteIndex < u.Route!.Length && u.Route[u.RouteIndex] == id)
        {
            u.RouteIndex++;
            return;
        }
        UnitSim.Replan(sim, u);
        Replan(sim, u, node);
    }

    static void Replan(Simulation sim, Unit u, AbstractNode node)
    {
        node.Waypoints = Plan(sim, u);
        node.Next = 0;
        node.PlannedFor = u.Route;
    }

    /// <summary>
    /// Border hook from unit physics, after it moved the unit onto another tile: if that tile isn't rendered, the unit
    /// becomes a node there and then, mid-flight. Returns true if it did.
    /// </summary>
    internal static bool Crossed(Simulation sim, Unit u)
    {
        if (!sim.RateModeEnabled || RateMode.IsFullSim(sim, sim.Tiles[u.TileId]))
            return false;
        Enter(sim, u);
        return true;
    }

    static void Enter(Simulation sim, Unit u)
    {
        sim.AbstractNodes[u.Id] = new AbstractNode(Plan(sim, u), u.Route);
        (u.VX, u.VY, u.AngVel) = (0, 0, 0);
    }

    /// <summary>
    /// End of tick, once the tile modes for the next one are chosen: resumes interrupted units whose fight is over, turns
    /// units on tiles that aren't rendered into nodes and materialises nodes on rendered ones. A travelling node that
    /// materialises among enemy units on a Contested tile is interrupted if its stance stops to fight.
    /// </summary>
    internal static void Update(Simulation sim, bool[] full, bool[] contested)
    {
        var nodes = sim.AbstractNodes;
        foreach (int id in nodes.Keys.Where(id => sim.UnitById(id) == null).ToList())
            nodes.Remove(id);
        UpdateInterrupted(sim);
        foreach (var u in sim.Units)
        {
            bool isNode = nodes.TryGetValue(u.Id, out var node);
            if (!full[u.TileId] && !isNode)
            {
                Enter(sim, u);
            }
            else if (full[u.TileId] && isNode)
            {
                nodes.Remove(u.Id);
                Materialise(u, node!);
                if (contested[u.TileId] && u.Route != null && EnemyUnitOn(sim, u) && UnitSim.StopsToFight(u))
                    Interrupt(sim, u);
            }
        }
    }

    /// <summary>Halts a travelling unit where it is to fight; its order stays, to be resumed by <see cref="UpdateInterrupted"/>.</summary>
    static void Interrupt(Simulation sim, Unit u)
    {
        UnitSim.Halt(u);
        (u.VX, u.VY) = (0, 0);
        sim.InterruptedUnits[u.Id] = u.Order;
    }

    /// <summary>
    /// Interrupted units carry on with their order once no enemy unit is left on their tile. A new order from the
    /// player, or anything else that has already set them on their way again, ends the interruption.
    /// </summary>
    static void UpdateInterrupted(Simulation sim)
    {
        var interrupted = sim.InterruptedUnits;
        if (interrupted.Count == 0)
            return;
        foreach (var (id, order) in interrupted.ToList())
        {
            if (sim.UnitById(id) is not { } u || !ReferenceEquals(u.Order, order) || (u.Route != null && !u.Chasing))
            {
                interrupted.Remove(id);
            }
            else if (!EnemyUnitOn(sim, u))
            {
                interrupted.Remove(id);
                if (u.Route == null)
                    UnitSim.Resume(sim, u);
            }
        }
    }

    /// <summary>Whether another player's unit is on the unit's tile.</summary>
    static bool EnemyUnitOn(Simulation sim, Unit u)
    {
        foreach (var o in sim.Units)
        {
            if (o.TileId == u.TileId && o.Owner != u.Owner)
                return true;
        }
        return false;
    }

    /// <summary>Back to physics, at the node's position, flying toward its next waypoint at cruise speed.</summary>
    static void Materialise(Unit u, AbstractNode node)
    {
        u.Path = null;
        u.PathKey = null;
        u.Portal = null;
        if (u.Route == null || node.Next >= node.Waypoints.Count)
            return;
        var (px, py) = node.Waypoints[node.Next];
        long d = Dist(u.X, u.Y, px, py);
        if (d > 0)
            (u.VX, u.VY) = ((int)((px - u.X) * Speed(u) / d), (int)((py - u.Y) * Speed(u) / d));
    }
}
