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

    static ContentDb Build(bool power, string[] extraBuildings, string? buildingsJson = null)
    {
        var buildings = JsonNode.Parse(buildingsJson ?? Read("buildings.json"), documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!.AsArray();
        if (!power)
        {
            foreach (var b in buildings)
                b!["power"] = 0;
        }
        foreach (var json in extraBuildings)
            buildings.Add(JsonNode.Parse(json));
        return ContentDb.FromJson(Read("items.json"), Read("recipes.json"), buildings.ToJsonString(), Read("goals.json"), Read("terrain.json"));
    }

    /// <summary>A plain 3x3 host in the <c>factory</c> category, standing in for the unit factory in booster tests.</summary>
    public const string TestFactory =
        """{ "id": "test_factory", "name": "Test Factory", "menu": "production", "kind": "core", "category": "factory", "width": 3, "height": 3, "power": 0, "hp": 100, "cost": {} }""";

    /// <summary>
    /// The real content with buildings.json edited by <paramref name="editBuildings"/>, which must change it; without
    /// power draws if not <paramref name="power"/>.
    /// </summary>
    public static ContentDb Load(Func<string, string> editBuildings, bool power = true)
    {
        string buildings = Read("buildings.json"), edited = editBuildings(buildings);
        if (edited == buildings)
            throw new InvalidOperationException("the edit changed nothing; buildings.json has moved on");
        return Build(power, [], edited);
    }
}
