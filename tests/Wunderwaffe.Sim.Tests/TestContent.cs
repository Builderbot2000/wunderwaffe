using System.Text.Json.Nodes;
using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.Tests;

static class TestContent
{
    static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", name));

    /// <param name="extraBuildings">Building JSON objects appended to the real list.</param>
    public static ContentDb Load(params string[] extraBuildings) => Build(power: true, extraBuildings);

    /// <summary>
    /// The real content with every building's power draw set to 0, for tests about belts, machines and ports that
    /// shouldn't need a power network laid next to every machine. Power tests use <see cref="Load"/>.
    /// </summary>
    public static ContentDb Unpowered(params string[] extraBuildings) => Build(power: false, extraBuildings);

    static ContentDb Build(bool power, string[] extraBuildings, string? buildingsJson = null, string? layersJson = null)
    {
        var buildings = JsonNode.Parse(buildingsJson ?? Read("buildings.json"), documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!.AsArray();
        if (!power)
        {
            foreach (var b in buildings)
                b!["power"] = 0;
        }
        if (buildingsJson == null)
            buildings.Add(JsonNode.Parse(Bank));
        foreach (var json in extraBuildings)
            buildings.Add(JsonNode.Parse(json));
        return ContentDb.FromJson(Read("items.json"), Read("recipes.json"), buildings.ToJsonString(), Read("goals.json"), Read("terrain.json"),
            layersJson ?? Read("layers.json"), Prefabs());
    }

    /// <summary>The real content as shipped (no test bank) with <paramref name="extra"/> prefab JSON objects added.</summary>
    public static ContentDb WithPrefabs(params string[] extra) =>
        ContentDb.FromJson(Read("items.json"), Read("recipes.json"), Read("buildings.json"), Read("goals.json"), Read("terrain.json"),
            Read("layers.json"), [.. Prefabs(), .. extra]);

    /// <summary>The texts of every shipped prefab (FB-021).</summary>
    public static IEnumerable<string> Prefabs() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "data", "prefabs"), "*.json").Order(StringComparer.Ordinal).Select(File.ReadAllText);

    /// <summary>A plain 3x3 host in the <c>factory</c> category, standing in for the unit factory in booster tests.</summary>
    public const string TestFactory =
        """{ "id": "test_factory", "name": "Test Factory", "menu": "production", "kind": "wall", "category": "factory", "width": 3, "height": 3, "power": 0, "hp": 100, "cost": {} }""";

    /// <summary>
    /// A test-only 1x1 core with no power and room for anything: the funds of hand-built test tiles
    /// (<see cref="Harness.Bank"/>), kept out of power networks and layouts.
    /// </summary>
    public const string Bank =
        """{ "id": "bank", "name": "Bank", "menu": "logistics", "kind": "core", "width": 1, "height": 1, "power": 0, "capacity": 1000000000, "hp": 1000, "cost": { "copper_ore": 1 } }""";

    /// <summary>
    /// The shipped content (no test bank) with every item, recipe and building id replaced by an opaque one (<c>i0</c>,
    /// <c>r0</c>, <c>b0</c>... in file order), consistently in every file: recipes, costs, ammo, goals, terrain deposit
    /// weights and prefabs. Kinds, categories, layers and prefab ids stay. MP-007: code that names content ids fails on it.
    /// </summary>
    public static ContentDb Renamed()
    {
        var options = new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip };
        JsonArray Parse(string text) => JsonNode.Parse(text, documentOptions: options)!.AsArray();
        var items = Parse(Read("items.json"));
        var recipes = Parse(Read("recipes.json"));
        var buildings = Parse(Read("buildings.json"));
        var goals = Parse(Read("goals.json"));
        var terrain = Parse(Read("terrain.json"));
        var prefabs = Prefabs().Select(p => JsonNode.Parse(p, documentOptions: options)!.AsObject()).ToList();

        Dictionary<string, string> Ids(JsonArray list, string prefix) =>
            list.Select((n, i) => ((string)n!["id"]!, $"{prefix}{i}")).ToDictionary(p => p.Item1, p => p.Item2);
        var item = Ids(items, "i");
        var recipe = Ids(recipes, "r");
        var building = Ids(buildings, "b");

        static void Set(JsonNode? node, string key, Dictionary<string, string> map)
        {
            if (node?[key] is JsonValue v && map.TryGetValue((string)v!, out var to))
                node[key] = to;
        }
        static void Keys(JsonNode? node, string key, Dictionary<string, string> map)
        {
            if (node?[key] is not JsonObject old)
                return;
            var renamed = new JsonObject();
            foreach (var (k, v) in old.ToList())
            {
                old.Remove(k);
                renamed[map.GetValueOrDefault(k, k)] = v;
            }
            node[key] = renamed;
        }

        foreach (var n in items)
            Set(n, "id", item);
        foreach (var n in recipes)
        {
            Set(n, "id", recipe);
            Set(n, "building", building);
            Keys(n, "inputs", item);
            Keys(n, "outputs", item);
        }
        foreach (var n in buildings)
        {
            Set(n, "id", building);
            Keys(n, "cost", item);
            Set(n?["weapon"], "ammo", item);
            Keys(n?["repair"], "cost", item);
        }
        foreach (var n in goals)
            Set(n?["condition"], "item", item);
        foreach (var n in terrain)
            Keys(n, "deposits", item);
        foreach (var p in prefabs)
        {
            Keys(p, "items", item);
            foreach (var b in p["blocks"]!.AsArray())
                Set(b, "defId", building);
        }
        return ContentDb.FromJson(items.ToJsonString(), recipes.ToJsonString(), buildings.ToJsonString(), goals.ToJsonString(),
            terrain.ToJsonString(), Read("layers.json"), prefabs.Select(p => p.ToJsonString()));
    }

    /// <summary>
    /// The real content plus the test bank core, with buildings.json edited by <paramref name="editBuildings"/>, which must
    /// change it; without power draws if not <paramref name="power"/>. <paramref name="editLayers"/>, if given, edits
    /// layers.json the same way.
    /// </summary>
    public static ContentDb Load(Func<string, string> editBuildings, bool power = true, Func<string, string>? editLayers = null)
    {
        string real = Read("buildings.json").TrimEnd();
        string buildings = real[..^1].TrimEnd() + ",\n" + Bank + "\n]";
        string edited = editBuildings(buildings);
        if (edited == buildings)
            throw new InvalidOperationException("the edit changed nothing; buildings.json has moved on");
        string? layers = null;
        if (editLayers != null && (layers = editLayers(Read("layers.json"))) == Read("layers.json"))
            throw new InvalidOperationException("the layers edit changed nothing; layers.json has moved on");
        return Build(power, [], edited, layers);
    }
}
