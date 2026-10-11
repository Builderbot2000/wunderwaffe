using Wunderwaffe.Sim.Ai;
using Wunderwaffe.Sim.Api;
using Wunderwaffe.Sim.Core;
using Xunit.Abstractions;

namespace Wunderwaffe.Sim.Tests;

/// <summary>The game AI (MP-002) playing through commands only (NF-009).</summary>
public class AiTests(ITestOutputHelper output)
{
    /// <summary>Runs a match with its AI seats for up to <paramref name="seconds"/> or until <paramref name="done"/>.</summary>
    static (double AiMs, double SimMs) Play(Simulation sim, int seconds, Func<bool>? done = null)
    {
        var ai = new AiDirector(sim.Setup!);
        var aiTime = new System.Diagnostics.Stopwatch();
        var simTime = new System.Diagnostics.Stopwatch();
        for (int i = 0; i < seconds * Simulation.TicksPerSecond && done?.Invoke() != true; i++)
        {
            aiTime.Start();
            ai.BeforeStep(sim);
            aiTime.Stop();
            simTime.Start();
            sim.Step();
            simTime.Stop();
        }
        return (aiTime.Elapsed.TotalMilliseconds, simTime.Elapsed.TotalMilliseconds);
    }

    [Theory]
    [InlineData(3037UL)]
    [InlineData(42UL)]
    public void AnAiPlaysTheWholeGoalChain(ulong seed)
    {
        var sim = Simulation.NewGame(TestContent.Load(), new GameSetup(5, 5, seed, Players: 1, AiPlayers: 1));
        var me = () => sim.PlayerOrNull(0)!;
        var (aiMs, simMs) = Play(sim, 40 * 60, () => me().GoalsDone == sim.Content.Goals.Count);
        output.WriteLine($"ai {aiMs:F0} ms, sim {simMs:F0} ms over {sim.Tick} ticks");

        var q = new GameQuery(sim, 0);
        output.WriteLine($"goals {me().GoalsDone} at {string.Join(", ", me().GoalTicks.Select(t => t / 60))} s; tiles {q.OwnedTiles().Count}; " +
                         $"stock {string.Join(", ", q.ResourceTotals().Select(kv => $"{kv.Key} {kv.Value}"))}");
        foreach (int t in q.OwnedTiles())
            output.WriteLine($"tile {t} n[{string.Join(",", q.Tile(t)!.Neighbors)}] dep[{string.Join(",", q.Tile(t)!.Deposits.Keys)}]: " +
                             string.Join(" ", q.Buildings(t).Where(b => b.DefId != "conveyor").Select(b => $"{b.DefId}@{b.X},{b.Y}>{q.OutletOf(t, b.Id)}/p{b.Partner}/{b.Status}")));
        int home = q.StartTile();
        var mill = q.FindBuilding(home, "steel_mill");
        var core = q.FindBuilding(home, "core")!;
        foreach (int n in q.Tile(home)!.Neighbors.Where(n => q.OwnedTiles().Contains(n)))
        {
            var cells = q.Cells(home, 0, 0, 999, 999).Where(c => c.Zone == n).ToList();
            output.WriteLine($"zone {n}: {cells.Count} cells, {cells.Count(c => c.Buildable)} buildable; plan " +
                $"{q.PlanLine(home, null, -1, [new Stage("import_port"), new Stage("steel_mill")], core.Id, n)?.Count}; spot {q.FindSpot(home, "import_port", zone: n)}");
        }
        output.WriteLine("think: " + string.Join(" | ", new AiPlayer(0).Think(sim)));
        output.WriteLine(string.Join(" | ", sim.CommandLog.TakeLast(12).Select(c => $"{c.Tick}: {c.Command}")));
        if (mill != null)
            output.WriteLine($"mill route {q.RouteBelt(home, mill.Id, core.Id)?.Count}; plan {q.PlanLine(home, null, mill.Id, [], core.Id)?.Count}; " +
                $"feed {q.PlanLine(home, "iron_ore", -1, [new Stage("smelter", "smelt_iron")], mill.Id)?.Count}");
        Assert.Equal(sim.Content.Goals.Count, me().GoalsDone);
    }

    [Fact]
    public void TwoAisPlayDeterministicallyAndReplayFromTheLog()
    {
        GameSetup setup = new(5, 5, 7, Players: 2, AiPlayers: 2);
        var a = Simulation.NewGame(TestContent.Load(), setup);
        var b = Simulation.NewGame(TestContent.Load(), setup);
        Play(a, 6 * 60);
        Play(b, 6 * 60);
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.NotEmpty(a.CommandLog);
        Assert.Contains(a.CommandLog, c => c.Command.Player == 1);

        // The AI's moves are ordinary logged commands: a save replays without running the AI.
        var replayed = Replay.Load(TestContent.Load(), SaveFile.FromJson(SaveFile.From(a).ToJson()));
        Assert.Equal(a.StateHash(), replayed.StateHash());
    }

    [Fact]
    public void AisKeepActingThroughALongMatch()
    {
        // MP-002: no stall. Every AI keeps issuing commands, grows, builds units and sends them out.
        var sim = Simulation.NewGame(TestContent.Load(), new GameSetup(6, 6, 3037, Players: 2, AiPlayers: 2, VictoryPercent: 100));
        var perMinute = new List<int[]>();
        for (int minute = 0; minute < 15 && !sim.IsOver; minute++)
        {
            int before = sim.CommandLog.Count;
            Play(sim, 60);
            perMinute.Add([.. Enumerable.Range(0, 2).Select(p => sim.CommandLog.Skip(before).Count(c => c.Command.Player == p))]);
        }
        var q = new GameQuery(sim, 0);
        foreach (var p in q.Players())
            output.WriteLine($"player {p.Id}: {p.Tiles} tiles, {p.Units} units, launched {p.Launched}, goals {p.GoalsDone}, eliminated {p.Eliminated}");
        output.WriteLine(string.Join(" ", perMinute.Select(m => $"[{string.Join(",", m)}]")));
        output.WriteLine(string.Join(" | ", sim.CommandLog.TakeLast(4).Select(c => $"{c.Tick}: {c.Command}")));

        foreach (var p in q.Players())
        {
            Assert.True(p.Tiles >= 3, $"player {p.Id} has {p.Tiles} tiles");
            Assert.True(p.Launched >= 1, $"player {p.Id} launched {p.Launched}");
        }
        Assert.Contains(sim.CommandLog, c => c.Command is MoveUnitsCommand);
    }
}
