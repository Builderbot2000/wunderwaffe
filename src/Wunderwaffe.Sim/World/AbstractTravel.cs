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
/// Abstract travel (UC-009, OQ-15): a unit on a tile running in rate mode is a world-map node with no physics. It
/// follows its order's route as a line of waypoints (border crossing points, then the destination) at its cruise speed,
/// keeping <see cref="Unit.X"/>, <see cref="Unit.Y"/> and <see cref="Unit.TileId"/> exact, and materialises at that
/// position, with that velocity, as soon as its tile runs the full sim again (viewed or Contested).
/// Waypoints and speed come from movement (<see cref="UnitSim.AbstractWaypoints"/>, <see cref="UnitSim.CruiseSpeed"/>).
/// </summary>
public static class AbstractTravel
{
    const int S = Unit.CellScale;

    /// <summary>How far abstract travel time may stray from the full sim's, in percent (UC-009); tests hold it to this.</summary>
    public const int TolerancePercent = 15;

    /// <summary>Cruise speed of an abstract unit, sub-cells per tick: what the flight controller flies at.</summary>
    public static int Speed(Unit u) => UnitSim.CruiseSpeed(u);

    /// <summary>Terrain-legal waypoints from the unit's position to its order's destination (movement's route).</summary>
    public static (long X, long Y)[] Plan(Simulation sim, Unit u) => UnitSim.AbstractWaypoints(sim, u);

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
            if (node.Next >= node.Waypoints.Count)
                u.Route = null;
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

    /// <summary>Turns units on rate-mode tiles into nodes and materialises those on full-sim tiles.</summary>
    internal static void Update(Simulation sim, bool[] full)
    {
        var nodes = sim.AbstractNodes;
        foreach (int id in nodes.Keys.Where(id => sim.UnitById(id) == null).ToList())
            nodes.Remove(id);
        foreach (var u in sim.Units)
        {
            bool isNode = nodes.TryGetValue(u.Id, out var node);
            if (!full[u.TileId] && !isNode)
            {
                nodes[u.Id] = new AbstractNode(Plan(sim, u), u.Route);
                (u.VX, u.VY, u.AngVel) = (0, 0, 0);
            }
            else if (full[u.TileId] && isNode)
            {
                nodes.Remove(u.Id);
                Materialise(u, node!);
            }
        }
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
