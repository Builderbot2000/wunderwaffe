using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.World;

/// <param name="hp">Starting hit points; null for the def's full <see cref="BuildingDef.Hp"/>.</param>
public sealed class PlacedBuilding(int id, BuildingDef def, int owner, int x, int y, int width, int height, int rotation, RecipeDef? recipe, int? hp = null)
{
    public int Id { get; } = id;
    public string DefId { get; } = def.Id;
    public BuildingKind Kind { get; } = def.Kind;

    /// <summary>Resource radius in cells (FB-018): <see cref="BuildingDef.SupplyRadius"/>, or <see cref="Balance.CoreSupplyRadius"/> for a core.</summary>
    public int SupplyRadius { get; } = def.Kind == BuildingKind.Core ? Balance.CoreSupplyRadius : def.SupplyRadius;

    /// <summary>Player who placed or landed it; a tile's core belongs to the tile's owner. Need not be the tile's owner.</summary>
    public int Owner { get; internal set; } = owner;

    /// <summary>Hit points left, out of <see cref="BuildingDef.Hp"/>. Kept while the building rides on a unit.</summary>
    public int Hp { get; internal set; } = hp ?? def.Hp;

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

    /// <summary>Weapons, shields and repair blocks (CB-001, FB-010, CB-005). A shield starts fully charged.</summary>
    public CombatState? Combat { get; } =
        def.Kind is BuildingKind.Weapon or BuildingKind.Shield or BuildingKind.Repair
            ? new CombatState { Charge = def.Shield?.Capacity ?? 0, AmmoItem = def.Weapon?.Ammo } : null;
}

/// <summary>
/// A tile's local map (FB-001): which building occupies each cell and which deposit lies under it. It covers the
/// tile's bounding box in the world map; cells of the box that belong to other tiles are outside the tile
/// (<see cref="InBounds(int, int)"/> is false) and are kept only to draw the land around it.
/// </summary>
public sealed class TileGrid
{
    readonly int[] _cells;
    readonly string?[] _deposits;
    readonly bool[] _mask;
    readonly bool[] _buildable;
    readonly byte[] _terrain;
    int[] _zones;

    // Index = building id. Slot 0 is unused so a cell value of 0 means empty. Removed slots stay null,
    // which keeps ids and iteration order deterministic.
    readonly List<PlacedBuilding?> _buildings = [null];

    // Buildings by behaviour, each in id order, so the entity sim visits them in a deterministic order.
    readonly List<PlacedBuilding> _belts = [];
    readonly List<PlacedBuilding> _machines = [];
    readonly List<PlacedBuilding> _exportPorts = [];
    readonly List<PlacedBuilding> _importPorts = [];
    readonly List<PlacedBuilding> _combat = [];
    readonly List<PlacedBuilding> _suppliers = [];
    readonly bool[] _frames;

    /// <summary>A rectangular tile of buildable ground with no port zones.</summary>
    public TileGrid(int width, int height)
        : this(width, height, Filled(width * height, true), Filled(width * height, true), new byte[width * height])
    {
    }

    /// <param name="mask">Which cells of the box are part of the tile.</param>
    /// <param name="buildable">Which cells of the tile have ground buildings may stand on.</param>
    /// <param name="terrain">Terrain index of every cell of the box, inside the tile or not.</param>
    internal TileGrid(int width, int height, bool[] mask, bool[] buildable, byte[] terrain)
    {
        _mask = mask;
        _buildable = buildable;
        _terrain = terrain;
        _zones = Filled(width * height, -1);
        for (int i = 0; i < mask.Length; i++)
        {
            if (!mask[i])
                continue;
            Area++;
            if (buildable[i])
                BuildableCount++;
        }
        Width = width;
        Height = height;
        _cells = new int[width * height];
        _deposits = new string?[width * height];
        _frames = new bool[width * height];
    }

    static T[] Filled<T>(int n, T value)
    {
        var a = new T[n];
        Array.Fill(a, value);
        return a;
    }

    /// <summary>Size of the bounding box.</summary>
    public int Width { get; }
    public int Height { get; }

    /// <summary>Cells that are part of the tile.</summary>
    public int Area { get; }

    /// <summary>Cells of the tile whose terrain takes buildings.</summary>
    public int BuildableCount { get; }

    public int BuildingCount { get; private set; }

    public IReadOnlyList<PlacedBuilding> Belts => _belts;

    /// <summary>Machines that run recipes. Import ports are separate; see <see cref="ImportPorts"/>.</summary>
    public IReadOnlyList<PlacedBuilding> Machines => _machines;

    public IReadOnlyList<PlacedBuilding> ExportPorts => _exportPorts;
    public IReadOnlyList<PlacedBuilding> ImportPorts => _importPorts;

    /// <summary>Weapons, shields and repair blocks, in id order: what the combat step visits.</summary>
    public IReadOnlyList<PlacedBuilding> CombatBuildings => _combat;

    /// <summary>Buildings with a resource radius (FB-018), such as the core and storage, in id order.</summary>
    public IReadOnlyList<PlacedBuilding> Suppliers => _suppliers;

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

