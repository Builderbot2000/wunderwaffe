using Wunderwaffe.Sim.Content;
using static Wunderwaffe.Sim.Core.IntMath;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// Unit flight, one tick at a time. A controller steers toward the order's next waypoint: it picks the velocity it
/// wants, turns the strongest engine toward the acceleration it needs, and fires each engine in proportion to how well
/// it lines up. Then drag and integration, finding the tile under the unit, and collisions with buildings and other
/// units. Positions are in world sub-cells, so crossing a border is just flying over it. Everything is integer, and
/// units are visited in id order.
/// </summary>
internal static class UnitPhysics
{
    const int S = Unit.CellScale;

    /// <summary>Collision radius of one cell: a little under half a cell, so neighbouring cells overlap and a body has no gaps.</summary>
    const long CellRadius = S * 45L / 100;

    /// <summary>A unit has arrived when it is this close to its target and slower than <see cref="ArriveSpeed"/>.</summary>
    const long ArriveRadius = S / 2;
    const long ArriveSpeed = S / 100;

    /// <summary>Velocity errors smaller than this are left alone, so units don't jitter at their target.</summary>
    const long DeadBand = S / 1500;

    /// <summary>The wanted speed near a target is its distance divided by this.</summary>
    const long BrakeDiv = 12;

    const int MaxTurn = 700;
    const int TurnGainDiv = 8;
    const int One = Trig.One;

    public static void Step(Simulation sim)
    {
        UnitSim.Step(sim);
        foreach (var u in sim.Units)
        {
            int px = u.X, py = u.Y;
            Control(sim, u);
            UpdateTile(sim, u, px, py);
            CollideBuildings(sim, u);
        }
        CollideUnits(sim);
    }

    static void Control(Simulation sim, Unit u)
    {
        long ax = 0, ay = 0;
        bool steering = false;

        if (u.Route != null && u.MaxSpeed > 0)
        {
            var tile = sim.Tiles[u.TileId];
            bool crossing = u.RouteIndex < u.Route.Length;
            var next = crossing ? sim.Tiles[u.Route[u.RouteIndex]] : null;
            long tx = u.DestX, ty = u.DestY;
            if (next != null)
            {
                // A cell of the next tile, just past the border, so the unit is still flying when it crosses.
                var (cx, cy) = Portal(sim, u, tile, next);
                (tx, ty) = ((long)cx * S + S / 2, (long)cy * S + S / 2);
            }
            bool through = FollowPath(sim, u, tile, next, ref tx, ref ty);

            long dx = tx - u.X, dy = ty - u.Y;
            long dist = ISqrt(dx * dx + dy * dy);
            long speed = ISqrt((long)u.VX * u.VX + (long)u.VY * u.VY);
            if (!crossing && !through && dist < ArriveRadius && speed < ArriveSpeed)
            {
                u.Route = null;
                UnitSim.Arrived(sim, u);
            }
            else if (dist > 0)
            {
                long want = crossing || through ? u.MaxSpeed : Math.Min(u.MaxSpeed, dist / BrakeDiv);
                long ex = dx * want / dist - u.VX, ey = dy * want / dist - u.VY;
                long emag = ISqrt(ex * ex + ey * ey);
                if (emag > DeadBand)
                {
                    steering = true;
                    TurnToward(u, Trig.Atan2(ey, ex) - u.MainDir);
                    (ax, ay) = Thrust(u, ex, ey, emag);
                }
            }
        }

        if (!steering)
            u.AngVel = u.AngVel * 7 / 8;
        u.Angle = (u.Angle + u.AngVel) & (Trig.Turn - 1);

        u.VX = (int)((u.VX + ax) * (Unit.DragDiv - 1) / Unit.DragDiv);
        u.VY = (int)((u.VY + ay) * (Unit.DragDiv - 1) / Unit.DragDiv);
        u.X += u.VX;
        u.Y += u.VY;
    }

