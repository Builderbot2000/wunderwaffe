using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wunderwaffe.Sim.Content;

/// <summary>An item and its resource tier (RS-001): 1 raw, 2 refined, 3 advanced, 4 exotic.</summary>
/// <param name="Regional">
/// Deposits only appear on some tiles, never the starting one, so players must expand to get it (RS-003).
/// </param>
public sealed record ItemDef(string Id, string Name, int Tier, bool Regional = false);

/// <summary>Converts inputs to outputs over <see cref="Ticks"/> in a given building (RS-002). Empty inputs = extraction.</summary>
public sealed record RecipeDef(
    string Id,
    string Building,
    int Ticks,
    Dictionary<string, int> Inputs,
    Dictionary<string, int> Outputs);

/// <summary>How a building behaves in the tile's entity simulation. JSON names are snake_case.</summary>
public enum BuildingKind
{
    /// <summary>Runs recipes (drills, smelters, mills). The default.</summary>
    Machine,

    /// <summary>The tile's core: anything delivered to it goes into the tile inventory.</summary>
    Core,

    /// <summary>Carries items one cell in the direction it faces (FB-004).</summary>
    Conveyor,

    /// <summary>Takes items from a belt and sends them to the neighbouring tile whose I/O zone it stands in (IO-002).</summary>
    ExportPort,

    /// <summary>Receives items from the neighbouring tile whose I/O zone it stands in and puts them on adjacent belts.</summary>
    ImportPort,

    /// <summary>Marks a unit: triggering it turns the framed blocks around it into a mobile unit.</summary>
    UnitCore,

    /// <summary>Pushes a unit the way it faces while the unit is active. Inert on a tile.</summary>
    Thruster,

    /// <summary>Conductor (FB-005): no behaviour, but joins the power buildings it touches into one network.</summary>
    Wire,

    /// <summary>Stores its network's surplus power and gives it back when the network falls short (FB-005).</summary>
    Battery,

    /// <summary>Improves the hosts it touches (FB-016); see <see cref="BoosterDef"/>. No behaviour of its own.</summary>
    Booster,

    /// <summary>Holds up to <see cref="BuildingDef.Storage"/> items and passes them on to its neighbours (FB-007).</summary>
    Storage,

    /// <summary>Passes items straight across, so two lines can cross (FB-004).</summary>
    Junction,

    /// <summary>Hands each item to its neighbours in turn, never back where it came from (FB-004).</summary>
    Router,

    /// <summary>Lets its filter item through straight ahead and turns everything else aside (FB-004).</summary>
    Sorter,

    /// <summary>Carries items over up to <see cref="BuildingDef.Range"/> cells to the next bridge ahead (FB-004).</summary>
    Bridge,
}

/// <summary>
/// What a booster does to the hosts it touches (FB-016, OQ-25). A booster boosts every matching host it touches
/// (up to <see cref="MaxHosts"/>); a host counts at most <see cref="MaxPerHost"/> boosters of one type.
/// </summary>
/// <param name="Hosts">Host <see cref="BuildingDef.Category"/> values it works with.</param>
/// <param name="Effects">
/// Stat to amount, added per booster; see <see cref="Stats"/>. <c>speed</c>, <c>output</c>, <c>power</c>,
/// <c>build_speed</c> and <c>input_buffer</c> are percent of base; <c>radius</c> is cells, <c>block_cap</c> blocks,
/// <c>input_sides</c> extra sides; <c>precision</c> and <c>cooling</c> are flags (1 = has it).
/// </param>
/// <param name="MaxPerHost">Most boosters of this type one host counts, lowest id first; 0 = no limit.</param>
/// <param name="MaxHosts">Most hosts one booster serves, lowest id first; 0 = every host it touches.</param>
/// <param name="Chain">
/// Counts only in a straight line running outward from the host: the first touches the host end-on, each next one
/// continues the line end to end (Gantry Rail). Chained boosters don't need to touch the host.
/// </param>
/// <param name="HalfWithout">Effects are halved unless the booster is itself boosted with this stat (Servo Bank needs <c>cooling</c>).</param>
public sealed record BoosterDef(
    string[] Hosts,
    Dictionary<string, int> Effects,
    int MaxPerHost = 0,
    int MaxHosts = 0,
    bool Chain = false,
    string? HalfWithout = null)
{
    public static readonly IReadOnlyList<string> Stats =
        ["speed", "output", "power", "build_speed", "input_buffer", "radius", "block_cap", "input_sides", "precision", "cooling"];
}

