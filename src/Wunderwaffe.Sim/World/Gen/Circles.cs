namespace Wunderwaffe.Sim.World.Gen;

/// <summary>
/// Phase four, after deposits: each tile's victory circle (WM-014). The circle goes where the most deposit cells fall in
/// its reach, among the centres that keep it at least <see cref="Balance.CircleClearance"/> cells off the tile's border.
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

        // Deposit cells in the (2r+1)-square around each centre, from a summed-area table: a cheap stand-in for the circle.
        var sum = new int[(w + 1) * (h + 1)];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int d = grid.InBounds(x, y) && grid.DepositAt(x, y) != null ? 1 : 0;
                sum[(y + 1) * (w + 1) + x + 1] = d + sum[y * (w + 1) + x + 1] + sum[(y + 1) * (w + 1) + x] - sum[y * (w + 1) + x];
            }
        }
        int Deposits(int x, int y)
        {
            int x0 = Math.Max(0, x - r), y0 = Math.Max(0, y - r), x1 = Math.Min(w, x + r + 1), y1 = Math.Min(h, y + r + 1);
            return sum[y1 * (w + 1) + x1] - sum[y0 * (w + 1) + x1] - sum[y1 * (w + 1) + x0] + sum[y0 * (w + 1) + x0];
        }

        int deepest = depth.Max();
        // A centre at depth d (Chebyshev steps to the nearest outside cell) has every cell within r of it inside the
        // tile when d > r; the clearance asks for more where the tile allows.
        int need = Math.Min(deepest, r + 1 + Balance.CircleClearance);
        (int X, int Y) best = (0, 0);
        long bestScore = -1;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int d = depth[y * w + x];
                if (d < need)
                    continue;
                long score = (long)Deposits(x, y) * 1_000_000 + d;
                if (score > bestScore)
                    (best, bestScore) = ((x, y), score);
            }
        }
        return best;
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
