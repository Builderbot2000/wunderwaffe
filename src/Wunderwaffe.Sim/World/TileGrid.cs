using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.World;

public sealed class PlacedBuilding(int id, BuildingDef def, int x, int y, int width, int height, int rotation, RecipeDef? recipe)
{
    public int Id { get; } = id;
    public string DefId { get; } = def.Id;
    public BuildingKind Kind { get; } = def.Kind;
    public int X { get; } = x;
    public int Y { get; } = y;

    /// <summary>Footprint after rotation.</summary>
    public int Width { get; } = width;
    public int Height { get; } = height;

    /// <summary>0-3, quarter turns clockwise from east.</summary>
    public int Rotation { get; } = rotation;

    /// <summary>Recipe this building runs, or null for buildings that don't produce (core, conveyor, ports).</summary>
    public string? RecipeId { get; } = recipe?.Id;

    // Runtime state for the tile's entity simulation. Exactly the one matching Kind is set.
    public BeltState? Belt { get; } = def.Kind == BuildingKind.Conveyor ? new BeltState(def.Speed) : null;
    public MachineState? Machine { get; } =
        def.Kind is BuildingKind.Machine or BuildingKind.ImportPort ? new MachineState(recipe) : null;
    public ExportState? Export { get; } = def.Kind == BuildingKind.ExportPort ? new ExportState() : null;
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

    // Buildings by behaviour, each in id order, so the entity sim visits them in a deterministic order.
    readonly List<PlacedBuilding> _belts = [];
    readonly List<PlacedBuilding> _machines = [];
    readonly List<PlacedBuilding> _exportPorts = [];
    readonly List<PlacedBuilding> _importPorts = [];
    readonly bool[] _frames;

    public TileGrid(int width, int height)
    {
        Width = width;
        Height = height;
        _cells = new int[width * height];
        _deposits = new string?[width * height];
        _frames = new bool[width * height];
    }

    public int Width { get; }
    public int Height { get; }
    public int BuildingCount { get; private set; }

    public IReadOnlyList<PlacedBuilding> Belts => _belts;

    /// <summary>Machines that run recipes. Import ports are separate; see <see cref="ImportPorts"/>.</summary>
    public IReadOnlyList<PlacedBuilding> Machines => _machines;

    public IReadOnlyList<PlacedBuilding> ExportPorts => _exportPorts;
    public IReadOnlyList<PlacedBuilding> ImportPorts => _importPorts;

    /// <summary>Cells carrying a unit frame (the floor a unit is built on).</summary>
    public int FrameCount { get; private set; }

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

    public bool HasFrame(int x, int y) => InBounds(x, y) && _frames[y * Width + x];

    internal void SetFrame(int x, int y, bool on)
    {
        if (_frames[y * Width + x] == on)
            return;
        _frames[y * Width + x] = on;
        FrameCount += on ? 1 : -1;
        Revision++;
    }

    public string? DepositAt(int x, int y) => _deposits[y * Width + x];

    public void SetDeposit(int x, int y, string? itemId)
    {
        _deposits[y * Width + x] = itemId;
        Revision++;
    }

    /// <summary>Cells per deposit item, for showing what a tile holds (WM-002).</summary>
    public SortedDictionary<string, int> DepositCounts()
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in _deposits)
        {
            if (item != null)
                counts[item] = counts.GetValueOrDefault(item) + 1;
        }
        return counts;
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

    internal PlacedBuilding Add(BuildingDef def, int x, int y, int w, int h, int rotation, RecipeDef? recipe)
    {
        var b = new PlacedBuilding(_buildings.Count, def, x, y, w, h, rotation, recipe);
        _buildings.Add(b);
        KindList(b.Kind)?.Add(b);
        Fill(b, b.Id);
        BuildingCount++;
        Revision++;
        return b;
    }

    internal void Remove(PlacedBuilding b)
    {
        Fill(b, 0);
        _buildings[b.Id] = null;
        KindList(b.Kind)?.Remove(b);
        BuildingCount--;
        Revision++;
    }

    List<PlacedBuilding>? KindList(BuildingKind kind) => kind switch
    {
        BuildingKind.Conveyor => _belts,
        BuildingKind.Machine => _machines,
        BuildingKind.ExportPort => _exportPorts,
        BuildingKind.ImportPort => _importPorts,
        _ => null,
    };

    void Fill(PlacedBuilding b, int value)
    {
        for (int cy = b.Y; cy < b.Y + b.Height; cy++)
        {
            for (int cx = b.X; cx < b.X + b.Width; cx++)
                _cells[cy * Width + cx] = value;
        }
    }
}
