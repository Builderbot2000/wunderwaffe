using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// Unit flight, one tick at a time. A controller steers toward the order's next waypoint: it picks the velocity it
/// wants, turns the strongest engine toward the acceleration it needs, and fires each engine in proportion to how well
/// it lines up. Then drag and integration, tile crossing, and collisions with buildings and other units. Everything is
/// integer, and units are visited in id order.
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
        foreach (var u in sim.Units)
        {
            Control(sim, u);
            Wrap(sim, u);
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
            long w = (long)tile.Grid.Width * S, h = (long)tile.Grid.Height * S;
            bool crossing = u.RouteIndex < u.Route.Length;
            long tx = u.DestX, ty = u.DestY;
            var side = crossing ? SideTo(tile, sim.Tiles[u.Route[u.RouteIndex]]) : default;
            bool through = FollowPath(u, tile, crossing, side, ref tx, ref ty);
            if (crossing && !through)
            {
                // Aim a cell past the edge so the unit is still flying when it crosses.
                (tx, ty) = side switch
                {
                    Direction.East => (w + S, (long)u.Y),
                    Direction.West => (-(long)S, (long)u.Y),
                    Direction.South => ((long)u.X, h + S),
                    _ => ((long)u.X, -(long)S),
                };
            }

            long dx = tx - u.X, dy = ty - u.Y;
            long dist = ISqrt(dx * dx + dy * dy);
            long speed = ISqrt((long)u.VX * u.VX + (long)u.VY * u.VY);
            if (!crossing && !through && dist < ArriveRadius && speed < ArriveSpeed)
            {
                u.Route = null;
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
    /// Keeps the unit on a path around buildings. Plans one across the tile to where it is heading (the dest cell, or the
    /// edge cell it will cross at) and, while waypoints remain, sets the target to the next one and returns true so the
    /// controller flies through it without braking. With no path to follow it leaves the target alone, and the unit
    /// heads straight and slides along whatever is in the way.
    /// </summary>
    static bool FollowPath(Unit u, TileState tile, bool crossing, Direction side, ref long tx, ref long ty)
    {
        var grid = tile.Grid;
        int goalX, goalY;
        if (crossing)
        {
            // The edge cell on the unit's own lane, so the crossing is as straight as the buildings allow.
            int lx = Math.Clamp(u.X / S, 0, grid.Width - 1), ly = Math.Clamp(u.Y / S, 0, grid.Height - 1);
            (goalX, goalY) = side switch
            {
                Direction.East => (grid.Width - 1, ly),
                Direction.West => (0, ly),
                Direction.South => (lx, grid.Height - 1),
                _ => (lx, 0),
            };
        }
        else
        {
            goalX = Math.Clamp(u.DestX / S, 0, grid.Width - 1);
            goalY = Math.Clamp(u.DestY / S, 0, grid.Height - 1);
        }

        var key = (tile.Id, grid.Revision, goalX, goalY);
        if (u.PathKey != key)
        {
            u.PathKey = key;
            u.PathIndex = 0;
            var blocked = UnitPath.Blocked(grid, UnitPath.Clearance(u));
            var start = (Math.Clamp(u.X / S, 0, grid.Width - 1), Math.Clamp(u.Y / S, 0, grid.Height - 1));
            u.Path = UnitPath.Find(grid, blocked, start, (goalX, goalY));
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
        if (!crossing && (last.X != goalX || last.Y != goalY))
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
    /// Moves a unit onto the neighbour tile when its centre crosses an edge, keeping its order on course. At the edge
    /// of the world it stops against the border instead.
    /// </summary>
    static void Wrap(Simulation sim, Unit u)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            var tile = sim.Tiles[u.TileId];
            long w = (long)tile.Grid.Width * S, h = (long)tile.Grid.Height * S;
            Direction side;
            if (u.X < 0) side = Direction.West;
            else if (u.X >= w) side = Direction.East;
            else if (u.Y < 0) side = Direction.North;
            else if (u.Y >= h) side = Direction.South;
            else return;

            if (sim.Neighbor(tile, side) is not { } next)
            {
                int rx = (int)Math.Min(u.Radius, w / 2), ry = (int)Math.Min(u.Radius, h / 2);
                if (side is Direction.West or Direction.East)
                {
                    u.X = side == Direction.West ? rx : (int)w - rx;
                    u.VX = 0;
                }
                else
                {
                    u.Y = side == Direction.North ? ry : (int)h - ry;
                    u.VY = 0;
                }
                continue;
            }

            long nw = (long)next.Grid.Width * S, nh = (long)next.Grid.Height * S;
            switch (side)
            {
                case Direction.East: u.X -= (int)w; break;
                case Direction.West: u.X += (int)nw; break;
                case Direction.South: u.Y -= (int)h; break;
                default: u.Y += (int)nh; break;
            }
            u.X = (int)Math.Clamp(u.X, 0, nw - 1);
            u.Y = (int)Math.Clamp(u.Y, 0, nh - 1);
            u.TileId = next.Id;

            if (u.Route == null)
                continue;
            if (u.RouteIndex < u.Route.Length && u.Route[u.RouteIndex] == next.Id)
            {
                u.RouteIndex++;
            }
            else
            {
                // Drifted somewhere the order didn't plan: plan again from here.
                u.Route = UnitSim.FindRoute(sim, next, sim.Tiles[u.DestTileId]);
                u.RouteIndex = 0;
            }
        }
    }

    /// <summary>Pushes a unit out of any solid building its cells overlap and cancels the velocity into it.</summary>
    static void CollideBuildings(Simulation sim, Unit u)
    {
        var grid = sim.Tiles[u.TileId].Grid;
        foreach (var cell in u.Cells)
        {
            var (cx0, cy0) = u.CellWorld(cell);
            long gx = FloorDiv(cx0, S), gy = FloorDiv(cy0, S);
            for (long ny = gy - 1; ny <= gy + 1; ny++)
            {
                for (long nx = gx - 1; nx <= gx + 1; nx++)
                {
                    if (!grid.InBounds((int)nx, (int)ny) || grid.BuildingAt((int)nx, (int)ny) is not { } b || b.Kind == BuildingKind.Conveyor)
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
                        long l = cx - minX, r = minX + S - cx, t = cy - minY, bt = minY + S - cy;
                        long m = Math.Min(Math.Min(l, r), Math.Min(t, bt));
                        (nxn, nyn) = m == l ? (-One, 0) : m == r ? (One, 0) : m == t ? (0, -One) : (0, One);
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

    /// <summary>Pushes overlapping units on a tile apart, in proportion to mass, and makes the collision inelastic.</summary>
    static void CollideUnits(Simulation sim)
    {
        var units = sim.Units;
        for (int i = 0; i < units.Count; i++)
        {
            for (int j = i + 1; j < units.Count; j++)
            {
                Unit a = units[i], b = units[j];
                if (a.TileId != b.TileId)
                    continue;
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

    static Direction SideTo(TileState from, TileState to)
    {
        foreach (var d in Directions.All)
        {
            if (from.X + d.Dx() == to.X && from.Y + d.Dy() == to.Y)
                return d;
        }
        throw new InvalidOperationException($"tiles {from.Id} and {to.Id} are not adjacent");
    }

    static long FloorDiv(long a, long b) => a >= 0 ? a / b : -((-a + b - 1) / b);

    internal static long ISqrt(long n)
    {
        if (n < 2)
            return n;
        long x = n, y = (x + 1) / 2;
        while (y < x)
        {
            x = y;
            y = (x + n / x) / 2;
        }
        return x;
    }
}
