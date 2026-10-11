using System.Diagnostics;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using Xunit.Abstractions;

namespace Wunderwaffe.Sim.Tests;

[Collection(TimingCollection.Name)]
public class SaveTests(ITestOutputHelper output)
{
    const int Me = 0;

    /// <summary>
    /// A small session through every command kind: a mining line, a claim, a unit lifted off and flown, all spread
    /// over a few in-game minutes.
    /// </summary>
    static Simulation Session()
    {
        var sim = Simulation.NewGame(TestContent.Load(), GameSetup.Default);
        var home = sim.Tiles[sim.StartTileId];
        var east = sim.Neighbors(home).First(n => n.Grid.BuildableCount * 2 > n.Grid.Area);
        // Home's shape and terrain depend on the seed, so build on the first open ground: belts on its top row, the
        // unit three rows down.
        var (x0, y0) = Harness.Open(home, 12, 6);
        int ux = x0, uy = y0 + 3;
        void At(int seconds, params Command[] commands)
        {
            while (sim.Tick < seconds * Simulation.TicksPerSecond)
                sim.Step();
            foreach (var c in commands)
                sim.Enqueue(c);
        }

        At(0, new DebugGrantCommand(Me, home.Id, "copper_ore", 500), new DebugGrantCommand(Me, home.Id, "steel", 50),
            new DebugGrantCommand(Me, home.Id, "iron_plate", 50), new DebugGrantCommand(Me, home.Id, "copper_plate", 50));
        At(1, Enumerable.Range(0, 10).Select(i => (Command)new PlaceBuildingCommand(Me, home.Id, "conveyor", x0 + i, y0)).ToArray());
        At(2, new RemoveBuildingCommand(Me, home.Id, x0 + 5, y0), new ClaimTileCommand(Me, east.Id));
        At(3, new PlaceFrameCommand(Me, home.Id, ux, uy), new PlaceFrameCommand(Me, home.Id, ux + 1, uy), new PlaceFrameCommand(Me, home.Id, ux + 2, uy),
            new PlaceFrameCommand(Me, home.Id, ux + 3, uy), new RemoveFrameCommand(Me, home.Id, ux + 3, uy));
        At(4, new PlaceBuildingCommand(Me, home.Id, "thruster", ux, uy), new PlaceBuildingCommand(Me, home.Id, "unit_core", ux + 1, uy),
            new PlaceBuildingCommand(Me, home.Id, "thruster", ux + 2, uy));
        At(5, new ActivateUnitCommand(Me, home.Id, ux + 1, uy));
        At(6, new MoveUnitsCommand(Me, [1], home.Id, home.CenterX - home.OriginX, home.CenterY - home.OriginY));
        At(7, new SetRallyCommand(Me, home.Id, x0, y0, home.Id, home.CenterX - home.OriginX, home.CenterY - home.OriginY, 0),
            new CreateSquadCommand(Me, "Alpha", [1]), new RenameSquadCommand(Me, 1, "Bravo"),
            new SetFormationCommand(Me, 1, Formation.Wedge), new SetFormationCommand(Me, 1, Formation.Custom, [0, 0]),
            new SetSquadStanceCommand(Me, 1, Stance.Defensive), new SetStanceCommand(Me, [1], Stance.Aggressive),
            new AssignSquadCommand(Me, 0, [1]), new DisbandSquadCommand(Me, 1));
        At(8, new PatrolUnitsCommand(Me, [1], home.Id, home.CenterX - home.OriginX, home.CenterY - home.OriginY),
            new HoldUnitsCommand(Me, [1]), new RetreatUnitsCommand(Me, [1]));
        At(20, new DeployUnitCommand(Me, 1));
        At(30);
        return sim;
    }

