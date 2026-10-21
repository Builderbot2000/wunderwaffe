using Wunderwaffe.Sim.Api;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

/// <summary>MP-009 faction templates and MP-010 faction rules.</summary>
public class FactionTests
{
    /// <summary>A capture-to-build faction whose walls are its own variant, for variant/base separation.</summary>
    const string Raiders =
        """[ { "id": "charter", "name": "Charter" }, { "id": "raiders", "name": "Raiders", "variants": { "wall": "bulwark" }, "rules": [ { "type": "capture_to_build", "kind": "wall" } ] } ]""";

    [Fact]
    public void ShippedFactionsLoadWithTheDefaultFirst()
    {
        var content = TestContent.WithFactions();
        var f = content.Factions;
        Assert.Equal("charter", content.DefaultFaction.Id);
        Assert.True(f["charter"].Has("wall"));
        Assert.False(f["charter"].Has("bulwark")); // another faction's variant
        Assert.True(f["foundry"].Has("bulwark"));
        Assert.False(f["foundry"].Has("wall"));
        Assert.False(f["nomads"].Has("laser_turret"));
    }

    [Fact]
    public void ASubsetWithoutItsPrerequisitesIsRefused()
    {
        // No smelter: nothing makes plates, so every entry costing them is missing its prerequisites.
        var e = Assert.Throws<ContentException>(() => TestContent.WithFactions("""[ { "id": "a", "name": "A", "except": ["smelter"] } ]"""));
        Assert.Contains(e.Errors, x => x.Contains("'steel_mill' costs 'iron_plate'"));
        e = Assert.Throws<ContentException>(() => TestContent.WithFactions("""[ { "id": "a", "name": "A", "buildings": ["drill", "conveyor"] } ]"""));
        Assert.Contains(e.Errors, x => x.Contains("no core"));
        e = Assert.Throws<ContentException>(() => TestContent.WithFactions("""[ { "id": "a", "name": "A", "variants": { "wall": "gun_turret" } } ]"""));
        Assert.Contains(e.Errors, x => x.Contains("kind, footprint and layer"));
        e = Assert.Throws<ContentException>(() => TestContent.WithFactions("""[ { "id": "a", "name": "A", "rules": [ { "type": "buff", "stat": "hp", "percent": 10, "kind": "wall" } ] } ]"""));
        Assert.Contains(e.Errors, x => x.Contains("buff needs a stat"));
    }

    [Fact]
    public void PlacementRefusesWhatTheFactionLacks()
    {
        var sim = FactionSim("nomads");
        var tile = Tile(sim);
        Assert.Equal(PlaceResult.NotInFaction, Placement.Check(sim, tile, Me, "laser_turret", 2, 2, 0, null, out _));
        Assert.Equal(PlaceResult.Ok, Placement.Check(sim, tile, Me, "gun_turret", 2, 2, 0, null, out _));
        var q = new GameQuery(sim, Me);
        Assert.DoesNotContain(q.BuildingTypes(), b => b.Id == "laser_turret");
        Assert.Equal(FactionResult.NotInFaction, q.CheckFaction("laser_turret"));
    }

    [Fact]
    public void VariantsStandInForTheirBaseInSharedBlueprints()
    {
        var sim = FactionSim("foundry");
        var mine = Tile(sim);
        var theirs = Tile(sim, x: 40, owner: Them);
        Assert.Equal(PlaceResult.NotInFaction, Placement.Check(sim, mine, Me, "wall", 2, 2, 0, null, out _));
        var bp = new Blueprint([new BlueprintBlock("wall", 0, 0, 0)], []);
        Assert.Equal(BlueprintResult.Ok, Blueprints.Check(sim, mine, Me, bp, 2, 2, 0, false, out var plan));
        Assert.Equal("bulwark", plan.Blocks[0].Def.Id);
        // The default faction gets the base back from a blueprint recorded with the variant.
        var back = new Blueprint([new BlueprintBlock("bulwark", 0, 0, 0)], []);
        Assert.Equal(BlueprintResult.Ok, Blueprints.Check(sim, theirs, Them, back, 2, 2, 0, false, out plan));
        Assert.Equal("wall", plan.Blocks[0].Def.Id);
        Assert.Equal(FactionResult.Ok, new GameQuery(sim, Me).CheckBlueprintFaction(bp));
    }

