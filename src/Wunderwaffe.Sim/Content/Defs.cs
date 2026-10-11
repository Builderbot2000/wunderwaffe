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

    /// <summary>
    /// Fires at enemies in range (CB-001): a turret on a tile, a gun on a unit. See <see cref="BuildingDef.Weapon"/>.
    /// Belts load its ammo (CB-004).
    /// </summary>
    Weapon,

    /// <summary>Does nothing but soak up damage (FB-010).</summary>
    Wall,

    /// <summary>Absorbs enemy shots entering its radius while it has charge (FB-010). See <see cref="BuildingDef.Shield"/>.</summary>
    Shield,

    /// <summary>Restores friendly hit points within its radius, paying items (CB-005). See <see cref="BuildingDef.Repair"/>.</summary>
    Repair,

    /// <summary>
    /// Holds up to <see cref="BuildingDef.Storage"/> items of any kind and unloads them onto belts leading away (FB-007).
    /// Shares stock within <see cref="BuildingDef.SupplyRadius"/> cells (FB-018). On a tile it shares the tile inventory; on a
    /// unit it adds <see cref="BuildingDef.Storage"/> to the unit's cargo.
    /// </summary>
    Storage,

    /// <summary>
    /// A floor laid on water (FB-017): not a building on the tile but a layer under its cells, like frame, that makes
    /// them buildable ground. Ownerless; removed (and refunded) from an empty cell.
    /// </summary>
    Platform,
    /// <summary>Builds a library blueprint block by block within its radius, fed by belts, and lifts units off (UD-006).</summary>
    UnitFactory,

    /// <summary>Wheels, a hull or a rotor: gives a unit its <see cref="BuildingDef.Locomotion"/> class (UD-004). Inert on a tile.</summary>
    Locomotor,

    /// <summary>Conductor (FB-005): no behaviour, but joins the power buildings it touches into one network.</summary>
    Wire,

    /// <summary>Stores its network's surplus power and gives it back when the network falls short (FB-005).</summary>
    Battery,

    /// <summary>Improves the hosts it touches (FB-016); see <see cref="BoosterDef"/>. No behaviour of its own.</summary>
    Booster,

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
/// <param name="Weapon">Weapons only: how it fires (CB-001).</param>
/// <param name="Resist">
/// Percent of each damage type this block ignores (CB-002); negative is a weakness. Missing types take full damage.
/// </param>
/// <param name="Shield">Shields only.</param>
/// <param name="Repair">Repair blocks only.</param>
/// <param name="SupplyRadius">Resource radius in cells (FB-018): cores and storage share stock with friendlies this close.</param>
/// <param name="Storage">Storage blocks: most items held on a tile (FB-007), or items added to a unit's cargo.</param>
/// <param name="OnWater">May stand on water terrain (<c>water</c> in terrain.json) as well as buildable ground (FB-017).</param>
/// <param name="Radius">Unit factories only: how far, in cells, from the factory's footprint it may build (UD-006).</param>
/// <param name="BlockCap">Unit factories only: the most frame cells a unit it builds may have (UD-006, WW-002).</param>
/// <param name="Upkeep">Items a minute this block costs while it rides on a unit (UD-013); null for none.</param>
/// <param name="Locomotion">Locomotors only: the class this block gives a unit (see <c>World.Locomotion</c>).</param>
/// <param name="Category">What boosters see the building as (FB-016): <c>drill</c>, <c>crafter</c>, <c>factory</c>...</param>
/// <param name="PowerSupply">Power made each tick: always for a core, while burning fuel for a machine (a generator).</param>
/// <param name="PowerCapacity">Batteries: power stored when full, in power-seconds.</param>
/// <param name="Discharge">Batteries: most power given back each tick.</param>
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
    WeaponDef? Weapon = null,
    Dictionary<string, int>? Resist = null,
    ShieldDef? Shield = null,
    RepairDef? Repair = null,
    int SupplyRadius = 0,
    int Storage = 0,
    bool OnWater = false,
    int Radius = 0,
    int BlockCap = 0,
    Dictionary<string, int>? Upkeep = null,
    [property: JsonConverter(typeof(SnakeCaseEnumConverter))] World.LocomotionClass? Locomotion = null,
    string? Category = null,
    int PowerSupply = 0,
    int PowerCapacity = 0,
    int Discharge = 0,
    int Range = 0,
    BoosterDef? Booster = null)
{
    /// <summary>
    /// Takes part in a tile's power network (FB-005): draws, supplies, stores or conducts. Thrusters only use power
    /// on a unit, so they are inert on a tile; a landed unit core keeps supplying, so a scout powers an outpost on a
    /// tile with no core.
    /// </summary>
    public bool IsPower =>
        Kind != BuildingKind.Thruster
        && (Power > 0 || PowerSupply > 0 || Kind is BuildingKind.Wire or BuildingKind.Battery);

    /// <summary>Width and height after <paramref name="rotation"/> quarter turns: odd turns swap them.</summary>
    public (int W, int H) Footprint(int rotation) => (rotation & 1) == 0 ? (Width, Height) : (Height, Width);

    /// <summary>Damage this block takes from a hit of <paramref name="amount"/> (CB-002): base x (100 - resist) / 100.</summary>
    public int DamageTaken(int amount, string type) =>
        Math.Max(0, amount * (100 - (Resist?.GetValueOrDefault(type) ?? 0)) / 100);
}

