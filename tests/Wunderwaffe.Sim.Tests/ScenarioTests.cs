using Wunderwaffe.Sim.Ai;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

/// <summary>Scenarios (WM-017) and AI head starts (MP-008).</summary>
public class ScenarioTests
{
    static readonly GameSetup Duel = new(3, 3, 3037, Players: 2);

    /// <summary>A generated match, the content it was made with, and that match saved as scenario <paramref name="id"/>.</summary>
    static (ContentDb Content, Simulation Sim, ScenarioDef Scenario) Saved(GameSetup setup, string id = "saved")
    {
        var content = TestContent.WithScenarios();
        var sim = Simulation.NewGame(content, setup);
        var def = content.AddScenario(Scenarios.ToJson(sim.ToScenario(id, "Saved map")));
        return (content, sim, def);
    }

    [Fact]
    public void AGeneratedMapSavedAsAScenarioReloadsToTheSameWorld()
    {
        var (content, a, def) = Saved(Duel);
        var b = Simulation.NewGame(content, GameSetup.ForScenario(def));

        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal(a.StartTileIds, b.StartTileIds);
        Assert.Equal(a.Tiles.Select(t => (t.Name, t.Biome, t.CircleX, t.CircleY, t.OriginX, t.OriginY)),
            b.Tiles.Select(t => (t.Name, t.Biome, t.CircleX, t.CircleY, t.OriginX, t.OriginY)));
        Assert.Equal(a.Tiles.Select(t => t.Neighbors.ToArray()), b.Tiles.Select(t => t.Neighbors.ToArray()));
        for (int y = 0; y < a.Map.Height; y += 7)
        {
            for (int x = 0; x < a.Map.Width; x += 7)
            {
                Assert.Equal(a.Map.TerrainAt(x, y), b.Map.TerrainAt(x, y));
                Assert.Equal(a.Map.ElevationAt(x, y), b.Map.ElevationAt(x, y));
                Assert.Equal(a.Map.TileAtCell(x, y), b.Map.TileAtCell(x, y));
            }
        }
        foreach (var t in a.Tiles)
        {
            var g = b.Tiles[t.Id].Grid;
            for (int i = 0; i < g.Width * g.Height; i++)
                Assert.Equal(t.Grid.ZoneAt(i % g.Width, i / g.Width), g.ZoneAt(i % g.Width, i / g.Width));
        }

        // And they play on the same: the save of the scenario match replays from its setup.
        Run(a, 600);
        Run(b, 600);
        Assert.Equal(a.StateHash(), b.StateHash());
        var replayed = Replay.Load(content, SaveFile.FromJson(SaveFile.From(b).ToJson()));
        Assert.Equal(b.StateHash(), replayed.StateHash());
    }

    [Fact]
    public void EditingTheScenarioChangesTheWorld()
    {
        var (content, sim, def) = Saved(Duel);
        int home = sim.StartTileIds[0];
        var homeTile = def.Tiles[home];
        var deposits = homeTile.Layers!["deposit"];
        string[] legend = [.. deposits.Legend!];
        int iron = Array.IndexOf(legend, "iron_ore");
        Assert.True(iron > 0, "the start has iron ore");
        legend[iron] = "copper_ore";
        var spot = Open(sim.Tiles[home], 1, 1);
        var free = Enumerable.Range(0, 400).Select(i => (X: spot.X + i % 20, Y: spot.Y + i / 20))
            .First(c => sim.Tiles[home].Grid.Buildable(c.X, c.Y) && sim.Tiles[home].Grid.BuildingAt(c.X, c.Y) == null);
        var seat0 = def.Seats[0];
        var other = Enumerable.Range(0, def.Tiles.Count).First(t => sim.Tiles[t].Owner == TileState.Unowned);

        var tiles = def.Tiles.ToList();
        // Changed deposits drop their patches, which are found again from the new deposits.
        tiles[home] = homeTile with
        {
            Name = "Edited", Layers = new(homeTile.Layers) { ["deposit"] = deposits with { Legend = legend } }, Patches = null, PatchMap = null,
        };
        var edited = def with
        {
            Id = "edited",
            Terrain = def.Terrain with { Legend = def.Terrain.Legend!.Select(t => t == "plains" ? "forest" : t).ToArray() },
            Tiles = tiles,
            Seats = [seat0 with
            {
                Tiles = [.. seat0.Tiles, other],
                Buildings = [.. seat0.Buildings!, new ScenarioBuilding(home, "wall", free.X, free.Y)],
            }, def.Seats[1]],
        };
        var b = Simulation.NewGame(content, GameSetup.ForScenario(content.AddScenario(Scenarios.ToJson(edited))));

        var tile = b.Tiles[home];
        Assert.Equal("Edited", tile.Name);
        Assert.Equal(0, tile.Grid.DepositCounts().GetValueOrDefault("iron_ore"));
        Assert.True(tile.Grid.DepositCounts()["copper_ore"] > sim.Tiles[home].Grid.DepositCounts()["copper_ore"]);
        Assert.Equal("wall", tile.Grid.BuildingAt(free.X, free.Y)?.DefId);
        Assert.Equal(0, b.Tiles[other].Owner);
        int forest = content.TerrainList.ToList().FindIndex(t => t.Id == "forest");
        int plains = content.TerrainList.ToList().FindIndex(t => t.Id == "plains");
        bool anyPlains = false;
        for (int i = 0; i < sim.Map.Width * sim.Map.Height; i += 13)
        {
            int x = i % sim.Map.Width, y = i / sim.Map.Width;
            anyPlains |= sim.Map.TerrainAt(x, y) == plains;
            Assert.NotEqual(plains, b.Map.TerrainAt(x, y));
            if (sim.Map.TerrainAt(x, y) == plains)
                Assert.Equal(forest, b.Map.TerrainAt(x, y));
        }
        Assert.True(anyPlains);
    }