    [Fact]
    public void CaptureToBuildUnlocksThatExactBuildingForGood()
    {
        var sim = FactionSim("nomads");
        var tile = Tile(sim);
        Assert.Equal(PlaceResult.NotCaptured, Placement.Check(sim, tile, Me, "unit_factory", 2, 2, 0, null, out _));
        ulong before = sim.StateHash();
        Capture(sim, "unit_factory");
        Assert.NotEqual(before, sim.StateHash());
        Assert.Equal(PlaceResult.Ok, Placement.Check(sim, tile, Me, "unit_factory", 2, 2, 0, null, out _));
        Assert.Equal(PlaceResult.NotCaptured, Placement.Check(sim, tile, Me, "core_large", 6, 6, 0, null, out _));
        Assert.Contains("core_large", new GameQuery(sim, Me).Faction().Locked);
        // Building one and losing it changes nothing: the unlock stays.
        sim.Enqueue(new PlaceBuildingCommand(Me, tile.Id, "unit_factory", 2, 2, 0));
        Run(sim, 1);
        sim.Enqueue(new RemoveBuildingCommand(Me, tile.Id, 3, 3));
        Run(sim, 1);
        Assert.Null(tile.Grid.BuildingAt(3, 3));
        Assert.Equal(FactionResult.Ok, Factions.Check(sim, Me, "unit_factory"));
    }

    [Fact]
    public void MovingABuildingTheFactionCantBuildIsAllowed()
    {
        var sim = FactionSim("nomads");
        var tile = Tile(sim);
        // Got some other way than building it (a scenario, a claim): FB-023 may still move it.
        tile.Grid.Add(sim.Content.Buildings["laser_turret"], Me, 2, 2, 1, 1, 0, null);
        Assert.Equal(PlaceResult.Ok, Relocation.Check(sim, tile, Me, 2, 2, tile, 5, 5, 0, out _));
    }

    [Fact]
    public void AVariantAndItsBaseAreCapturedSeparately()
    {
        var sim = FactionSim("raiders", Raiders);
        Tile(sim);
        Capture(sim, "wall");
        Assert.Equal(FactionResult.NotCaptured, Factions.Check(sim, Me, "bulwark"));
        Capture(sim, "bulwark");
        Assert.Equal(FactionResult.Ok, Factions.Check(sim, Me, "bulwark"));
    }

    [Fact]
    public void ABuffRaisesItsStatForTheOwnersBuildingsOnly()
    {
        var sim = FactionSim("foundry");
        var mine = Tile(sim);
        var theirs = Tile(sim, x: 40, owner: Them);
        Ore(mine, "iron_ore", 2, 2);
        Ore(theirs, "iron_ore", 2, 2);
        Place(sim, mine, "drill", 2, 2);
        sim.Enqueue(new PlaceBuildingCommand(Them, theirs.Id, "drill", 2, 2, 0));
        Run(sim, 2);
        var a = mine.Grid.BuildingAt(2, 2)!;
        var b = theirs.Grid.BuildingAt(2, 2)!;
        Assert.Equal(20, Boosters.StatsOf(sim, mine, a).Speed);
        Assert.Equal(20, a.Machine!.Boost.Speed);
        Assert.Equal(0, b.Machine!.Boost.Speed);
    }

    [Fact]
    public void RandomIsResolvedFromTheSeedAndSeat()
    {
        var content = TestContent.WithFactions();
        var picks = new HashSet<string>();
        for (ulong seed = 0; seed < 40; seed++)
        {
            var setup = GameSetup.Default with { Seed = seed, Players = 2, Factions = [GameSetup.RandomFaction, GameSetup.RandomFaction] };
            var again = setup with { };
            Assert.Equal(setup.FactionFor(1, content).Id, again.FactionFor(1, content).Id);
            picks.Add(setup.FactionFor(0, content).Id);
        }
        Assert.True(picks.Count > 1);
        Assert.Equal("unknown faction 'nope'", (GameSetup.Default with { Factions = ["nope"] }).Problem(content));
    }

    [Fact]
    public void FactionsAndCaptureRecordsSurviveSavesAndSnapshots()
    {
        var content = TestContent.WithFactions();
        var setup = GameSetup.Default with { Width = 3, Height = 3, Players = 2, Factions = ["foundry", "nomads"] };
        var sim = Simulation.NewGame(content, setup);
        Assert.Equal("foundry", sim.FactionOf(0).Id);
        Assert.Equal("nomads", sim.FactionOf(1).Id);
        Run(sim, 5);
        var loaded = Replay.Load(content, SaveFile.FromJson(SaveFile.From(sim).ToJson()));
        Assert.Equal("nomads", loaded.FactionOf(1).Id);
        Assert.Equal(sim.StateHash(), loaded.StateHash());

        Capture(sim, "core_large", 1);
        var restored = Snapshot.Capture(sim).Restore(content, sim.Setup!, sim.CommandLog);
        Assert.Contains("core_large", restored.Players[1].Acquired);
        Assert.Equal(sim.StateHash(), restored.StateHash());
    }
}
