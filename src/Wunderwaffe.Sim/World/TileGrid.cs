using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.World;

/// <param name="hp">Starting hit points; null for the def's full <see cref="BuildingDef.Hp"/>.</param>
public sealed class PlacedBuilding(int id, BuildingDef def, int owner, int x, int y, int width, int height, int rotation, RecipeDef? recipe, int? hp = null)
{
    public int Id { get; } = id;
    public string DefId { get; } = def.Id;
    public BuildingKind Kind { get; } = def.Kind;
    public BuildingDef Def { get; } = def;

    /// <summary>Resource radius in cells (FB-018): <see cref="BuildingDef.SupplyRadius"/> (cores set theirs in buildings.json).</summary>
    public int SupplyRadius { get; } = def.SupplyRadius;

    /// <summary>Player who placed or landed it; a tile's core belongs to the tile's owner. Need not be the tile's owner.</summary>
    public int Owner { get; internal set; } = owner;

    /// <summary>Hit points left, out of <see cref="BuildingDef.Hp"/>. Kept while the building rides on a unit.</summary>
    public int Hp { get; internal set; } = hp ?? def.Hp;

    /// <summary>Top-left cell; changes only when the building is cut and pasted (FB-023, <see cref="Relocation"/>).</summary>
    public int X { get; internal set; } = x;
    public int Y { get; internal set; } = y;

    /// <summary>Footprint after rotation.</summary>
    public int Width { get; internal set; } = width;
    public int Height { get; internal set; } = height;

    /// <summary>0-3, quarter turns clockwise from east.</summary>
    public int Rotation { get; internal set; } = rotation;

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

    public FactoryState? Factory { get; } = def.Kind == BuildingKind.UnitFactory ? new FactoryState() : null;
    public StorageState? Storage { get; } = def.Kind == BuildingKind.Storage ? new StorageState(def.Storage) : null;
    public BatteryState? Battery { get; } =
        def.Kind == BuildingKind.Battery ? new BatteryState((long)def.PowerCapacity * Simulation.TicksPerSecond) : null;
    public RelayState? Relay { get; } = def.Kind switch
    {
        BuildingKind.Junction => new RelayState(4),
        BuildingKind.Router or BuildingKind.Bridge => new RelayState(1),
        _ => null,
    };
    public SorterState? Sorter { get; } = def.Kind == BuildingKind.Sorter ? new SorterState() : null;

    /// <summary>A core's own stock (FB-007, FB-019): build funds on its tile, up to <see cref="BuildingDef.Capacity"/> per item from deliveries.</summary>
    public Core.Inventory? Stock { get; } = def.Kind == BuildingKind.Core ? new Core.Inventory() : null;

    /// <summary>Takes part in a power network (FB-005); see <see cref="BuildingDef.IsPower"/>.</summary>
    public bool IsPower { get; } = def.IsPower;
}

/// <summary>
/// A tile's local map (FB-001): which building occupies each cell, and what each cell layer (FB-022: deposits,
/// platforms, frame, cables, from layers.json) holds there. It covers the
/// tile's bounding box in the world map; cells of the box that belong to other tiles are outside the tile
/// (<see cref="InBounds(int, int)"/> is false) and are kept only to draw the land around it.
/// </summary>
public sealed class TileGrid
{
    readonly int[] _cells;
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
    readonly List<PlacedBuilding> _factories = [];
    readonly List<PlacedBuilding> _storages = [];
    readonly List<PlacedBuilding> _relays = [];
    readonly List<PlacedBuilding> _cores = [];

    // Cell layers (FB-022): per layer, the entry each cell holds (0 = empty), and how many cells hold each entry.
    readonly byte[][] _layers;
    readonly int[][] _layerCounts;

    /// <param name="mask">Which cells of the box are part of the tile.</param>
    /// <param name="buildable">Which cells of the tile have ground buildings may stand on.</param>
    /// <param name="terrain">Terrain index of every cell of the box, inside the tile or not.</param>
    /// <param name="layers">The content's cell layers.</param>
    internal TileGrid(int width, int height, bool[] mask, bool[] buildable, byte[] terrain, LayerSet layers)
    {
        Layers = layers;
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
        _layers = new byte[layers.Count][];
        _layerCounts = new int[layers.Count][];
        for (int l = 0; l < layers.Count; l++)
        {
            _layers[l] = new byte[width * height];
            _layerCounts[l] = new int[layers[l].Ids.Count];
            _layerCounts[l][0] = width * height;
        }
        Power = new TilePower(this);
        Patches = new DepositPatches(this);
    }