    [Fact]
    public void DepositPatchAmountsAreSavedAndEditable()
    {
        // RS-004: a patch's remaining amount is part of the map; the file can change it, or leave patches out.
        var (content, sim, def) = Saved(Duel);
        int home = sim.StartTileIds[0];
        var patches = sim.Tiles[home].Grid.Patches;
        Assert.NotEmpty(patches.All);
        var first = patches.All[0];
        patches.SetAmount(first, first.Start / 3);
        var used = content.AddScenario(Scenarios.ToJson(sim.ToScenario("used", "Used")));
        var b = Simulation.NewGame(content, GameSetup.ForScenario(used));
        Assert.Equal(sim.StateHash(), b.StateHash());
        Assert.Equal(first.Start / 3, b.Tiles[home].Grid.Patches.All[0].Amount);
        Assert.Equal(patches.All.Select(p => (p.Item, p.Cells, p.Start, p.Amount)),
            b.Tiles[home].Grid.Patches.All.Select(p => (p.Item, p.Cells, p.Start, p.Amount)));

        var tiles = used.Tiles.ToList();
        var records = tiles[home].Patches!.ToList();
        records[0] = records[0] with { Amount = 7 };
        tiles[home] = tiles[home] with { Patches = records };
        var edited = Simulation.NewGame(content, GameSetup.ForScenario(content.AddScenario(Scenarios.ToJson(used with { Id = "edited", Tiles = tiles }))));
        Assert.Equal(7, edited.Tiles[home].Grid.Patches.All[0].Amount);

        // No patches in the file: found from the deposits at full amounts, as the generated map had them.
        tiles = [.. def.Tiles.Select(t => t with { Patches = null, PatchMap = null })];
        var found = Simulation.NewGame(content, GameSetup.ForScenario(content.AddScenario(Scenarios.ToJson(def with { Id = "found", Tiles = tiles }))));
        Assert.Equal(Simulation.NewGame(content, Duel).StateHash(), found.StateHash());

        // A patch over cells without its deposit is refused.
        tiles = [.. def.Tiles];
        tiles[home] = tiles[home] with { Patches = [.. tiles[home].Patches!.Select(p => p with { Item = p.Item == "iron_ore" ? "copper_ore" : "iron_ore" })] };
        Assert.Throws<ContentException>(() => content.AddScenario(Scenarios.ToJson(def with { Id = "bad", Tiles = tiles })));
    }

    [Fact]
    public void UnitsAndTheirCargoAreSavedInTheScenario()
    {
        var content = TestContent.WithScenarios();
        var sim = Simulation.NewGame(content, Duel);
        var home = sim.Tiles[sim.StartTileIds[0]];
        var core = home.Grid.Cores.Single();
        var unit = LiftOff(sim, home, core.X, core.Y);
        long ore = unit.Cargo.Get("copper_ore");
        Assert.True(ore > 0);

        var def = content.AddScenario(Scenarios.ToJson(sim.ToScenario("units", "Units")));
        var b = Simulation.NewGame(content, GameSetup.ForScenario(def));
        var copy = Assert.Single(b.Units);
        Assert.Equal((unit.Owner, unit.TileId, unit.X, unit.Y, unit.Mass), (copy.Owner, copy.TileId, copy.X, copy.Y, copy.Mass));
        Assert.Equal(unit.Blocks.Select(x => (x.DefId, x.GX, x.GY)), copy.Blocks.Select(x => (x.DefId, x.GX, x.GY)));
        Assert.Equal(ore, copy.Cargo.Get("copper_ore"));
        Assert.Empty(b.Tiles[b.StartTileIds[0]].Grid.Cores);
        // The seat still owns its start and can land the unit there.
        Assert.Equal(0, b.Tiles[b.StartTileIds[0]].Owner);
    }