    [Fact]
    public void ReplayingASaveRebuildsTheSameState()
    {
        // NF-002: load restores identical state.
        var sim = Session();
        Assert.Equal(1, sim.PlayerOrNull(Me)!.Launched);
        Assert.Equal(2, sim.Tiles.Count(t => t.Owner == Me));
        string json = SaveFile.From(sim, "test").ToJson();

        var loaded = Replay.Load(TestContent.Load(), SaveFile.FromJson(json));
        Assert.Equal(sim.Tick, loaded.Tick);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
        Assert.Equal(sim.CommandLog.Count, loaded.CommandLog.Count);

        // And the two stay in step afterwards.
        for (int i = 0; i < 600; i++)
        {
            sim.Step();
            loaded.Step();
        }
        Assert.Equal(sim.StateHash(), loaded.StateHash());
    }

    [Fact]
    public void EveryCommandKindRoundTrips()
    {
        var sim = Session();
        var save = SaveFile.FromJson(SaveFile.From(sim).ToJson());
        Assert.Equal(sim.CommandLog, save.Commands, (a, b) => a.Tick == b.Tick && a.Command.ToString() == b.Command.ToString());
        var kinds = save.Commands.Select(c => c.Command.GetType()).Distinct().Count();
        Assert.Equal(typeof(Command).Assembly.GetTypes().Count(t => t.IsSubclassOf(typeof(Command)) && !t.IsAbstract), kinds);
    }

    [Fact]
    public void ReplayCanRunInSlices()
    {
        var sim = Session();
        var replay = new Replay(TestContent.Load(), SaveFile.FromJson(SaveFile.From(sim).ToJson()));
        int slices = 0;
        while (!replay.Done)
        {
            replay.Advance(97);
            slices++;
        }
        Assert.True(slices > 1);
        Assert.True(replay.Matches);
    }

    [Fact]
    public void AReplayThatDivergesIsReported()
    {
        var sim = Session();
        var save = SaveFile.From(sim) with { Hash = sim.StateHash() + 1 };
        Assert.Throws<SaveException>(() => Replay.Load(TestContent.Load(), save));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{ "Version": 99, "Setup": { "Width": 5, "Height": 5, "Seed": 1 }, "Tick": 0, "Hash": 0, "Label": "", "Commands": [] }""")]
    public void BadFilesAreRejected(string json) =>
        Assert.Throws<SaveException>(() => SaveFile.FromJson(json));

    [Fact]
    public void OnlyNewGamesCanBeSaved()
    {
        var sim = new Simulation(TestContent.Load());
        Assert.Throws<SaveException>(() => SaveFile.From(sim));
    }

    [Fact]
    public void BusyHourReplaysQuickly()
    {
        // NF-002 wants loads well under a few seconds. A save replays every tick, so time a 10-minute session with
        // a working factory and extrapolate.
        var sim = Simulation.NewGame(TestContent.Load(), GameSetup.Default);
        var home = sim.Tiles[sim.StartTileId];
        sim.Enqueue(new DebugGrantCommand(Me, home.Id, "copper_ore", 100_000));
        sim.Enqueue(new DebugGrantCommand(Me, home.Id, "iron_ore", 100_000));
        int placed = 0;
        for (int y = 0; y < 90 && placed < 300; y += 3)
        {
            for (int x = 0; x < 90 && placed < 300; x += 3)
            {
                sim.Enqueue(new PlaceBuildingCommand(Me, home.Id, "drill", x, y));
                placed++;
            }
        }
        for (int i = 0; i < 10 * 60 * Simulation.TicksPerSecond; i++)
            sim.Step();

        var save = SaveFile.FromJson(SaveFile.From(sim).ToJson());
        var sw = Stopwatch.StartNew();
        Replay.Load(TestContent.Load(), save);
        double perHour = sw.Elapsed.TotalSeconds * 6;
        output.WriteLine($"replayed 10 min in {sw.Elapsed.TotalSeconds:F2}s, ~{perHour:F1}s per hour of play");
        Assert.True(perHour < 60, $"{perHour:F1}s per hour");
    }
}
