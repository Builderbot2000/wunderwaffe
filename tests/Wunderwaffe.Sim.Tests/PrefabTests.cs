using Wunderwaffe.Sim.Api;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

/// <summary>Prefab blueprints (FB-021): content, validation, match setup, the query API, and blueprint codes (UD-009).</summary>
public class PrefabTests
{
    /// <summary>Two walls side by side: a building prefab for head-start tests.</summary>
    const string Walls =
        """{ "id": "walls", "name": "Walls", "blocks": [ { "defId": "wall", "x": 0, "y": 0 }, { "defId": "wall", "x": 1, "y": 0 } ], "frames": [] }""";

    static ContentException Refused(string prefab) =>
        Assert.Throws<ContentException>(() => TestContent.WithPrefabs(prefab));

    [Fact]
    public void TheShippedScoutIsTheStartingUnitWithTheStartingItems()
    {
        var content = TestContent.Load();
        var scout = content.Prefabs[GameSetup.DefaultStart];
        Assert.Equal(["thruster", "core", "thruster"], scout.Blocks.Select(b => b.DefId));
        Assert.Equal(8, scout.Frames.Length);
        Assert.True(scout.Blueprint.IsUnit(content.Buildings));
        Assert.Equal(600, scout.Items!["copper_ore"]);
        Assert.Equal(300, scout.Items["iron_ore"]);
    }

    [Fact]
    public void BrokenPrefabsRefuseToLoad()
    {
        static string Prefab(string blocks, string frames, string extra = "") =>
            $$"""{ "id": "bad", "name": "Bad", "blocks": [{{blocks}}], "frames": [{{frames}}]{{extra}} }""";
        const string core = """{ "defId": "core", "x": 0, "y": 0 }""";
        Assert.Contains(Refused(Prefab("""{ "defId": "unobtainium", "x": 0, "y": 0 }""", "")).Errors, e => e.Contains("unknown building 'unobtainium'"));
        Assert.Contains(Refused(Prefab("""{ "defId": "wall", "x": 0, "y": 0, "rotation": 5 }""", "")).Errors, e => e.Contains("rotation 5"));
        Assert.Contains(Refused(Prefab(core + "," + """{ "defId": "wall", "x": 1, "y": 1 }""", "")).Errors, e => e.Contains("overlaps"));
        Assert.Contains(Refused(Prefab("", """{ "x": 0, "y": 0 }, { "x": 0, "y": 0 }""")).Errors, e => e.Contains("repeated"));
        Assert.Contains(Refused(Prefab("""{ "defId": "wall", "x": 2, "y": 3 }""", "")).Errors, e => e.Contains("(0, 0)"));
        Assert.Contains(Refused(Prefab(core, """{ "x": 0, "y": 0 }""")).Errors, e => e.Contains("must stand on its frame"));
        Assert.Contains(Refused(Prefab("""{ "defId": "wall", "x": 0, "y": 0 }""", "", """, "items": { "iron_ore": 5 }""")).Errors,
            e => e.Contains("need a core"));
        Assert.Contains(Refused(Prefab(core, "", """, "items": { "unobtainium": 5 }""")).Errors, e => e.Contains("unknown item"));
        Assert.Contains(Refused(Prefab("", "")).Errors, e => e.Contains("no blocks"));
        Assert.Contains(Refused("""{ "id": "scout", "name": "Again", "blocks": [ { "defId": "wall", "x": 0, "y": 0 } ], "frames": [] }""").Errors,
            e => e.Contains("duplicate prefab id 'scout'"));
    }

