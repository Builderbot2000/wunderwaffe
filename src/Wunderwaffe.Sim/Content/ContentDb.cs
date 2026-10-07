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

    /// <summary>Recipes each building can run, sorted by recipe id.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<RecipeDef>> RecipesByBuilding { get; }

    ContentDb(
        Dictionary<string, ItemDef> items,
        Dictionary<string, RecipeDef> recipes,
        Dictionary<string, BuildingDef> buildings,
        Dictionary<string, TechDef> tech)
    {
        Items = items;
        Recipes = recipes;
        Buildings = buildings;
        Tech = tech;
        RecipesByBuilding = recipes.Values
            .GroupBy(r => r.Building)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<RecipeDef>)g.OrderBy(r => r.Id, StringComparer.Ordinal).ToList());
    }

    public static ContentDb FromJson(string items, string recipes, string buildings, string tech)
    {
        var errors = new List<string>();
        var db = new ContentDb(
            Index(Parse<ItemDef>(items, "items"), x => x.Id, "item", errors),
            Index(Parse<RecipeDef>(recipes, "recipes"), x => x.Id, "recipe", errors),
            Index(Parse<BuildingDef>(buildings, "buildings"), x => x.Id, "building", errors),
            Index(Parse<TechDef>(tech, "tech"), x => x.Id, "tech", errors));
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
            if (!Buildings.ContainsKey(r.Building))
                errors.Add($"recipe '{r.Id}': unknown building '{r.Building}'");
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
        }

        foreach (var t in Tech.Values)
        {
            if (!Items.ContainsKey(t.Condition.Item))
                errors.Add($"tech '{t.Id}': unknown condition item '{t.Condition.Item}'");
            foreach (var id in t.Unlocks.Where(id => !Buildings.ContainsKey(id) && !Recipes.ContainsKey(id)))
                errors.Add($"tech '{t.Id}': unlocks unknown building/recipe '{id}'");
        }
    }
}
