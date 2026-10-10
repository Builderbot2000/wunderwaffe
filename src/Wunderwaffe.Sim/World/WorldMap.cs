namespace Wunderwaffe.Sim.World;

/// <summary>
/// The one global cell map every tile is cut from (WM-001, WM-003). Each cell has a terrain, an elevation and the tile
/// it belongs to; a tile's local map is its bounding box of this map, offset by the tile's origin. Units move in these
/// global coordinates, so the tile a unit is on is just the tile of the cell under it.
/// </summary>
public sealed class WorldMap
{
    /// <summary>Two tiles are neighbours only if they share at least this many cell edges; touching corners don't count.</summary>
    public const int MinBorderCells = 3;

    byte[] _terrain;
    short[] _elevation;
    int[] _tileOf;
    int[][] _neighbors = [];
    readonly Dictionary<(int From, int To), (int X, int Y)[]> _borders = [];

    public WorldMap(int width, int height)
    {
        Width = width;
        Height = height;
        _terrain = new byte[width * height];
        _elevation = new short[width * height];
        _tileOf = new int[width * height];
        Array.Fill(_tileOf, -1);
    }

    internal WorldMap(int width, int height, byte[] terrain, short[] elevation, int[] tileOf)
    {
        Width = width;
        Height = height;
        _terrain = terrain;
        _elevation = elevation;
        _tileOf = tileOf;
    }

    public int Width { get; private set; }
    public int Height { get; private set; }

    public bool InBounds(int gx, int gy) => gx >= 0 && gy >= 0 && gx < Width && gy < Height;

    /// <summary>The tile a cell belongs to, or -1 outside the world or in a gap between hand-built tiles.</summary>
    public int TileAtCell(int gx, int gy) => InBounds(gx, gy) ? _tileOf[gy * Width + gx] : -1;

    /// <summary>Index into <c>ContentDb.TerrainList</c>. Cells outside the world read as terrain 0.</summary>
    public int TerrainAt(int gx, int gy) => InBounds(gx, gy) ? _terrain[gy * Width + gx] : 0;

    /// <summary>0-1000; sea level is <see cref="Gen.TerrainGen.SeaLevel"/>.</summary>
    public int ElevationAt(int gx, int gy) => InBounds(gx, gy) ? _elevation[gy * Width + gx] : 0;

    /// <summary>Neighbouring tile ids, ascending.</summary>
    public IReadOnlyList<int> NeighborsOf(int tileId) => tileId >= 0 && tileId < _neighbors.Length ? _neighbors[tileId] : [];

    /// <summary>
    /// Cells of tile <paramref name="from"/> that share an edge with tile <paramref name="to"/>, row by row. Empty if
    /// they aren't neighbours.
    /// </summary>
    public IReadOnlyList<(int X, int Y)> BorderCells(int from, int to) => _borders.GetValueOrDefault((from, to)) ?? [];

    /// <summary>Hand-built worlds: makes room for more tiles, keeping what is there. New cells belong to no tile.</summary>
    internal void Grow(int width, int height, byte terrain)
    {
        if (width <= Width && height <= Height)
            return;
        width = Math.Max(width, Width);
        height = Math.Max(height, Height);
        var t = new byte[width * height];
        var e = new short[width * height];
        var o = new int[width * height];
        Array.Fill(t, terrain);
        Array.Fill(e, (short)Gen.TerrainGen.LowlandElevation);
        Array.Fill(o, -1);
        for (int y = 0; y < Height; y++)
        {
            Array.Copy(_terrain, y * Width, t, y * width, Width);
            Array.Copy(_elevation, y * Width, e, y * width, Width);
            Array.Copy(_tileOf, y * Width, o, y * width, Width);
        }
        (_terrain, _elevation, _tileOf, Width, Height) = (t, e, o, width, height);
    }

    internal void SetTile(int gx, int gy, int tileId) => _tileOf[gy * Width + gx] = tileId;

    /// <summary>Recomputes which tiles are neighbours and the cells along each shared border.</summary>
    internal void RebuildAdjacency(int tileCount)
    {
        var edges = new Dictionary<(int, int), int>();
        var cells = new Dictionary<(int From, int To), SortedSet<int>>();
        void Touch(int a, int b, int index)
        {
            if (!cells.TryGetValue((a, b), out var set))
                cells[(a, b)] = set = [];
            set.Add(index);
        }

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int i = y * Width + x, a = _tileOf[i];
                if (a < 0)
                    continue;
                // Each edge once: to the right and below.
                if (x + 1 < Width && _tileOf[i + 1] is int r && r >= 0 && r != a)
                {
                    edges[(Math.Min(a, r), Math.Max(a, r))] = edges.GetValueOrDefault((Math.Min(a, r), Math.Max(a, r))) + 1;
                    Touch(a, r, i);
                    Touch(r, a, i + 1);
                }
                if (y + 1 < Height && _tileOf[i + Width] is int d && d >= 0 && d != a)
                {
                    edges[(Math.Min(a, d), Math.Max(a, d))] = edges.GetValueOrDefault((Math.Min(a, d), Math.Max(a, d))) + 1;
                    Touch(a, d, i);
                    Touch(d, a, i + Width);
                }
            }
        }

        var lists = new List<int>[tileCount];
        for (int t = 0; t < tileCount; t++)
            lists[t] = [];
        _borders.Clear();
        foreach (var ((a, b), n) in edges)
        {
            if (n < MinBorderCells)
                continue;
            lists[a].Add(b);
            lists[b].Add(a);
            _borders[(a, b)] = cells[(a, b)].Select(i => (i % Width, i / Width)).ToArray();
            _borders[(b, a)] = cells[(b, a)].Select(i => (i % Width, i / Width)).ToArray();
        }
        _neighbors = lists.Select(l => l.Order().ToArray()).ToArray();
    }
}
