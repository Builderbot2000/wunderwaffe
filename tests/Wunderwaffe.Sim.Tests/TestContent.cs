using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.Tests;

static class TestContent
{
    static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", name));

    public static ContentDb Load() =>
        ContentDb.FromJson(Read("items.json"), Read("recipes.json"), Read("buildings.json"), Read("tech.json"), Read("goals.json"), Read("terrain.json"));
}