    /// <summary>A copy of the ground alone (shape, buildability, terrain), with nothing on it: for trying out deposits.</summary>
    internal TileGrid Blank() => new(Width, Height, _mask, _buildable, _terrain, Layers);

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
    public IReadOnlyList<PlacedBuilding> Storages => _storages;

    /// <summary>Cores standing on the tile, every owner's, in id order (WM-016).</summary>
    public IReadOnlyList<PlacedBuilding> Cores => _cores;

    /// <summary>Junctions, routers and bridges (FB-004).</summary>
    public IReadOnlyList<PlacedBuilding> Relays => _relays;

    /// <summary>Power networks and boosted stats, rebuilt from the layout when <see cref="Revision"/> changes (FB-005, FB-016).</summary>
    public TilePower Power { get; }

    /// <summary>Machines the entity step skips until something changes around them (NF-001).</summary>
    internal MachineSleep Sleep { get; } = new();

    /// <summary>Weapons, shields and repair blocks, in id order: what the combat step visits.</summary>
    public IReadOnlyList<PlacedBuilding> CombatBuildings => _combat;

    /// <summary>Buildings with a resource radius (FB-018), such as the core and storage, in id order.</summary>
    public IReadOnlyList<PlacedBuilding> Suppliers => _suppliers;

    /// <summary>Unit factories (UD-006), in id order.</summary>
    public IReadOnlyList<PlacedBuilding> Factories => _factories;

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

    /// <summary>A cell of this tile whose terrain, or a floor layer on it (platforms, FB-017), takes buildings (FB-002).</summary>
    public bool Buildable(int x, int y) => InBounds(x, y) && Ground(y * Width + x, -1);

    /// <summary>Like <see cref="Buildable(int, int)"/> as if layer <paramref name="without"/> were empty there: what removing it would leave.</summary>
    public bool BuildableWithout(int x, int y, int without) => InBounds(x, y) && Ground(y * Width + x, without);

    bool Ground(int i, int without)
    {
        if (_buildable[i])
            return true;
        foreach (int l in Layers.Floors)
        {
            if (l != without && _layers[l][i] != 0)
                return true;
        }
        return false;
    }

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

    // ---- Cell layers (FB-022). Every layer is stored, counted and cleared the same way; behaviour code finds its
    // layer by building kind (Layers.Of) and reads it through these. Layered cells are always cells of the tile, so
    // the cheaper box check is enough here.

    /// <summary>The content's layers, in stacking order.</summary>
    public LayerSet Layers { get; }

    /// <summary>The entry of <paramref name="layer"/> at a cell (0 = empty, also outside the box or for layer -1).</summary>
    public int EntryAt(int layer, int x, int y) => (uint)layer < (uint)_layers.Length && InRect(x, y) ? _layers[layer][y * Width + x] : 0;

    public bool Has(int layer, int x, int y) => EntryAt(layer, x, y) != 0;

    /// <summary>Whether the layer buildings of <paramref name="kind"/> live on holds anything at a cell (frame, cable, platform).</summary>
    public bool Has(BuildingKind kind, int x, int y) => Has(Layers.Of(kind), x, y);

    /// <summary>The building laid at a cell of a building layer, or null.</summary>
    public BuildingDef? LaidAt(int layer, int x, int y) => EntryAt(layer, x, y) is > 0 and var e ? Layers[layer].Buildings[e] : null;

    /// <summary>Cells of <paramref name="layer"/> holding anything (0 for layer -1).</summary>
    public int CountOn(int layer) => (uint)layer < (uint)_layers.Length ? _layers[layer].Length - _layerCounts[layer][0] : 0;

    /// <summary>Cells of <paramref name="layer"/> holding <paramref name="entry"/>.</summary>
    public int CountOn(int layer, int entry) => _layerCounts[layer][entry];

    /// <inheritdoc cref="CountOn(int)"/>
    public int CountOn(BuildingKind kind) => CountOn(Layers.Of(kind));

