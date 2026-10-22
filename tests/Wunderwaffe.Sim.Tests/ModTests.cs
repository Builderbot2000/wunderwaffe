using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Tests;

/// <summary>NF-006: content mods merge by id into the shipped content.</summary>
public class ModTests
{
    static string Data(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", name));

    static ContentSources Shipped() => new(
        ContentSources.ListFiles.ToDictionary(f => f, Data), [.. TestContent.Prefabs()], []);

    static ModDef Example()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "mods", "example");
        var lists = Directory.GetFiles(dir, "*.json").Where(f => Path.GetFileName(f) != "mod.json")
            .ToDictionary(f => Path.GetFileName(f), File.ReadAllText);
        return ModDef.Parse(File.ReadAllText(Path.Combine(dir, "mod.json")), lists, [], []);
    }

    [Fact]
    public void ExampleModAddsABlockThatCanBePlaced()
    {
        // The test bank comes in as a second mod, the way any content would.
        var bank = new ModDef("bank", "Bank", new Dictionary<string, string> { ["buildings.json"] = $"[{TestContent.Bank}]" }, [], []);
        var content = Mods.Apply(Shipped(), [Example(), bank]).Load();
        Assert.Equal("bastion_wall", content.BuildingList[^2].Id);

        var sim = new Simulation(content);
        var tile = Harness.Tile(sim);
        Assert.Equal(PlaceResult.Ok, Placement.Check(sim, tile, Harness.Me, "bastion_wall", 4, 4, 0, null, out _));
        Harness.Place(sim, tile, "bastion_wall", 4, 4);
        sim.Step();
        Assert.Equal("bastion_wall", tile.Grid.BuildingAt(5, 5)?.DefId);
    }

    [Fact]
    public void ModEntriesReplaceByIdInPlace()
    {
        var mod = new ModDef("retune", "Retune", new Dictionary<string, string>
        {
            ["buildings.json"] = """[{ "id": "wall", "name": "Big Wall", "menu": "combat", "kind": "wall", "width": 1, "height": 1, "power": 0, "hp": 999, "cost": { "iron_plate": 3 } }]""",
        }, [], []);
        var shipped = Shipped().Load();
        var content = Mods.Apply(Shipped(), [mod]).Load();
        Assert.Equal(999, content.Buildings["wall"].Hp);
        Assert.Equal(shipped.BuildingList.Select(b => b.Id), content.BuildingList.Select(b => b.Id));
    }

    [Fact]
    public void BrokenModsAreRefused()
    {
        Assert.Throws<ContentException>(() => ModDef.Parse("{}", new Dictionary<string, string>(), [], []));
        Assert.Throws<ContentException>(() => ModDef.Parse("""{ "id": "x" }""", new Dictionary<string, string> { ["units.json"] = "[]" }, [], []));
        var bad = new ModDef("bad", "Bad", new Dictionary<string, string>
        {
            ["buildings.json"] = """[{ "id": "nothing_wall", "name": "Wall", "kind": "wall", "width": 1, "height": 1, "hp": 10, "cost": { "unobtainium": 3 } }]""",
        }, [], []);
        Assert.Throws<ContentException>(() => Mods.Apply(Shipped(), [bad]).Load());
    }
}
