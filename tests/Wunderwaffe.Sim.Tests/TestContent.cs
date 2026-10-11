using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.Tests;

static class TestContent
{
    static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", name));

    public static ContentDb Load() =>
        ContentDb.FromJson(Read("items.json"), Read("recipes.json"), Read("buildings.json"), Read("goals.json"), Read("terrain.json"));

    /// <summary>The real content with buildings.json edited by <paramref name="editBuildings"/>, which must change it.</summary>
    public static ContentDb Load(Func<string, string> editBuildings)
    {
        string buildings = Read("buildings.json"), edited = editBuildings(buildings);
        if (edited == buildings)
            throw new InvalidOperationException("the edit changed nothing; buildings.json has moved on");
        return ContentDb.FromJson(Read("items.json"), Read("recipes.json"), edited, Read("goals.json"), Read("terrain.json"));
    }
}
