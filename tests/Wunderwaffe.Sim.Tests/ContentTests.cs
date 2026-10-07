using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.Tests;

public class ContentTests
{
    const string Items = """
        [ { "id": "ore", "name": "Ore", "tier": 1 },
          { "id": "plate", "name": "Plate", "tier": 2 },
          { "id": "exotic", "name": "Exotic", "tier": 4 } ]
        """;
    const string Buildings = """[ { "id": "smelter", "name": "Smelter", "width": 2, "height": 2, "power": 0, "cost": {} } ]""";

    [Fact]
    public void GameContentLoads()
    {
        ContentDb db = TestContent.Load();
        Assert.NotEmpty(db.Items);
        Assert.NotEmpty(db.Recipes);
    }

    [Fact]
    public void RejectsSkippingTiers()
    {
        const string recipes = """
            [ { "id": "cheat", "building": "smelter", "ticks": 60, "inputs": { "ore": 1 }, "outputs": { "exotic": 1 } } ]
            """;
        var ex = Assert.Throws<ContentException>(() => ContentDb.FromJson(Items, recipes, Buildings, "[]"));
        Assert.Contains(ex.Errors, e => e.Contains("tier 4"));
    }

    [Fact]
    public void RejectsUnknownReferences()
    {
        const string recipes = """
            [ { "id": "r", "building": "nope", "ticks": 60, "inputs": { "ghost": 1 }, "outputs": { "plate": 1 } } ]
            """;
        var ex = Assert.Throws<ContentException>(() => ContentDb.FromJson(Items, recipes, Buildings, "[]"));
        Assert.Contains(ex.Errors, e => e.Contains("unknown building 'nope'"));
        Assert.Contains(ex.Errors, e => e.Contains("unknown item 'ghost'"));
    }
}