    /// <summary>
    /// Where the unit crosses from <paramref name="tile"/> into <paramref name="next"/>: the cell of the next tile, along
    /// their shared border, in the terrain region its route enters (so its locomotion can cross there, IO-003), that
    /// makes the shortest trip from the unit to its destination. Chosen once per border.
    /// </summary>
    static (int X, int Y) Portal(Simulation sim, Unit u, TileState tile, TileState next)
    {
        if (u.Portal is { } p && p.Tile == tile.Id && p.Next == next.Id)
            return (p.X, p.Y);

        var g = UnitNav.Regions(sim, u.Locomotion, UnitPath.Clearance(u));
        int have = u.RouteRegions?[u.RouteIndex] ?? -1, want = u.RouteRegions?[u.RouteIndex + 1] ?? -1;
        var best = UnitNav.Portal(sim, g, tile, next, have, want, u.X, u.Y, u.DestX, u.DestY);
        u.Portal = (tile.Id, next.Id, best.X, best.Y);
        return best;
    }

    /// <summary>
    /// Keeps the unit on a path around buildings. Plans one over the tile, and the next tile when crossing, to the
    /// target cell and, while waypoints remain, sets the target to the next one and returns true so the controller flies
    /// through it without braking. With no path to follow it leaves the target alone, and the unit heads straight and
    /// slides along whatever is in the way.
    /// </summary>
    static bool FollowPath(Simulation sim, Unit u, TileState tile, TileState? next, ref long tx, ref long ty)
    {
        var win = UnitPath.Window(tile, next);
        int goalX = Math.Clamp((int)(tx / S), win.X, win.X + win.W - 1);
        int goalY = Math.Clamp((int)(ty / S), win.Y, win.Y + win.H - 1);

        long revision = tile.Grid.Revision + (next?.Grid.Revision ?? 0);
        var key = (tile.Id, next?.Id ?? -1, revision, goalX, goalY);
        if (u.PathKey != key)
        {
            u.PathKey = key;
            u.PathIndex = 0;
            var blocked = UnitPath.Blocked(sim, win, tile, next, UnitPath.Clearance(u), u.Locomotion);
            var start = (Math.Clamp(u.X / S, win.X, win.X + win.W - 1) - win.X, Math.Clamp(u.Y / S, win.Y, win.Y + win.H - 1) - win.Y);
            u.Path = UnitPath.Find(win.W, win.H, blocked, start, (goalX - win.X, goalY - win.Y))
                ?.Select(c => (c.X + win.X, c.Y + win.Y)).ToArray();
        }
        if (u.Path is not { } path)
            return false;

        while (u.PathIndex < path.Length - 1 && Near(u, path[u.PathIndex]))
            u.PathIndex++;
        if (u.PathIndex < path.Length - 1)
        {
            (tx, ty) = ((long)path[u.PathIndex].X * S + S / 2, (long)path[u.PathIndex].Y * S + S / 2);
            return true;
        }

        // Last waypoint: it was a free cell near the goal. Head for its centre if the goal itself was in the way.
        var last = path[^1];
        if (next == null && (last.X != goalX || last.Y != goalY))
            (tx, ty) = ((long)last.X * S + S / 2, (long)last.Y * S + S / 2);
        return false;
    }

    static bool Near(Unit u, (int X, int Y) cell)
    {
        long dx = (long)cell.X * S + S / 2 - u.X, dy = (long)cell.Y * S + S / 2 - u.Y;
        return dx * dx + dy * dy < (long)S * S;
    }

    /// <summary>Accelerates the angular velocity toward the rate that closes the heading error, within the engines' torque.</summary>
    static void TurnToward(Unit u, int heading)
    {
        int err = Trig.Diff(heading, u.Angle);
        int want = Math.Clamp(err / TurnGainDiv, -MaxTurn, MaxTurn);
        int alpha = u.TurnAccel;
        u.AngVel += Math.Clamp(want - u.AngVel, -alpha, alpha);
    }

