namespace Wunderwaffe.Sim.Content;

/// <summary>An item and its resource tier (RS-001): 1 raw, 2 refined, 3 advanced, 4 exotic.</summary>
public sealed record ItemDef(string Id, string Name, int Tier);

/// <summary>Converts inputs to outputs over <see cref="Ticks"/> in a given building (RS-002). Empty inputs = extraction.</summary>
public sealed record RecipeDef(
    string Id,
    string Building,
    int Ticks,
    Dictionary<string, int> Inputs,
    Dictionary<string, int> Outputs);

public sealed record BuildingDef(
    string Id,
    string Name,
    int Width,
    int Height,
    int Power,
    Dictionary<string, int> Cost);

/// <summary>Resource-based unlock trigger (TT-002). Type is currently only "produce".</summary>
public sealed record UnlockCondition(string Type, string Item, int Amount);

/// <summary>Tech node unlocked by acquiring resources, not science points (TT-001).</summary>
public sealed record TechDef(string Id, string Name, UnlockCondition Condition, List<string> Unlocks);
