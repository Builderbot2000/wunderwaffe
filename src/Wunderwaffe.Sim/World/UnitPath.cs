using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// Path-finding inside one tile for units. Buildings (except conveyors) are obstacles, grown by the unit's size so the
/// path keeps its whole body clear. A* over the cell grid, then the path is straightened wherever there is a clear line,
/// so units fly in long straight runs instead of hugging cell corners. Integer costs and a fixed tie-break make it
/// deterministic (NF-004).
/// </summary>
internal static class UnitPath
{
    const int Straight = 10, Diagonal = 14;

    /// <summary>How far, in cells, a goal inside an obstacle is moved to find free ground.</summary>
    const int GoalSearch = 8;

    /// <summary>How many cells out from a building the unit's centre must stay: its radius, less the half cell its own cell centre sits in.</summary>
    public static int Clearance(Unit u) => Math.Max(0, (u.Radius + Unit.CellScale / 2 + Unit.CellScale - 1) / Unit.CellScale - 1);

    /// <summary>Cells a unit with the given clearance can't put its centre in.</summary>
    public static bool[] Blocked(TileGrid grid, int clearance)
    {
        int w = grid.Width, h = grid.Height;
        var blocked = new bool[w * h];
        foreach (var b in grid.Buildings)
        {
            if (b.Kind == BuildingKind.Conveyor)
                continue;
            int x0 = Math.Max(0, b.X - clearance), x1 = Math.Min(w - 1, b.X + b.Width - 1 + clearance);
            int y0 = Math.Max(0, b.Y - clearance), y1 = Math.Min(h - 1, b.Y + b.Height - 1 + clearance);
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                    blocked[y * w + x] = true;
            }
        }
        return blocked;
    }

    /// <summary>
    /// Waypoints from <paramref name="start"/> (not included) to <paramref name="goal"/>, or null if there is no way.
    /// A goal inside an obstacle is replaced by the nearest free cell.
    /// </summary>
    public static (int X, int Y)[]? Find(TileGrid grid, bool[] blocked, (int X, int Y) start, (int X, int Y) goal)
    {
        int w = grid.Width, h = grid.Height;
        if (blocked[goal.Y * w + goal.X] && NearestFree(grid, blocked, goal) is { } free)
            goal = free;
        else if (blocked[goal.Y * w + goal.X])
            return null;
        if (start == goal)
            return [goal];

        var cost = new int[w * h];
        Array.Fill(cost, int.MaxValue);
        var from = new int[w * h];
        var open = new PriorityQueue<int, (int F, int Index)>();
        int s = start.Y * w + start.X, g = goal.Y * w + goal.X;
        cost[s] = 0;
        from[s] = -1;
        open.Enqueue(s, (Heuristic(start.X, start.Y, goal), s));

        while (open.TryDequeue(out int cur, out var pri))
        {
            int cx = cur % w, cy = cur / w;
            if (cur == g)
                break;
            if (pri.F > cost[cur] + Heuristic(cx, cy, goal))
                continue; // a stale entry: a cheaper route to this cell was found after it was queued

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                        continue;
                    int nx = cx + dx, ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h || blocked[ny * w + nx])
                        continue;
                    // No cutting a corner: a diagonal step needs both cells it passes between to be free.
                    if (dx != 0 && dy != 0 && (blocked[cy * w + nx] || blocked[ny * w + cx]))
                        continue;
                    int next = ny * w + nx;
                    int c = cost[cur] + (dx != 0 && dy != 0 ? Diagonal : Straight);
                    if (c >= cost[next])
                        continue;
                    cost[next] = c;
                    from[next] = cur;
                    open.Enqueue(next, (c + Heuristic(nx, ny, goal), next));
                }
            }
        }
        if (cost[g] == int.MaxValue)
            return null;

        var path = new List<(int X, int Y)>();
        for (int i = g; i != s; i = from[i])
            path.Add((i % w, i / w));
        path.Add(start);
        path.Reverse();
        return Straighten(blocked, w, path);
    }

    static int Heuristic(int x, int y, (int X, int Y) goal)
    {
        int dx = Math.Abs(x - goal.X), dy = Math.Abs(y - goal.Y);
        return Straight * Math.Max(dx, dy) + (Diagonal - Straight) * Math.Min(dx, dy);
    }

    /// <summary>The free cell nearest <paramref name="cell"/>, scanning outward ring by ring, or null.</summary>
    static (int X, int Y)? NearestFree(TileGrid grid, bool[] blocked, (int X, int Y) cell)
    {
        for (int r = 1; r <= GoalSearch; r++)
        {
            (int X, int Y)? best = null;
            long bestD = long.MaxValue;
            for (int y = cell.Y - r; y <= cell.Y + r; y++)
            {
                for (int x = cell.X - r; x <= cell.X + r; x++)
                {
                    if (Math.Max(Math.Abs(x - cell.X), Math.Abs(y - cell.Y)) != r || !grid.InBounds(x, y) || blocked[y * grid.Width + x])
                        continue;
                    long d = (long)(x - cell.X) * (x - cell.X) + (long)(y - cell.Y) * (y - cell.Y);
                    if (d < bestD)
                        (best, bestD) = ((x, y), d);
                }
            }
            if (best != null)
                return best;
        }
        return null;
    }

    /// <summary>Drops waypoints that a straight run can skip. The first point is the start and is left out of the result.</summary>
    static (int X, int Y)[] Straighten(bool[] blocked, int w, List<(int X, int Y)> path)
    {
        var result = new List<(int X, int Y)>();
        int i = 0;
        while (i < path.Count - 1)
        {
            int j = path.Count - 1;
            while (j > i + 1 && !Clear(blocked, w, path[i], path[j]))
                j--;
            result.Add(path[j]);
            i = j;
        }
        return [.. result];
    }

    /// <summary>True if the straight line between two cell centres crosses no blocked cell.</summary>
    static bool Clear(bool[] blocked, int w, (int X, int Y) a, (int X, int Y) b)
    {
        int dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y);
        int sx = a.X < b.X ? 1 : -1, sy = a.Y < b.Y ? 1 : -1;
        int x = a.X, y = a.Y, err = dx - dy;
        while (x != b.X || y != b.Y)
        {
            int e2 = 2 * err;
            bool stepX = e2 > -dy, stepY = e2 < dx;
            if (stepX && stepY && (blocked[y * w + x + sx] || blocked[(y + sy) * w + x]))
                return false;
            if (stepX)
            {
                err -= dy;
                x += sx;
            }
            if (stepY)
            {
                err += dx;
                y += sy;
            }
            if (blocked[y * w + x])
                return false;
        }
        return true;
    }
}
