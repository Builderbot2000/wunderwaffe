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

    /// <summary>Takes items from a belt and sends them to the neighbouring tile across the edge it faces (IO-002).</summary>
    ExportPort,

    /// <summary>Receives items from the neighbouring tile across the edge it faces and puts them on adjacent belts.</summary>
    ImportPort,

    /// <summary>Marks a unit: triggering it turns the framed blocks around it into a mobile unit.</summary>
    UnitCore,

    /// <summary>Pushes a unit the way it faces while the unit is active. Inert on a tile.</summary>
    Thruster,
}

public sealed class SnakeCaseEnumConverter() : JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower);

/// <param name="Speed">Conveyors only: how far an item moves along a cell each tick, out of <c>BeltState.CellLength</c>.</param>
/// <param name="Thrust">Thrusters only: force on the unit while it is active.</param>
public sealed record BuildingDef(
    string Id,
    string Name,
    int Width,
    int Height,
    int Power,
    Dictionary<string, int> Cost,
    [property: JsonConverter(typeof(SnakeCaseEnumConverter))] BuildingKind Kind = BuildingKind.Machine,
    int Speed = 0,
    int Thrust = 0);

/// <summary>
/// What a player must have done for a tech node or goal to complete (TT-002). Counts are cumulative per player:
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

/// <summary>Tech node unlocked by acquiring resources, not science points (TT-001).</summary>
/// <param name="Unlocks">Building and recipe ids. Content no node unlocks is available from the start (TT-004).</param>
public sealed record TechDef(string Id, string Name, UnlockCondition Condition, List<string> Unlocks);

/// <summary>One step of the guided objective chain. Goals complete in file order.</summary>
public sealed record GoalDef(string Id, string Name, string Description, UnlockCondition Condition);
