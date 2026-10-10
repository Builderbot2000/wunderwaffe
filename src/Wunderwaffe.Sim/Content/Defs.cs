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

    /// <summary>Builds a library blueprint block by block within its radius, fed by belts, and lifts units off (UD-006).</summary>
    UnitFactory,
}

public sealed class SnakeCaseEnumConverter() : JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower);

/// <param name="Hp">Hit points of one placed block (one building, wherever it stands: tile or unit). Must be &gt; 0.</param>
/// <param name="Speed">Conveyors only: how far an item moves along a cell each tick, out of <c>BeltState.CellLength</c>.</param>
/// <param name="Thrust">Thrusters only: force on the unit while it is active.</param>
/// <param name="Radius">Unit factories only: how far, in cells, from the factory's footprint it may build (UD-006).</param>
/// <param name="BlockCap">Unit factories only: the most frame cells a unit it builds may have (UD-006, WW-002).</param>
/// <param name="Upkeep">Items a minute this block costs while it rides on a unit (UD-013); null for none.</param>
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
    int Radius = 0,
    int BlockCap = 0,
    Dictionary<string, int>? Upkeep = null)
{
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