    /// <summary>Fires every engine in proportion to how well it points along the wanted acceleration; returns the acceleration.</summary>
    static (long X, long Y) Thrust(Unit u, long ex, long ey, long emag)
    {
        long exu = ex * One / emag, eyu = ey * One / emag;
        long fx = 0, fy = 0;
        foreach (var t in u.Thrusters)
        {
            int dir = u.Angle + t.Dir;
            long c = Trig.Cos(dir), s = Trig.Sin(dir);
            long dot = (c * exu + s * eyu) / One;
            if (dot <= 0)
                continue;
            long f = t.Thrust * dot / One;
            fx += f * c / One;
            fy += f * s / One;
        }

        long ax = fx * Unit.ThrustScale / u.Mass, ay = fy * Unit.ThrustScale / u.Mass;
        // Never ask for more than closes the error this tick, which would only overshoot.
        long amag = ISqrt(ax * ax + ay * ay);
        if (amag > emag)
        {
            ax = ax * emag / amag;
            ay = ay * emag / amag;
        }
        return (ax, ay);
    }

    /// <summary>
    /// The unit's tile is the tile of the cell under its centre. When that changes, the order moves on to the next
    /// tile of its route, or plans a new route if the unit drifted somewhere unplanned. At the edge of the world, or of
    /// a gap between hand-built tiles, it stops against the border instead.
    /// </summary>
    static void UpdateTile(Simulation sim, Unit u, int prevX, int prevY)
    {
        var map = sim.Map;
        long w = (long)map.Width * S, h = (long)map.Height * S;
        int rx = (int)Math.Min(u.Radius, w / 2), ry = (int)Math.Min(u.Radius, h / 2);
        if (u.X < 0 || u.X >= w)
        {
            u.X = u.X < 0 ? rx : (int)w - rx;
            u.VX = 0;
        }
        if (u.Y < 0 || u.Y >= h)
        {
            u.Y = u.Y < 0 ? ry : (int)h - ry;
            u.VY = 0;
        }

        int id = map.TileAtCell(u.X / S, u.Y / S);
        if (id < 0)
        {
            if (map.TileAtCell(prevX / S, u.Y / S) >= 0)
            {
                u.X = prevX;
                u.VX = 0;
            }
            else if (map.TileAtCell(u.X / S, prevY / S) >= 0)
            {
                u.Y = prevY;
                u.VY = 0;
            }
            else
            {
                (u.X, u.Y, u.VX, u.VY) = (prevX, prevY, 0, 0);
            }
            id = map.TileAtCell(u.X / S, u.Y / S);
        }
        if (id < 0 || id == u.TileId)
            return;

        u.TileId = id;
        u.Portal = null;
        if (u.Route == null)
            return;
        if (u.RouteIndex < u.Route.Length && u.Route[u.RouteIndex] == id)
        {
            u.RouteIndex++;
        }
        else
        {
            // Drifted somewhere the order didn't plan: plan again from here.
            UnitSim.Replan(sim, u);
        }
    }