    /// <summary>Puts <paramref name="entry"/> (0 clears) on a cell of a layer.</summary>
    internal void SetEntry(int layer, int x, int y, int entry)
    {
        int i = y * Width + x;
        int old = _layers[layer][i];
        if (old == entry)
            return;
        _layers[layer][i] = (byte)entry;
        _layerCounts[layer][old]--;
        _layerCounts[layer][entry]++;
        Revision++;
    }

    /// <summary>Lays a layer building (frame, cable, platform) on a cell.</summary>
    internal void Lay(BuildingDef def, int x, int y)
    {
        var (layer, entry) = Layers.Of(def);
        SetEntry(layer, x, y, entry);
    }

    /// <summary>Empties a cell of the layer buildings of <paramref name="kind"/> live on.</summary>
    internal void Clear(BuildingKind kind, int x, int y) => SetEntry(Layers.Of(kind), x, y, 0);

    /// <summary>The raw cells of a layer, row by row, for hashing and snapshots.</summary>
    internal ReadOnlySpan<byte> LayerCells(int layer) => _layers[layer];

    // Deposits are the layer whose entries are items; their patches and amounts (RS-004) sit beside it in Patches.

    /// <summary>Deposit patches and what each holds (RS-004).</summary>
    public DepositPatches Patches { get; }

    public string? DepositAt(int x, int y) => EntryAt(Layers.Deposit, x, y) is > 0 and var e ? Layers[Layers.Deposit].Ids[e] : null;

    /// <summary>Lays or clears a deposit by hand. The cell leaves its patch, if it had one: a hand-laid deposit never runs out (RS-004).</summary>
    public void SetDeposit(int x, int y, string? itemId)
    {
        Patches.Detach(x, y);
        SetEntry(Layers.Deposit, x, y, itemId == null ? 0 : Layers[Layers.Deposit].EntryOf(itemId));
    }

