namespace Wunderwaffe.Sim.World;

public sealed class PlacedBuilding(int id, string defId, int x, int y, int width, int height, int rotation, string? recipeId)
{
    public int Id { get; } = id;
    public string DefId { get; } = defId;
    public int X { get; } = x;
    public int Y { get; } = y;

    /// <summary>Footprint after rotation.</summary>
    public int Width { get; } = width;
    public int Height { get; } = height;

    /// <summary>0-3, quarter turns clockwise.</summary>
    public int Rotation { get; } = rotation;

    /// <summary>Recipe this building runs, or null for buildings that don't produce (core, conveyor).</summary>
    public string? RecipeId { get; } = recipeId;
}

/// <summary>
/// A tile's local map (FB-001): which building occupies each cell and which deposit lies under it.
/// </summary>
public sealed class TileGrid
{
    readonly int[] _cells;
    readonly string?[] _deposits;

    // Index = building id. Slot 0 is unused so a cell value of 0 means empty. Removed slots stay null,
    // which keeps ids and iteration order deterministic.
    readonly List<PlacedBuilding?> _buildings = [null];

    public TileGrid(int width, int height)
    {
        Width = width;
        Height = height;
        _cells = new int[width * height];
        _deposits = new string?[width * height];
    }

    public int Width { get; }
    public int Height { get; }
    public int BuildingCount { get; private set; }

    /// <summary>Increments on every change so views know when to redraw.</summary>
    public int Revision { get; private set; }

    public IEnumerable<PlacedBuilding> Buildings
    {
        get
        {
            foreach (var b in _buildings)
            {
                if (b != null)
                    yield return b;
            }
        }
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public string? DepositAt(int x, int y) => _deposits[y * Width + x];

    public void SetDeposit(int x, int y, string? itemId)
    {
        _deposits[y * Width + x] = itemId;
        Revision++;
    }

    public PlacedBuilding? BuildingAt(int x, int y) =>
        InBounds(x, y) ? _buildings[_cells[y * Width + x]] : null;

    public bool InBounds(int x, int y, int w, int h) => x >= 0 && y >= 0 && x + w <= Width && y + h <= Height;

    public bool IsFree(int x, int y, int w, int h)
    {
        for (int cy = y; cy < y + h; cy++)
        {
            for (int cx = x; cx < x + w; cx++)
            {
                if (_cells[cy * Width + cx] != 0)
                    return false;
            }
        }
        return true;
    }

    internal PlacedBuilding Add(string defId, int x, int y, int w, int h, int rotation, string? recipeId)
    {
        var b = new PlacedBuilding(_buildings.Count, defId, x, y, w, h, rotation, recipeId);
        _buildings.Add(b);
        Fill(b, b.Id);
        BuildingCount++;
        Revision++;
        return b;
    }

    internal void Remove(PlacedBuilding b)
    {
        Fill(b, 0);
        _buildings[b.Id] = null;
        BuildingCount--;
        Revision++;
    }

    void Fill(PlacedBuilding b, int value)
    {
        for (int cy = b.Y; cy < b.Y + b.Height; cy++)
        {
            for (int cx = b.X; cx < b.X + b.Width; cx++)
                _cells[cy * Width + cx] = value;
        }
    }
}