public sealed class SnakeCaseEnumConverter() : JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower);

/// <param name="Power">
/// Power drawn each tick (FB-005): by a machine while it works, by anything else always. Power is an abstract
/// per-tick unit shared with <see cref="PowerSupply"/> and <see cref="Discharge"/>.
/// </param>
/// <param name="Hp">Hit points of one placed block (one building, wherever it stands: tile or unit). Must be &gt; 0.</param>
/// <param name="Speed">Conveyors only: how far an item moves along a cell each tick, out of <c>BeltState.CellLength</c>.</param>
/// <param name="Thrust">Thrusters only: force on the unit while it is active.</param>
/// <param name="Category">What boosters see the building as (FB-016): <c>drill</c>, <c>crafter</c>, <c>factory</c>...</param>
/// <param name="PowerSupply">Power made each tick: always for a core, while burning fuel for a machine (a generator).</param>
/// <param name="PowerCapacity">Batteries: power stored when full, in power-seconds.</param>
/// <param name="Discharge">Batteries: most power given back each tick.</param>
/// <param name="Storage">Storage buildings: most items held.</param>
/// <param name="Range">Bridges: the farthest cell ahead the next bridge may stand on.</param>
/// <param name="Booster">Boosters: what it does to its hosts.</param>
public sealed record BuildingDef(
    string Id,
    string Name,
    int Width,
    int Height,
    int Power,
    Dictionary<string, int> Cost,
    int Hp,
    [property: JsonConverter(typeof(SnakeCaseEnumConverter))] BuildingKind Kind = BuildingKind.Machine,
    int Speed = 0,
    int Thrust = 0,
    string? Category = null,
    int PowerSupply = 0,
    int PowerCapacity = 0,
    int Discharge = 0,
    int Storage = 0,
    int Range = 0,
    BoosterDef? Booster = null)
{
    /// <summary>Takes part in a power network (FB-005): draws, supplies, stores or conducts.</summary>
    public bool IsPower => Power > 0 || PowerSupply > 0 || Kind is BuildingKind.Wire or BuildingKind.Battery;

    /// <summary>Width and height after <paramref name="rotation"/> quarter turns: odd turns swap them.</summary>
    public (int W, int H) Footprint(int rotation) => (rotation & 1) == 0 ? (Width, Height) : (Height, Width);
}

/// <summary>
/// What a player must have done for a goal to complete. Counts are cumulative per player:
/// <list type="bullet">
/// <item><c>produce</c>: <see cref="Item"/> made by machines, extraction included.</item>
/// <item><c>claim</c>: tiles owned at once.</item>
/// <item><c>ship</c>: items that arrived through import ports.</item>
/// <item><c>launch</c>: units lifted off.</item>
/// </list>
/// </summary>
public sealed record UnlockCondition(string Type, string? Item = null, int Amount = 1)
{
    public static readonly IReadOnlyList<string> Types = ["produce", "claim", "ship", "launch"];
}

/// <summary>One step of the guided objective chain. Goals complete in file order.</summary>
public sealed record GoalDef(string Id, string Name, string Description, UnlockCondition Condition);

/// <summary>
/// A kind of ground (WM-002, FB-002). World generation classifies each cell into the first terrain, in file order,
/// whose <see cref="Elevation"/> and <see cref="Moisture"/> bands (inclusive, 0-1000) contain it. Elevations below
/// <c>TerrainGen.SeaLevel</c> are under water.
/// </summary>
/// <param name="Color">Map colour, <c>#rrggbb</c>.</param>
/// <param name="Buildable">Whether buildings and frame may stand on it.</param>
/// <param name="Water">Counts as water for coasts, moisture and a tile's land share.</param>
/// <param name="River">Used for river cells instead of the bands. At most one terrain should set it.</param>
/// <param name="Deposits">Relative chance, per minable item, that a deposit patch of it is centred on this terrain.</param>
public sealed record TerrainDef(
    string Id,
    string Name,
    string Color,
    bool Buildable = true,
    bool Water = false,
    bool River = false,
    int[]? Elevation = null,
    int[]? Moisture = null,
    Dictionary<string, int>? Deposits = null)
{
    public bool Contains(int elevation, int moisture) =>
        !River && InBand(Elevation, elevation) && InBand(Moisture, moisture);

    static bool InBand(int[]? band, int v) => band == null || (v >= band[0] && v <= band[1]);

    public int DepositWeight(string item) => Deposits?.GetValueOrDefault(item) ?? 0;
}