    [Fact]
    public void TheSetupNamesEachSeatsPrefabsAndExtrasGiveAHeadStart()
    {
        // OQ-47: an AI's head start is extra prefabs for its seat.
        var setup = GameSetup.Default with { Players = 2, AiPlayers = 1, StartPrefabs = [[], [GameSetup.DefaultStart, "walls"]] };
        var sim = Simulation.NewGame(TestContent.WithPrefabs(Walls), setup);
        var human = sim.Tiles[sim.StartTileIds[0]];
        var ai = sim.Tiles[sim.StartTileIds[1]];
        Assert.DoesNotContain(human.Grid.Buildings, b => b.DefId == "wall");
        var walls = ai.Grid.Buildings.Where(b => b.DefId == "wall").ToList();
        Assert.Equal(2, walls.Count);
        Assert.All(walls, w => Assert.Equal(1, w.Owner));
        Assert.Equal(walls[0].Y, walls[1].Y);
        Assert.Equal(1, Math.Abs(walls[0].X - walls[1].X));
        // Both still start with the scout, its core in the circle holding the prefab's items.
        foreach (var (home, p) in new[] { (human, 0), (ai, 1) })
        {
            var core = Assert.Single(home.Grid.Cores);
            Assert.Equal(p, core.Owner);
            Assert.True(home.InCircle(core.X, core.Y, core.Width, core.Height));
            Assert.Equal(StartItems(sim)["copper_ore"], core.Stock!.Get("copper_ore"));
        }
    }

    [Fact]
    public void ASetupNamingAnUnknownPrefabIsRefused()
    {
        var setup = GameSetup.Default with { StartPrefabs = [["nothing_like_it"]] };
        Assert.Contains("nothing_like_it", setup.Problem(TestContent.Load()));
        Assert.Throws<ArgumentOutOfRangeException>(() => Simulation.NewGame(TestContent.Load(), setup));
        Assert.NotNull((GameSetup.Default with { StartPrefabs = [[], []] }).Problem());
    }

    [Fact]
    public void TheStartingScoutLiftsOffAsBuilt()
    {
        var sim = Simulation.NewGame(TestContent.Load(), GameSetup.Default with { Width = GameSetup.MinSize, Height = GameSetup.MinSize });
        var home = sim.Tiles[sim.StartTileId];
        var core = Assert.Single(home.Grid.Cores);
        Assert.Equal(AssembleResult.Ok, UnitAssembly.CheckActivate(sim, home, Me, core.X, core.Y, out _));
    }

    [Fact]
    public void TheQueryListsPrefabsForAnyDriver()
    {
        // The AI picks designs through the same queries as everyone else.
        var sim = Simulation.NewGame(TestContent.WithPrefabs(Walls), GameSetup.Default with { Width = GameSetup.MinSize, Height = GameSetup.MinSize });
        var q = new GameQuery(sim, Me);
        Assert.Equal(["scout", "walls"], q.Prefabs().Select(p => p.Id));
        var scout = q.Prefab("scout")!;
        Assert.True(scout.IsUnit);
        Assert.Equal(300, scout.Items["iron_ore"]);
        Assert.False(q.Prefab("walls")!.IsUnit);
        Assert.Equal(BlueprintResult.Blocked, q.CheckBlueprint(sim.StartTileId, scout.Blueprint, -5, -5));
    }

    [Fact]
    public void ABlueprintCodeReimportsToTheSameDesign()
    {
        // UD-009.
        var content = TestContent.Load();
        var bp = new Blueprint(
            [new BlueprintBlock("thruster", 0, 0, 2), new BlueprintBlock("smelter", 1, 0, 1, "smelt_copper"), new BlueprintBlock("wall", 3, 1, 0)],
            [new BlueprintCell(0, 1), new BlueprintCell(3, 0)]);
        string code = Prefabs.ToCode("My design", bp, ["unit", "fast"]);
        Assert.StartsWith(Prefabs.CodePrefix, code);
        Assert.DoesNotContain(code, c => char.IsWhiteSpace(c));

        var back = Prefabs.FromCode(content, code, out var error);
        Assert.Null(error);
        Assert.Equal("My design", back!.Name);
        Assert.Equal(["unit", "fast"], back.Tags!);
        Assert.Equal(bp.Blocks, back.Blocks);
        Assert.Equal(bp.Frames, back.Frames);
        Assert.Equal(code, Prefabs.ToCode(back.Name, back.Blueprint, back.Tags));

        Assert.Null(Prefabs.FromCode(content, "hello", out error));
        Assert.NotNull(error);
        Assert.Null(Prefabs.FromCode(content, code[..^6] + "!!!!!!", out error));
        Assert.NotNull(error);
        var alien = Prefabs.ToCode("Alien", new Blueprint([new BlueprintBlock("unobtainium", 0, 0, 0)], []));
        Assert.Null(Prefabs.FromCode(content, alien, out error));
        Assert.Contains("unobtainium", error);
    }
}
