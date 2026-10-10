using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.World;

/// <summary>A rectangle of world cells that a unit plans its path in.</summary>
internal readonly record struct PathWindow(int X, int Y, int W, int H);

/// <summary>
/// Path-finding for units over the tile they are on, plus the next tile when crossing into it. Buildings (except
/// conveyors) are obstacles, grown by the unit's size so the path keeps its whole body clear; cells of other tiles are
/// out of bounds. A* over the cell grid, then the path is straightened wherever there is a clear line,
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

    /// <summary>The bounding box of <paramref name="tile"/> and, if given, <paramref name="next"/>.</summary>
    public static PathWindow Window(TileState tile, TileState? next)
    {
        int x0 = tile.OriginX, y0 = tile.OriginY, x1 = x0 + tile.Grid.Width, y1 = y0 + tile.Grid.Height;
        if (next != null)
        {
            x0 = Math.Min(x0, next.OriginX);
            y0 = Math.Min(y0, next.OriginY);
            x1 = Math.Max(x1, next.OriginX + next.Grid.Width);
            y1 = Math.Max(y1, next.OriginY + next.Grid.Height);
        }
        return new PathWindow(x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>
    /// Cells of the window a unit with the given clearance can't put its centre in: those of other tiles, and those
    /// within reach of a building on <paramref name="tile"/> or <paramref name="next"/>. Indexed from the window's corner.
    /// </summary>
    public static bool[] Blocked(Simulation sim, PathWindow win, TileState tile, TileState? next, int clearance)
    {
        int w = win.W, h = win.H;
        int nextId = next?.Id ?? -1;
        var blocked = new bool[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int id = sim.Map.TileAtCell(win.X + x, win.Y + y);
                blocked[y * w + x] = id != tile.Id && id != nextId;
            }
        }
        foreach (var t in next == null ? new[] { tile } : new[] { tile, next })
        {
            foreach (var b in t.Grid.Buildings)
            {
                if (b.Kind == BuildingKind.Conveyor)
                    continue;
                int bx = t.OriginX + b.X - win.X, by = t.OriginY + b.Y - win.Y;
                int x0 = Math.Max(0, bx - clearance), x1 = Math.Min(w - 1, bx + b.Width - 1 + clearance);
                int y0 = Math.Max(0, by - clearance), y1 = Math.Min(h - 1, by + b.Height - 1 + clearance);
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                        blocked[y * w + x] = true;
                }
            }
        }
        return blocked;
    }

    /// <summary>
    /// Waypoints from <paramref name="start"/> (not included) to <paramref name="goal"/>, or null if there is no way.
    /// A goal inside an obstacle is replaced by the nearest free cell.
    /// </summary>
    public static (int X, int Y)[]? Find(int w, int h, bool[] blocked, (int X, int Y) start, (int X, int Y) goal)
    {
        if (blocked[goal.Y * w + goal.X] && NearestFree(w, h, blocked, goal) is { } free)
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
    static (int X, int Y)? NearestFree(int w, int h, bool[] blocked, (int X, int Y) cell)
    {
        for (int r = 1; r <= GoalSearch; r++)
        {
            (int X, int Y)? best = null;
            long bestD = long.MaxValue;
            for (int y = cell.Y - r; y <= cell.Y + r; y++)
            {
                for (int x = cell.X - r; x <= cell.X + r; x++)
                {
                    if (Math.Max(Math.Abs(x - cell.X), Math.Abs(y - cell.Y)) != r || x < 0 || y < 0 || x >= w || y >= h || blocked[y * w + x])
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