/// <summary>
/// How a weapon block fires (CB-001, CB-004). Hits resolve against block hitboxes in integer maths.
/// </summary>
/// <param name="Range">Reach in cells, from the block's centre.</param>
/// <param name="Damage">Base damage of one hit, before the target's resistances (CB-002).</param>
/// <param name="Cooldown">Ticks between shots.</param>
/// <param name="Type">Damage type, matched against <see cref="BuildingDef.Resist"/>.</param>
/// <param name="Speed">Projectile speed in cells per second; 0 is a beam that hits the tick it fires.</param>
/// <param name="Ammo">Item one shot uses, or null for a weapon that runs on power.</param>
/// <param name="Magazine">Shots of <see cref="Ammo"/> it holds loaded.</param>
public sealed record WeaponDef(
    int Range,
    int Damage,
    int Cooldown,
    string Type = "kinetic",
    int Speed = 0,
    string? Ammo = null,
    int Magazine = 0);

/// <summary>A shield bubble (FB-010): enemy shots crossing into <see cref="Radius"/> cells drain charge instead of hitting.</summary>
/// <param name="Capacity">Most damage it holds back before it drops.</param>
/// <param name="Regen">Charge regained per second.</param>
public sealed record ShieldDef(int Radius, int Capacity, int Regen);

/// <summary>A repair block (CB-005): heals friendly blocks within <see cref="Radius"/> cells.</summary>
/// <param name="Rate">Hit points restored per second, shared over everything in reach.</param>
/// <param name="Cost">Paid per <see cref="Per"/> hit points restored, from the tile inventory (or a unit's cargo).</param>
public sealed record RepairDef(int Radius, int Rate, Dictionary<string, int> Cost, int Per = 100);

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
/// <param name="Ground">Whether ground units can cross it (UD-004). Default: anything but water.</param>
/// <param name="Naval">Whether naval units can cross it (UD-004). Default: water only.</param>
public sealed record TerrainDef(
    string Id,
    string Name,
    string Color,
    bool Buildable = true,
    bool Water = false,
    bool River = false,
    int[]? Elevation = null,
    int[]? Moisture = null,
    Dictionary<string, int>? Deposits = null,
    bool? Ground = null,
    bool? Naval = null)
{
    /// <summary>Ground units cross it: <see cref="Ground"/>, defaulting to anything but water.</summary>
    public bool GroundPassable => Ground ?? !Water;

    /// <summary>Naval units cross it: <see cref="Naval"/>, defaulting to water.</summary>
    public bool NavalPassable => Naval ?? Water;

    public bool Contains(int elevation, int moisture) =>
        !River && InBand(Elevation, elevation) && InBand(Moisture, moisture);

    static bool InBand(int[]? band, int v) => band == null || (v >= band[0] && v <= band[1]);

    public int DepositWeight(string item) => Deposits?.GetValueOrDefault(item) ?? 0;
}