    [Fact]
    public void ASavedMatchNeedsTheSameScenarioToReplay()
    {
        var (content, _, def) = Saved(Duel, "map");
        var sim = Simulation.NewGame(content, GameSetup.ForScenario(def));
        Run(sim, 120);
        string save = SaveFile.From(sim).ToJson();
        Assert.Contains("\"Scenario\":\"map\"", save);

        // The same file loads; an edited one with the same id doesn't.
        var again = TestContent.WithScenarios(Scenarios.ToJson(def));
        Assert.Equal(sim.StateHash(), Replay.Load(again, SaveFile.FromJson(save)).StateHash());
        var changed = TestContent.WithScenarios(Scenarios.ToJson(def with { Tiles = [def.Tiles[0] with { Name = "Elsewhere" }, .. def.Tiles.Skip(1)] }));
        var e = Assert.Throws<SaveException>(() => Replay.Load(changed, SaveFile.FromJson(save)));
        Assert.Contains("changed", e.Message);
        Assert.Throws<SaveException>(() => Replay.Load(TestContent.WithScenarios(), SaveFile.FromJson(save)));
    }

    [Fact]
    public void BrokenScenariosAreRefusedOnLoad()
    {
        var (content, sim, def) = Saved(Duel);
        string[] Errors(ScenarioDef bad) => Assert.Throws<ContentException>(() => content.AddScenario(Scenarios.ToJson(bad))).Errors.ToArray();

        Assert.Contains(Errors(def with { Terrain = def.Terrain with { Legend = ["lava", .. def.Terrain.Legend!.Skip(1)] } }),
            e => e.Contains("unknown terrain 'lava'"));
        Assert.Contains(Errors(def with { TileMap = def.TileMap with { Rows = [.. def.TileMap.Rows.Skip(1)] } }), e => e.Contains("rows"));
        Assert.Contains(Errors(def with { TileMap = def.TileMap with { Rows = ["0*3", .. def.TileMap.Rows.Skip(1)] } }), e => e.Contains("cells long"));
        var core = def.Seats[0].Buildings!.First(b => b.DefId == "core");
        Assert.Contains(Errors(def with { Seats = [def.Seats[0] with { Buildings = [.. def.Seats[0].Buildings!, core] }, def.Seats[1]] }),
            e => e.Contains("Blocked"));
        Assert.Contains(Errors(def with { Seats = [new ScenarioSeat([]), def.Seats[1]] }), e => e.Contains("nothing to start with"));
        var tile = def.Tiles[0];
        Assert.Contains(Errors(def with { Tiles = [tile with { Layers = new() { ["deposit"] = new CellGrid(tile.Zones!.Rows, ["", "unobtainium"]) } }, .. def.Tiles.Skip(1)] }),
            e => e.Contains("unobtainium"));
        Assert.Contains(Errors(def with { Seats = [def.Seats[0] with { Tiles = [.. def.Seats[0].Tiles, def.Seats[1].Tiles[0]] }, def.Seats[1]] }),
            e => e.Contains("already belongs"));
        Assert.True(sim.Tiles.Count > 1);
    }

    [Fact]
    public void ASeatWithOnlyATileGetsTheStartPrefabs()
    {
        // A hand-authored map can name start tiles alone: they get the setup's start prefabs, as on a generated map.
        var (content, sim, def) = Saved(Duel);
        var tiles = def.Tiles.Select(t => t with { Layers = new(t.Layers!.Where(l => l.Key != "frame").ToDictionary()) }).ToList();
        var bare = def with { Id = "bare", Tiles = tiles, Seats = def.Seats.Select(s => new ScenarioSeat(s.Tiles)).ToList() };
        var b = Simulation.NewGame(content, GameSetup.ForScenario(content.AddScenario(Scenarios.ToJson(bare))));
        Assert.Equal(sim.StateHash(), b.StateHash());
    }