    /// <summary>Cells per deposit item, for showing what a tile holds (WM-002).</summary>
    public SortedDictionary<string, int> DepositCounts()
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var layer = Layers[Layers.Deposit];
        for (int e = 1; e < layer.Ids.Count; e++)
        {
            if (_layerCounts[layer.Index][e] > 0)
                counts[layer.Ids[e]] = _layerCounts[layer.Index][e];
        }
        return counts;
    }

    /// <summary>A building by its id, or null once removed.</summary>
    public PlacedBuilding? BuildingById(int id) => id > 0 && id < _buildings.Count ? _buildings[id] : null;

    public PlacedBuilding? BuildingAt(int x, int y) =>
        InRect(x, y) ? _buildings[_cells[y * Width + x]] : null;

    /// <summary>Buildings sharing an edge with <paramref name="b"/>'s footprint, each once, in id order.</summary>
    public List<PlacedBuilding> Touching(PlacedBuilding b)
    {
        var found = new List<PlacedBuilding>();
        void At(int x, int y)
        {
            if (BuildingAt(x, y) is { } n && n != b && !found.Contains(n))
                found.Add(n);
        }
        for (int x = b.X; x < b.X + b.Width; x++)
        {
            At(x, b.Y - 1);
            At(x, b.Y + b.Height);
        }
        for (int y = b.Y; y < b.Y + b.Height; y++)
        {
            At(b.X - 1, y);
            At(b.X + b.Width, y);
        }
        found.Sort((p, q) => p.Id.CompareTo(q.Id));
        return found;
    }

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

    /// <summary>Every cell of the footprint is buildable ground or platform. Assumes it is in bounds.</summary>
    public bool Buildable(int x, int y, int w, int h)
    {
        for (int cy = y; cy < y + h; cy++)
        {
            for (int cx = x; cx < x + w; cx++)
            {
                if (!Ground(cy * Width + cx, -1))
                    return false;
            }
        }
        return true;
    }

    /// <summary>No building on the footprint other than <paramref name="except"/> (a building being moved, FB-023).</summary>
    public bool IsFree(int x, int y, int w, int h, PlacedBuilding? except = null)
    {
        for (int cy = y; cy < y + h; cy++)
        {
            for (int cx = x; cx < x + w; cx++)
            {
                if (_cells[cy * Width + cx] is int id and not 0 && id != except?.Id)
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

    /// <summary>
    /// Puts a building down at a new spot and rotation, keeping its id and all its state (FB-023). Kind lists stay in id
    /// order, and the <see cref="Revision"/> bump rebuilds power and boosts and wakes sleeping machines.
    /// </summary>
    internal void Move(PlacedBuilding b, int x, int y, int w, int h, int rotation)
    {
        Fill(b, 0);
        (b.X, b.Y, b.Width, b.Height, b.Rotation) = (x, y, w, h, rotation);
        Fill(b, b.Id);
        Revision++;
    }

    /// <summary>
    /// A building changed hands (WM-019): bumps <see cref="Revision"/> so radii, circle occupants, power, rates and
    /// sleeping machines see the new owner.
    /// </summary>
    internal void SetOwner(PlacedBuilding b, int owner)
    {
        b.Owner = owner;
        Revision++;
    }

    List<PlacedBuilding>? KindList(BuildingKind kind) => kind switch
    {
        BuildingKind.Conveyor => _belts,
        BuildingKind.Machine => _machines,
        BuildingKind.ExportPort => _exportPorts,
        BuildingKind.ImportPort => _importPorts,
        BuildingKind.Weapon or BuildingKind.Shield or BuildingKind.Repair => _combat,
        BuildingKind.UnitFactory => _factories,
        BuildingKind.Storage => _storages,
        BuildingKind.Junction or BuildingKind.Router or BuildingKind.Bridge => _relays,
        BuildingKind.Core => _cores,
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
    // ---- Snapshots (NF-008): put a grid's changing state back exactly, building ids and revision included. ----

    /// <summary>
    /// Removes every building, wreck and layer entry (deposits too) and forgets their ids, so a snapshot can add its own back.
    /// </summary>
    internal void ClearForRestore()
    {
        Array.Clear(_cells);
        for (int l = 0; l < _layers.Length; l++)
        {
            Array.Clear(_layers[l]);
            Array.Clear(_layerCounts[l]);
            _layerCounts[l][0] = _layers[l].Length;
        }
        _buildings.Clear();
        _buildings.Add(null);
        foreach (var list in new[] { _belts, _machines, _exportPorts, _importPorts, _combat, _suppliers, _factories, _storages, _relays, _cores })
            list.Clear();
        _wrecks.Clear();
        _wrecks.Add(null);
        _wreckCells = null;
        WreckCount = 0;
        BuildingCount = 0;
        Power.Dirty = true;
        Sleep.Dirty = true;
    }

    /// <summary>Adds a building under a given id, higher than any added since <see cref="ClearForRestore"/>.</summary>
    internal PlacedBuilding Restore(int id, BuildingDef def, int owner, int x, int y, int w, int h, int rotation, RecipeDef? recipe, int hp)
    {
        while (_buildings.Count < id)
            _buildings.Add(null);
        var b = new PlacedBuilding(id, def, owner, x, y, w, h, rotation, recipe, hp);
        _buildings.Add(b);
        KindList(b.Kind)?.Add(b);
        if (b.SupplyRadius > 0)
            _suppliers.Add(b);
        Fill(b, b.Id);
        BuildingCount++;
        return b;
    }

    /// <summary>Puts a wreck back under its id, higher than any restored before it.</summary>
    internal void RestoreWreck(Wreck wreck)
    {
        while (_wrecks.Count < wreck.Id)
            _wrecks.Add(null);
        _wreckCells ??= new int[Width * Height];
        _wrecks.Add(wreck);
        FillWreck(wreck, wreck.Id);
        WreckCount++;
    }

    /// <summary>Ids handed out so far for wrecks: the next wreck gets this one.</summary>
    internal int NextWreckId => _wrecks.Count;

    /// <summary>Ids handed out so far: the next building added gets this one.</summary>
    internal int NextBuildingId => _buildings.Count;

    /// <summary>Pads the id list so the next building added gets <paramref name="next"/>, as it would have in the saved game.</summary>
    internal void RestoreIds(int next, int nextWreck, int revision)
    {
        while (_buildings.Count < next)
            _buildings.Add(null);
        while (_wrecks.Count < nextWreck)
            _wrecks.Add(null);
        Revision = revision;
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
