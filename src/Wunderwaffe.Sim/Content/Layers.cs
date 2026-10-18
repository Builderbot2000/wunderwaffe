namespace Wunderwaffe.Sim.Content;

/// <summary>
/// A per-cell layer of a tile (FB-022, OQ-52), from layers.json; file order is stacking order, bottom to top. Each cell
/// of a layer holds one of its entries (0 = empty): the buildings that name the layer, or every item for the layer that
/// holds items (deposits). Behaviour stays in code by <see cref="BuildingKind"/>; the stacking rules here are generic.
/// </summary>
/// <param name="SitsOn">
/// What a cell must have for the layer to be laid there, any one of: <c>ground</c> (terrain or a <see cref="Floor"/>
/// that takes buildings), <c>water</c> (water terrain) or the id of a layer below this one. Empty for a layer only world
/// generation fills.
/// </param>
/// <param name="Floor">Buildings, and layers that sit on <c>ground</c>, may stand on it where the terrain alone wouldn't take them (platforms).</param>
/// <param name="UnderBuildings">May be laid on a cell a building already stands on (frame, cables).</param>
/// <param name="BlocksBuildings">No building may be placed over its cells.</param>
/// <param name="Holds"><c>buildings</c> (placed by players) or <c>items</c> (the deposit layer; exactly one).</param>
/// <param name="Draw">How the tile view draws it: <c>tiled</c> (edge to edge), <c>icon</c> (item sprite on the ground) or <c>links</c> (joined to its neighbours, like cables).</param>
public sealed record LayerDef(
    string Id,
    string Name,
    string[]? SitsOn = null,
    bool Floor = false,
    bool UnderBuildings = false,
    bool BlocksBuildings = false,
    string Holds = LayerDef.HoldsBuildings,
    string Draw = "tiled")
{
    public const string Ground = "ground", Water = "water", HoldsBuildings = "buildings", HoldsItems = "items";
    public static readonly string[] DrawStyles = ["tiled", "icon", "links"];

    /// <summary>The kinds of building that live on a layer instead of the building grid.</summary>
    public static readonly BuildingKind[] Kinds = [BuildingKind.Platform, BuildingKind.Wire, BuildingKind.Frame];
}

/// <summary>A layer resolved against the content: its index and its entries (entry 0 is empty).</summary>
public sealed class Layer
{
    readonly Dictionary<string, int> _entryOf = new(StringComparer.Ordinal);

    internal Layer(LayerDef def, int index, IReadOnlyList<string> ids, IReadOnlyList<BuildingDef?> buildings)
    {
        Def = def;
        Index = index;
        Ids = ids;
        Buildings = buildings;
        for (int i = 1; i < ids.Count; i++)
            _entryOf[ids[i]] = i;
    }

    public LayerDef Def { get; }
    public string Id => Def.Id;

    /// <summary>Position in <see cref="LayerSet.All"/>, the index tile grids store it under.</summary>
    public int Index { get; }

    public bool HoldsItems => Def.Holds == LayerDef.HoldsItems;

    /// <summary>Content id of each entry (building or item); <c>Ids[0]</c> is "" for empty.</summary>
    public IReadOnlyList<string> Ids { get; }

    /// <summary>Building of each entry; null for entry 0 and on the item layer.</summary>
    public IReadOnlyList<BuildingDef?> Buildings { get; }

    /// <summary>Most entries a layer can have: a cell stores its entry in a byte.</summary>
    public const int MaxEntries = 255;

    /// <summary>The entry for a building or item id, or 0 if it isn't on this layer.</summary>
    public int EntryOf(string id) => _entryOf.GetValueOrDefault(id);
}

/// <summary>Every layer of the content, in stacking order, with the lookups behaviour code uses (by kind, the deposit layer).</summary>
public sealed class LayerSet
{
    readonly int[] _byKind;
    readonly Dictionary<string, (int Layer, int Entry)> _ofBuilding = new(StringComparer.Ordinal);

    internal LayerSet(IReadOnlyList<Layer> all)
    {
        All = all;
        _byKind = new int[Enum.GetValues<BuildingKind>().Length];
        Array.Fill(_byKind, -1);
        Deposit = -1;
        var floors = new List<int>();
        foreach (var layer in all)
        {
            if (layer.HoldsItems && Deposit < 0)
                Deposit = layer.Index;
            if (layer.Def.Floor)
                floors.Add(layer.Index);
            for (int e = 1; !layer.HoldsItems && e < layer.Buildings.Count; e++)
            {
                var b = layer.Buildings[e]!;
                _ofBuilding[b.Id] = (layer.Index, e);
                if (_byKind[(int)b.Kind] < 0)
                    _byKind[(int)b.Kind] = layer.Index;
            }
        }
        Floors = floors;
    }

    /// <summary>Layers bottom to top: the order they are stored, hashed, checked and drawn in.</summary>
    public IReadOnlyList<Layer> All { get; }

    public int Count => All.Count;
    public Layer this[int index] => All[index];

    /// <summary>The layer whose cells hold deposit items (RS-004 will give it per-cell patches), or -1.</summary>
    public int Deposit { get; }

    /// <summary>Layers marked <see cref="LayerDef.Floor"/>.</summary>
    public IReadOnlyList<int> Floors { get; }

    /// <summary>The layer buildings of <paramref name="kind"/> live on, or -1 (also for kinds that aren't layer kinds).</summary>
    public int Of(BuildingKind kind) => _byKind[(int)kind];

    /// <summary>Layer and entry of a layer building, or (-1, 0) for a building of the building grid.</summary>
    public (int Layer, int Entry) Of(BuildingDef def) => _ofBuilding.GetValueOrDefault(def.Id, (-1, 0));

    /// <summary>The layer with this id, or null.</summary>
    public Layer? ById(string id) => All.FirstOrDefault(l => l.Id == id);
}