    [Fact]
    public void AHeadStartPresetStampsItsPrefabsBesideTheAiStart()
    {
        var content = TestContent.WithScenarios();
        var setup = Duel with { AiPlayers = 1, AiHeadStarts = ["stockpile"] };
        var sim = Simulation.NewGame(content, setup);
        var human = sim.Tiles[sim.StartTileIds[0]];
        var ai = sim.Tiles[sim.StartTileIds[1]];
        Assert.Single(human.Grid.Cores);
        var cores = ai.Grid.Cores.Where(c => c.Owner == 1).ToList();
        Assert.Equal(["core", "core_large"], cores.Select(c => c.DefId));
        var start = cores[0];
        Assert.Equal(StartItems(sim)["copper_ore"] + 300, start.Stock!.Get("copper_ore"));
        Assert.Equal(120, cores[1].Stock!.Get("iron_plate"));
        // Beside the start, not across the tile.
        Assert.True(Math.Abs(cores[1].X - start.X) + Math.Abs(cores[1].Y - start.Y) < 12);

        // It's in the setup, so a save replays it.
        Run(sim, 60);
        Assert.Equal(sim.StateHash(), Replay.Load(content, SaveFile.FromJson(SaveFile.From(sim).ToJson())).StateHash());

        Assert.Contains("unknown head start", (setup with { AiHeadStarts = ["nope"] }).Problem(content));
        Assert.NotNull((setup with { AiHeadStarts = ["stockpile", "stockpile"] }).Problem());
        // Only AI seats get one.
        Assert.Null((Duel with { AiHeadStarts = ["stockpile"] }).HeadStartFor(1));
    }

    [Fact]
    public void AHeadStartWorksOnAScenarioToo()
    {
        var (content, _, def) = Saved(Duel);
        var sim = Simulation.NewGame(content, GameSetup.ForScenario(def, aiPlayers: 1) with { AiHeadStarts = ["second_scout"] });
        var ai = sim.Tiles[sim.StartTileIds[1]];
        Assert.Equal(2, ai.Grid.Cores.Count(c => c.Owner == 1));
        Assert.Single(sim.Tiles[sim.StartTileIds[0]].Grid.Cores);
    }

    [Fact]
    public void HeadStartPresetsAreValidated()
    {
        static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", name));
        var e = Assert.Throws<ContentException>(() => ContentDb.FromJson(Read("items.json"), Read("recipes.json"), Read("buildings.json"),
            Read("goals.json"), Read("terrain.json"), Read("layers.json"), TestContent.Prefabs(),
            """[ { "id": "x", "name": "X", "prefabs": ["missing"], "items": { "gold": 5 } } ]"""));
        Assert.Contains(e.Errors, x => x.Contains("unknown prefab 'missing'"));
        Assert.Contains(e.Errors, x => x.Contains("unknown item 'gold'"));
    }

    [Fact]
    public void AnAiSeatRunsItsScenarioAssetsFromTheFirstThink()
    {
        // MP-008: a scenario saved from the middle of an AI match; a fresh AI on that seat carries on with what it has.
        var content = TestContent.WithScenarios();
        var setup = Duel with { AiPlayers = 1 };
        var a = Simulation.NewGame(content, setup);
        var director = new AiDirector(setup);
        for (int i = 0; i < 2 * 60 * Simulation.TicksPerSecond; i++)
        {
            director.BeforeStep(a);
            a.Step();
        }
        int built = a.Tiles.Sum(t => t.Grid.Buildings.Count(b => b.Owner == 1));
        Assert.True(built > 3, "the AI built something to carry on with");

        var def = content.AddScenario(Scenarios.ToJson(a.ToScenario("midgame", "Mid-game")));
        var scenario = GameSetup.ForScenario(def, aiPlayers: 1);
        var b = Simulation.NewGame(content, scenario);
        Assert.Equal(built, b.Tiles.Sum(t => t.Grid.Buildings.Count(x => x.Owner == 1)));
        var ai = new AiDirector(scenario);
        for (int i = 0; i < 30 * Simulation.TicksPerSecond; i++)
        {
            ai.BeforeStep(b);
            b.Step();
        }
        Assert.Contains(b.CommandLog, c => c.Command.Player == 1);
        // It builds on what it was handed rather than stalling (MP-007 plans from whatever it owns).
        Assert.True(b.Tiles.Sum(t => t.Grid.Buildings.Count(x => x.Owner == 1)) + b.Units.Count(u => u.Owner == 1) > built,
            "the AI added nothing to its scenario assets");
    }

    [Fact]
    public void EveryShippedScenarioStartsAMatch()
    {
        var content = TestContent.WithScenarios();
        Assert.NotEmpty(content.Scenarios);
        foreach (var def in content.Scenarios.Values)
        {
            var setup = GameSetup.ForScenario(def, aiPlayers: def.Seats.Count - 1);
            Assert.Null(setup.Problem(content));
            var sim = Simulation.NewGame(content, setup);
            Assert.Equal(def.Seats.Count, sim.StartTileIds.Count);
            Run(sim, 60);
        }
    }
}