    /// <summary>
    /// Pushes a unit out of any solid building its cells overlap, on any tile, and out of terrain its locomotion can't
    /// cross (UD-004), and cancels the velocity into it. A unit whose centre already stands on such terrain (a boat
    /// built on land) isn't held by terrain, so it can get off.
    /// </summary>
    static void CollideBuildings(Simulation sim, Unit u)
    {
        bool stranded = !Locomotion.CanCross(sim, u.Locomotion, (int)FloorDiv(u.X, S), (int)FloorDiv(u.Y, S));
        foreach (var cell in u.Cells)
        {
            var (cx0, cy0) = u.CellWorld(cell);
            long gx = FloorDiv(cx0, S), gy = FloorDiv(cy0, S);
            for (long ny = gy - 1; ny <= gy + 1; ny++)
            {
                for (long nx = gx - 1; nx <= gx + 1; nx++)
                {
                    bool solid = sim.TileAtCell((int)nx, (int)ny) is { } t
                        && t.Grid.BuildingAt((int)nx - t.OriginX, (int)ny - t.OriginY) is { Kind: not BuildingKind.Conveyor };
                    if (!solid && (stranded || Locomotion.CanCross(sim, u.Locomotion, (int)nx, (int)ny)))
                        continue;

                    var (cx, cy) = u.CellWorld(cell);
                    long minX = nx * S, minY = ny * S;
                    long dx = cx - Math.Clamp(cx, minX, minX + S), dy = cy - Math.Clamp(cy, minY, minY + S);
                    long d2 = dx * dx + dy * dy;
                    if (d2 >= CellRadius * CellRadius)
                        continue;

                    long nxn, nyn, pen;
                    if (d2 == 0)
                    {
                        // Centre inside the cell: leave by the nearest side.
                        long l = cx - minX, r = minX + S - cx, t2 = cy - minY, bt = minY + S - cy;
                        long m = Math.Min(Math.Min(l, r), Math.Min(t2, bt));
                        (nxn, nyn) = m == l ? (-One, 0) : m == r ? (One, 0) : m == t2 ? (0, -One) : (0, One);
                        pen = m + CellRadius;
                    }
                    else
                    {
                        long dist = ISqrt(d2);
                        (nxn, nyn) = (dx * One / dist, dy * One / dist);
                        pen = CellRadius - dist;
                    }
                    u.X += (int)(nxn * pen / One);
                    u.Y += (int)(nyn * pen / One);
                    CancelApproach(u, nxn, nyn);
                }
            }
        }
    }

    static void CancelApproach(Unit u, long nx, long ny)
    {
        long vn = ((long)u.VX * nx + (long)u.VY * ny) / One;
        if (vn >= 0)
            return;
        u.VX -= (int)(nx * vn / One);
        u.VY -= (int)(ny * vn / One);
    }

    /// <summary>
    /// Pushes overlapping units apart, in proportion to mass, and makes the collision inelastic. Positions are global,
    /// so units on either side of a border collide too.
    /// </summary>
    static void CollideUnits(Simulation sim)
    {
        var units = sim.Units;
        for (int i = 0; i < units.Count; i++)
        {
            for (int j = i + 1; j < units.Count; j++)
            {
                Unit a = units[i], b = units[j];
                long far = (long)a.Radius + b.Radius;
                long ddx = a.X - (long)b.X, ddy = a.Y - (long)b.Y;
                if (ddx * ddx + ddy * ddy >= far * far)
                    continue;

                foreach (var ca in a.Cells)
                {
                    foreach (var cb in b.Cells)
                        Separate(a, ca, b, cb);
                }
            }
        }
    }

    static void Separate(Unit a, UnitCell ca, Unit b, UnitCell cb)
    {
        var (ax, ay) = a.CellWorld(ca);
        var (bx, by) = b.CellWorld(cb);
        long dx = bx - ax, dy = by - ay;
        long d2 = dx * dx + dy * dy, reach = 2 * CellRadius;
        if (d2 >= reach * reach)
            return;

        long dist = ISqrt(d2);
        long nx, ny;
        if (dist == 0)
            (nx, ny, dist) = (One, 0, 0);
        else
            (nx, ny) = (dx * One / dist, dy * One / dist);

        long pen = reach - dist, total = a.Mass + b.Mass;
        long moveA = pen * b.Mass / total, moveB = pen * a.Mass / total;
        a.X -= (int)(nx * moveA / One);
        a.Y -= (int)(ny * moveA / One);
        b.X += (int)(nx * moveB / One);
        b.Y += (int)(ny * moveB / One);

        // Relative velocity along the normal; if they are closing, hand them a common one (no bounce).
        long vn = ((b.VX - (long)a.VX) * nx + (b.VY - (long)a.VY) * ny) / One;
        if (vn >= 0)
            return;
        long ka = vn * b.Mass / total, kb = vn * a.Mass / total;
        a.VX += (int)(nx * ka / One);
        a.VY += (int)(ny * ka / One);
        b.VX -= (int)(nx * kb / One);
        b.VY -= (int)(ny * kb / One);
    }

}
