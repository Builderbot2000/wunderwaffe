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

    /// <summary>Buildings in file order, which is the order the build menu shows them in.</summary>
    public IReadOnlyList<BuildingDef> BuildingList { get; }

    /// <summary>The guided objective chain, in the order goals must be completed.</summary>
    public IReadOnlyList<GoalDef> Goals { get; }

    /// <summary>Terrains in file order, which is classification order. A cell stores its terrain's index here.</summary>
    public IReadOnlyList<TerrainDef> TerrainList { get; }

    /// <summary>
    /// Index of the terrain hand-built tiles are laid on: the first buildable land terrain with no bands (the catch-all),
    /// or else the first buildable land terrain.
    /// </summary>
    public int DefaultTerrain { get; }

    /// <summary>Index of the terrain rivers use, or -1 if no terrain is marked as river.</summary>
    public int RiverTerrain { get; }

    /// <summary>Used when no terrain file is given: one buildable terrain everywhere.</summary>
    static readonly TerrainDef FallbackTerrain = new("plains", "Plains", "#6b8f47");

    /// <summary>Recipes each building can run, sorted by recipe id.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<RecipeDef>> RecipesByBuilding { get; }

    ContentDb(
        Dictionary<string, ItemDef> items,
        Dictionary<string, RecipeDef> recipes,
        List<BuildingDef> buildingList,
        Dictionary<string, BuildingDef> buildings,
        List<GoalDef> goals,
        List<TerrainDef> terrain)
    {
        Items = items;
        Recipes = recipes;
        Buildings = buildings;
        BuildingList = buildingList;
        Goals = goals;
        TerrainList = terrain;
        static bool Land(TerrainDef t) => t.Buildable && !t.Water && !t.River;
        int catchAll = terrain.FindIndex(t => Land(t) && t.Elevation == null && t.Moisture == null);
        DefaultTerrain = catchAll >= 0 ? catchAll : Math.Max(0, terrain.FindIndex(Land));
        RiverTerrain = terrain.FindIndex(t => t.River);
        RecipesByBuilding = recipes.Values
            .GroupBy(r => r.Building)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<RecipeDef>)g.OrderBy(r => r.Id, StringComparer.Ordinal).ToList());
    }

    /// <param name="goals">The objective chain; null for none.</param>
    /// <param name="terrain">Terrain kinds for world generation; null for a single buildable terrain.</param>
    public static ContentDb FromJson(string items, string recipes, string buildings, string? goals = null, string? terrain = null)
    {
        var errors = new List<string>();
        var buildingList = Parse<BuildingDef>(buildings, "buildings");
        var goalList = goals == null ? [] : Parse<GoalDef>(goals, "goals");
        Index(goalList, x => x.Id, "goal", errors);
        var terrainList = terrain == null ? [FallbackTerrain] : Parse<TerrainDef>(terrain, "terrain");
        Index(terrainList, x => x.Id, "terrain", errors);
        var db = new ContentDb(
            Index(Parse<ItemDef>(items, "items"), x => x.Id, "item", errors),
            Index(Parse<RecipeDef>(recipes, "recipes"), x => x.Id, "recipe", errors),
            buildingList,
            Index(buildingList, x => x.Id, "building", errors),
            goalList,
            terrainList);
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
            if (b.Hp <= 0)
                errors.Add($"building '{b.Id}': hp must be > 0");
            if (b.Kind == BuildingKind.Platform && (!b.OnWater || b.Width != 1 || b.Height != 1))
                errors.Add($"building '{b.Id}': a platform must be 1x1 and onWater");
            if (b.Kind == BuildingKind.UnitFactory && (b.Radius <= 0 || b.BlockCap <= 0))
                errors.Add($"building '{b.Id}': a unit factory needs radius and blockCap > 0");
            foreach (var (id, n) in b.Upkeep ?? [])
            {
                if (!Items.ContainsKey(id) || n <= 0)
                    errors.Add($"building '{b.Id}': upkeep '{id}' must be a known item and > 0");
            }
            if ((b.Kind == BuildingKind.Locomotor) != (b.Locomotion != null))
                errors.Add($"building '{b.Id}': a locomotor needs a locomotion class, and only locomotors have one");
        }
        if (Buildings.Values.Count(b => b.Kind == BuildingKind.Platform) > 1)
            errors.Add("buildings: at most one platform");
        foreach (var g in Goals)
            ValidateCondition(g.Condition, $"goal '{g.Id}'", errors);

        ValidateTerrain(errors);
    }

    void ValidateTerrain(List<string> errors)
    {
        if (TerrainList.Count == 0)
            errors.Add("terrain: at least one terrain is needed");
        if (TerrainList.Count > 255)
            errors.Add("terrain: at most 255 terrains");
        if (!TerrainList.Any(t => t.Buildable && !t.Water && !t.River))
            errors.Add("terrain: no buildable land terrain");
        if (TerrainList.Count(t => t.River) > 1)
            errors.Add("terrain: more than one river terrain");

        var minable = Recipes.Values.Where(r => r.Inputs.Count == 0).SelectMany(r => r.Outputs.Keys).ToHashSet();
        foreach (var t in TerrainList)
        {
            if (t.Color.Length != 7 || t.Color[0] != '#' || !t.Color.Skip(1).All(Uri.IsHexDigit))
                errors.Add($"terrain '{t.Id}': colour must be #rrggbb");
            foreach (var band in new[] { t.Elevation, t.Moisture })
            {
                if (band != null && (band.Length != 2 || band[0] > band[1]))
                    errors.Add($"terrain '{t.Id}': a band is [min, max]");
            }
            foreach (var (item, w) in t.Deposits ?? [])
            {
                if (!minable.Contains(item))
                    errors.Add($"terrain '{t.Id}': deposit '{item}' is not a minable item");
                if (w < 0)
                    errors.Add($"terrain '{t.Id}': deposit weight must be >= 0");
            }
        }

        // Every elevation and moisture must classify, or generation would leave holes.
        for (int e = 0; e <= 1000; e += 5)
        {
            for (int m = 0; m <= 1000; m += 5)
            {
                if (ClassifyOrNull(e, m) == null)
                {
                    errors.Add($"terrain: nothing covers elevation {e}, moisture {m}");
                    return;
                }
            }
        }
    }

    int? ClassifyOrNull(int elevation, int moisture)
    {
        for (int i = 0; i < TerrainList.Count; i++)
        {
            if (TerrainList[i].Contains(elevation, moisture))
                return i;
        }
        return null;
    }

    /// <summary>The terrain index for a cell: the first terrain whose bands contain it.</summary>
    public int Classify(int elevation, int moisture) => ClassifyOrNull(elevation, moisture) ?? DefaultTerrain;

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
