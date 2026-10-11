namespace Wunderwaffe.Sim.World.Gen;

/// <summary>
/// Phase four, after deposits: each tile's victory circle (WM-014). The circle goes where the most deposit cells fall in
/// the circle, among the centres that keep it at least <see cref="Balance.CircleClearance"/> cells off the tile's border
/// (or, when no deposit is in reach of those, just wholly inside the tile).
/// Ties go to the centre deepest inside the tile, then the lowest row and column. A tile too small for that relaxes
/// the clearance, and one too small for the circle at all gets its deepest cell. Ocean tiles have no deposits, so their
/// circle is simply the deepest spot.
/// </summary>
internal static class Circles
{
    /// <summary>The local cell of the circle's centre.</summary>
    public static (int X, int Y) Place(TileGrid grid)
    {
        int w = grid.Width, h = grid.Height, r = Balance.CircleRadius;
        var depth = Depth(grid);

        // Deposit cells in reach of each centre, exactly: per row of the circle, a run of a row's running sums.
        var row = new int[h * (w + 1)];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
                row[y * (w + 1) + x + 1] = row[y * (w + 1) + x] + (grid.InBounds(x, y) && grid.DepositAt(x, y) != null ? 1 : 0);
        }
        var half = new int[r + 1];
        for (int dy = 0; dy <= r; dy++)
            half[dy] = (int)Core.IntMath.ISqrt((long)r * r - (long)dy * dy);
        int Deposits(int x, int y)
        {
            int n = 0;
            for (int dy = -r; dy <= r; dy++)
            {
                int cy = y + dy;
                if (cy < 0 || cy >= h)
                    continue;
                int hw = half[Math.Abs(dy)], x0 = Math.Max(0, x - hw), x1 = Math.Min(w, x + hw + 1);
                if (x1 > x0)
                    n += row[cy * (w + 1) + x1] - row[cy * (w + 1) + x0];
            }
            return n;
        }

        int deepest = depth.Max();
        // A centre at depth d (Chebyshev steps to the nearest outside cell) has every cell within r of it inside the
        // tile when d > r. The clearance asks for more where the tile allows; if that leaves no deposits in reach, the
        // circle may come up to the border instead, but stays wholly inside where it can.
        (int X, int Y, int Deposits) Best(int need)
        {
            (int X, int Y, int Deposits) best = (0, 0, -1);
            long bestScore = -1;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int d = depth[y * w + x];
                    if (d < need)
                        continue;
                    int n = Deposits(x, y);
                    long score = (long)n * 1_000_000 + d;
                    if (score > bestScore)
                        (best, bestScore) = ((x, y, n), score);
                }
            }
            return best;
        }
        var clear = Best(Math.Min(deepest, r + 1 + Balance.CircleClearance));
        if (clear.Deposits <= 0 && deepest > r + 1 + Balance.CircleClearance)
        {
            var near = Best(r + 1);
            if (near.Deposits > 0)
                return (near.X, near.Y);
        }
        return (clear.X, clear.Y);
    }

    /// <summary>
    /// For each cell of the box, Chebyshev steps to the nearest cell outside the tile (0 outside it); cells off the
    /// box count as outside.
    /// </summary>
    static int[] Depth(TileGrid grid)
    {
        int w = grid.Width, h = grid.Height;
        var depth = new int[w * h];
        var queue = new Queue<int>();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (!grid.InBounds(x, y))
                    queue.Enqueue(i);
                else if (x == 0 || y == 0 || x == w - 1 || y == h - 1)
                {
                    depth[i] = 1;
                    queue.Enqueue(i);
                }
                else
                {
                    depth[i] = int.MaxValue;
                }
            }
        }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue(), x = i % w, y = i / w;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                        continue;
                    int n = ny * w + nx;
                    if (depth[n] <= depth[i] + 1)
                        continue;
                    depth[n] = depth[i] + 1;
                    queue.Enqueue(n);
                }
            }
        }
        return depth;
    }
}
