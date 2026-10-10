using System.Text.Json;

namespace Wunderwaffe.Sim.Content;

public sealed class ContentException(IReadOnlyList<string> errors)
    : Exception("Invalid content:\n" + string.Join("\n", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>
/// All game content, loaded from JSON text so the simulation stays independent of Godot's file system (NF-003).
/// </summary>
public sealed class ContentDb
{
    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public IReadOnlyDictionary<string, ItemDef> Items { get; }
    public IReadOnlyDictionary<string, RecipeDef> Recipes { get; }
    public IReadOnlyDictionary<string, BuildingDef> Buildings { get; }
    public IReadOnlyDictionary<string, TechDef> Tech { get; }

    /// <summary>Buildings in file order, which is the order the build menu shows them in.</summary>
    public IReadOnlyList<BuildingDef> BuildingList { get; }

    /// <summary>Tech nodes in file order: roughly the order a player reaches them, and the order they are checked in.</summary>
    public IReadOnlyList<TechDef> TechList { get; }

    /// <summary>The guided objective chain, in the order goals must be completed.</summary>
    public IReadOnlyList<GoalDef> Goals { get; }

    /// <summary>The tech node that unlocks each building or recipe id. Ids not in here are starter content (TT-004).</summary>
    public IReadOnlyDictionary<string, string> UnlockedBy { get; }

    /// <summary>Recipes each building can run, sorted by recipe id.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<RecipeDef>> RecipesByBuilding { get; }

    ContentDb(
        Dictionary<string, ItemDef> items,
        Dictionary<string, RecipeDef> recipes,
        List<BuildingDef> buildingList,
        Dictionary<string, BuildingDef> buildings,
        List<TechDef> techList,
        Dictionary<string, TechDef> tech,
        List<GoalDef> goals)
    {
        Items = items;
        Recipes = recipes;
        Buildings = buildings;
        BuildingList = buildingList;
        Tech = tech;
        TechList = techList;
        Goals = goals;
        var unlockedBy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var t in TechList)
        {
            foreach (var id in t.Unlocks)
                unlockedBy.TryAdd(id, t.Id);
        }
        UnlockedBy = unlockedBy;
        RecipesByBuilding = recipes.Values
            .GroupBy(r => r.Building)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<RecipeDef>)g.OrderBy(r => r.Id, StringComparer.Ordinal).ToList());
    }

    /// <param name="goals">The objective chain; null for none.</param>
    public static ContentDb FromJson(string items, string recipes, string buildings, string tech, string? goals = null)
    {
        var errors = new List<string>();
        var buildingList = Parse<BuildingDef>(buildings, "buildings");
        var techList = Parse<TechDef>(tech, "tech");
        var goalList = goals == null ? [] : Parse<GoalDef>(goals, "goals");
        Index(goalList, x => x.Id, "goal", errors);
        var db = new ContentDb(
            Index(Parse<ItemDef>(items, "items"), x => x.Id, "item", errors),
            Index(Parse<RecipeDef>(recipes, "recipes"), x => x.Id, "recipe", errors),
            buildingList,
            Index(buildingList, x => x.Id, "building", errors),
            techList,
            Index(techList, x => x.Id, "tech", errors),
            goalList);
        db.Validate(errors);
        if (errors.Count > 0)
            throw new ContentException(errors);
        return db;
    }

    static List<T> Parse<T>(string json, string file) =>
        JsonSerializer.Deserialize<List<T>>(json, Options)
        ?? throw new ContentException([$"{file}: expected a JSON array"]);

    static Dictionary<string, T> Index<T>(List<T> list, Func<T, string> id, string kind, List<string> errors)
    {
        var map = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var x in list)
        {
            if (!map.TryAdd(id(x), x))
                errors.Add($"duplicate {kind} id '{id(x)}'");
        }
        return map;
    }

    void Validate(List<string> errors)
    {
        foreach (var r in Recipes.Values)
        {
            if (!Buildings.TryGetValue(r.Building, out var host))
                errors.Add($"recipe '{r.Id}': unknown building '{r.Building}'");
            else if (host.Kind != BuildingKind.Machine)
                errors.Add($"recipe '{r.Id}': building '{r.Building}' is a {host.Kind}, not a machine");
            if (r.Ticks <= 0)
                errors.Add($"recipe '{r.Id}': ticks must be > 0");
            foreach (var id in r.Inputs.Keys.Concat(r.Outputs.Keys))
            {
                if (!Items.ContainsKey(id))
                    errors.Add($"recipe '{r.Id}': unknown item '{id}'");
            }

            // RS-001: an item of tier N may only be made from inputs of at least tier N-1.
            if (r.Inputs.Count == 0)
                continue;
            int maxIn = r.Inputs.Keys.Where(Items.ContainsKey).Select(id => Items[id].Tier).DefaultIfEmpty(0).Max();
            foreach (var id in r.Outputs.Keys.Where(Items.ContainsKey))
            {
                if (Items[id].Tier > maxIn + 1)
                    errors.Add($"recipe '{r.Id}': '{id}' is tier {Items[id].Tier} but best input is tier {maxIn}");
            }
        }

        foreach (var b in Buildings.Values)
        {
            foreach (var id in b.Cost.Keys.Where(id => !Items.ContainsKey(id)))
                errors.Add($"building '{b.Id}': unknown cost item '{id}'");
            if (b.Kind == BuildingKind.Conveyor && b.Speed <= 0)
                errors.Add($"building '{b.Id}': a conveyor needs a speed > 0");
            if (b.Kind == BuildingKind.Thruster && (b.Thrust <= 0 || b.Width != 1 || b.Height != 1))
                errors.Add($"building '{b.Id}': a thruster must be 1x1 with thrust > 0");
            if (b.Kind is BuildingKind.ExportPort or BuildingKind.ImportPort or BuildingKind.UnitCore && (b.Width != 1 || b.Height != 1))
                errors.Add($"building '{b.Id}': ports must be 1x1");
        }

        var unlockedBy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var t in TechList)
        {
            ValidateCondition(t.Condition, $"tech '{t.Id}'", errors);
            foreach (var id in t.Unlocks)
            {
                if (!Buildings.ContainsKey(id) && !Recipes.ContainsKey(id))
                    errors.Add($"tech '{t.Id}': unlocks unknown building/recipe '{id}'");
                else if (!unlockedBy.TryAdd(id, t.Id))
                    errors.Add($"tech '{t.Id}': '{id}' is already unlocked by '{unlockedBy[id]}'");
            }
        }

        foreach (var g in Goals)
            ValidateCondition(g.Condition, $"goal '{g.Id}'", errors);
    }

    void ValidateCondition(UnlockCondition c, string owner, List<string> errors)
    {
        if (!UnlockCondition.Types.Contains(c.Type))
            errors.Add($"{owner}: unknown condition type '{c.Type}'");
        else if (c.Type == "produce" && (c.Item == null || !Items.ContainsKey(c.Item)))
            errors.Add($"{owner}: unknown condition item '{c.Item}'");
        if (c.Amount <= 0)
            errors.Add($"{owner}: condition amount must be > 0");
    }
}