    /// <summary>Inside the bounding box. The cell may still belong to another tile; see <see cref="InBounds(int, int)"/>.</summary>
    public bool InRect(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    /// <summary>A cell of this tile.</summary>
    public bool InBounds(int x, int y) => InRect(x, y) && _mask[y * Width + x];

    /// <summary>A cell of this tile whose terrain takes buildings (FB-002).</summary>
    public bool Buildable(int x, int y) => InBounds(x, y) && _buildable[y * Width + x];

    /// <summary>Terrain index (into <c>ContentDb.TerrainList</c>) of any cell of the box, or 0 outside it.</summary>
    public int TerrainAt(int x, int y) => InRect(x, y) ? _terrain[y * Width + x] : 0;

    /// <summary>
    /// The neighbouring tile whose I/O zone this cell is in, or -1 (IO-001). A port placed here links to that tile.
    /// </summary>
    public int ZoneAt(int x, int y) => InBounds(x, y) ? _zones[y * Width + x] : -1;

    internal void SetZones(int[] zones)
    {
        _zones = zones;
        Revision++;
    }

    // Frames and buildings only ever go on cells of the tile, so the cheaper box check is enough here.
    public bool HasFrame(int x, int y) => InRect(x, y) && _frames[y * Width + x];

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

    /// <summary>A building by its id, or null once removed.</summary>
    public PlacedBuilding? BuildingById(int id) => id > 0 && id < _buildings.Count ? _buildings[id] : null;

    public PlacedBuilding? BuildingAt(int x, int y) =>
        InRect(x, y) ? _buildings[_cells[y * Width + x]] : null;

    /// <summary>Every cell of the footprint is part of this tile.</summary>
    public bool InBounds(int x, int y, int w, int h)
    {
        if (x < 0 || y < 0 || x + w > Width || y + h > Height)
            return false;
        for (int cy = y; cy < y + h; cy++)
        {
            for (int cx = x; cx < x + w; cx++)
            {
                if (!_mask[cy * Width + cx])
                    return false;
            }
        }
        return true;
    }

    /// <summary>Every cell of the footprint is buildable ground. Assumes it is in bounds.</summary>
    public bool Buildable(int x, int y, int w, int h)
    {
        for (int cy = y; cy < y + h; cy++)
        {
            for (int cx = x; cx < x + w; cx++)
            {
                if (!_buildable[cy * Width + cx])
                    return false;
            }
        }
        return true;
    }

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

    internal PlacedBuilding Add(BuildingDef def, int owner, int x, int y, int w, int h, int rotation, RecipeDef? recipe, int? hp = null)
    {
        var b = new PlacedBuilding(_buildings.Count, def, owner, x, y, w, h, rotation, recipe, hp);
        _buildings.Add(b);
        KindList(b.Kind)?.Add(b);
        if (b.SupplyRadius > 0)
            _suppliers.Add(b);
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
        if (b.SupplyRadius > 0)
            _suppliers.Remove(b);
        BuildingCount--;
        Revision++;
    }

    List<PlacedBuilding>? KindList(BuildingKind kind) => kind switch
    {
        BuildingKind.Conveyor => _belts,
        BuildingKind.Machine => _machines,
        BuildingKind.ExportPort => _exportPorts,
        BuildingKind.ImportPort => _importPorts,
        BuildingKind.Weapon or BuildingKind.Shield or BuildingKind.Repair => _combat,
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

    // ---- Wrecks (CB-006): a layer of their own, so the entity sim, pathing and collisions never see them. ----

    // Index = wreck id, slot 0 unused, removed slots stay null (as with buildings). Cells are allocated on first use.
    readonly List<Wreck?> _wrecks = [null];
    int[]? _wreckCells;

    public int WreckCount { get; private set; }

    public IEnumerable<Wreck> Wrecks
    {
        get
        {
            foreach (var w in _wrecks)
            {
                if (w != null)
                    yield return w;
            }
        }
    }

    public Wreck? WreckAt(int x, int y) =>
        _wreckCells != null && InRect(x, y) ? _wrecks[_wreckCells[y * Width + x]] : null;

    /// <summary>No wreck on any cell of the footprint. Assumes it is in the box.</summary>
    public bool WreckFree(int x, int y, int w, int h)
    {
        if (_wreckCells == null)
            return true;
        for (int cy = y; cy < y + h; cy++)
        {
            for (int cx = x; cx < x + w; cx++)
            {
                if (_wreckCells[cy * Width + cx] != 0)
                    return false;
            }
        }
        return true;
    }

    internal Wreck AddWreck(string defId, int owner, int x, int y, int w, int h, int rotation)
    {
        _wreckCells ??= new int[Width * Height];
        var wreck = new Wreck(_wrecks.Count, defId, owner, x, y, w, h, rotation);
        _wrecks.Add(wreck);
        FillWreck(wreck, wreck.Id);
        WreckCount++;
        Revision++;
        return wreck;
    }

    internal void RemoveWreck(Wreck wreck)
    {
        FillWreck(wreck, 0);
        _wrecks[wreck.Id] = null;
        WreckCount--;
        Revision++;
    }

    void FillWreck(Wreck w, int value)
    {
        for (int cy = w.Y; cy < w.Y + w.Height; cy++)
        {
            for (int cx = w.X; cx < w.X + w.Width; cx++)
                _wreckCells![cy * Width + cx] = value;
        }
    }
}
