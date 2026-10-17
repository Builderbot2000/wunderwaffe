using Wunderwaffe.Sim.Ai;
using Wunderwaffe.Sim.Api;
using Wunderwaffe.Sim.Core;
using Xunit.Abstractions;

namespace Wunderwaffe.Sim.Tests;

/// <summary>The game AI (MP-002) on its framework (MP-006), playing through commands only (NF-009).</summary>
public class AiTests(ITestOutputHelper output)
{
    /// <summary>Runs a match with its AI seats for up to <paramref name="seconds"/> or until <paramref name="done"/>.</summary>
    static (double AiMs, double SimMs) Play(Simulation sim, int seconds, Func<bool>? done = null, AiDirector? director = null)
    {
        var ai = director ?? new AiDirector(sim.Setup!);
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
        var director = new AiDirector(sim.Setup!);
        var (aiMs, simMs) = Play(sim, 40 * 60, () => me().GoalsDone == sim.Content.Goals.Count, director);
        output.WriteLine($"ai {aiMs:F0} ms, sim {simMs:F0} ms over {sim.Tick} ticks");
        output.WriteLine($"goals {me().GoalsDone} at {string.Join(", ", me().GoalTicks.Select(t => t / 60))} s");
        output.WriteLine(director.For(0)!.Last?.Describe());
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
    public void ALoadOrTakeoverChangesNothingAboutItsPlay()
    {
        // MP-006: the AI keeps no memory the state can't give again. One match carries on with its AIs; a copy loaded
        // from its save carries on with new ones, initialised from that state. They stay identical.
        GameSetup setup = new(5, 5, 7, Players: 2, AiPlayers: 2);
        var a = Simulation.NewGame(TestContent.Load(), setup);
        var running = new AiDirector(setup);
        Play(a, 4 * 60, director: running);
        var b = Replay.Load(TestContent.Load(), SaveFile.FromJson(SaveFile.From(a).ToJson()));
        int before = a.CommandLog.Count;
        Play(a, 2 * 60, director: running);
        Play(b, 2 * 60);
        Assert.True(a.CommandLog.Count > before, "the AIs did nothing after the save");
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void AnAiTakesOverAPersonsSeatMidMatch()
    {
        // OQ-46: seat 0 is a person who does nothing for a minute and leaves; an AI takes up their side from there.
        var sim = Simulation.NewGame(TestContent.Load(), new GameSetup(5, 5, 3037, Players: 2, AiPlayers: 1));
        var director = new AiDirector(sim.Setup!);
        Assert.Null(director.For(0));
        Play(sim, 60, director: director);
        Assert.DoesNotContain(sim.CommandLog, c => c.Command.Player == 0);

        var ai = director.TakeOver(0, sim);
        Assert.NotNull(ai.Design);
        Assert.Same(ai, director.TakeOver(0, sim));
        Play(sim, 3 * 60, director: director);
        var q = new GameQuery(sim, 0);
        Assert.Contains(sim.CommandLog, c => c.Command.Player == 0 && c.Command is PlaceBuildingCommand);
        Assert.True(q.Buildings(q.StartTile()).Count(b => b.DefId == "drill") >= 2);
    }

    [Fact]
    public void AisKeepActingThroughALongMatch()
    {
        // MP-002: no stall. Every AI keeps issuing commands, grows, builds units and sends them out.
        var sim = Simulation.NewGame(TestContent.Load(), new GameSetup(6, 6, 3037, Players: 2, AiPlayers: 2, VictoryPercent: 100));
        var perMinute = new List<int[]>();
        var director = new AiDirector(sim.Setup!);
        foreach (var ai in director.Players)
            ai.Log = r => { foreach (var p in r.Chosen) output.WriteLine($"{r.Tick} AI {r.Player}: {p.Module}/{p.Key} {p.Why}"); };
        for (int minute = 0; minute < 15 && !sim.IsOver; minute++)
        {
            int before = sim.CommandLog.Count;
            Play(sim, 60, director: director);
            perMinute.Add([.. Enumerable.Range(0, 2).Select(p => sim.CommandLog.Skip(before).Count(c => c.Command.Player == p))]);
        }
        var q = new GameQuery(sim, 0);
        foreach (var p in q.Players())
            output.WriteLine($"player {p.Id}: {p.Tiles} tiles, {p.Units} units, launched {p.Launched}, goals {p.GoalsDone}, eliminated {p.Eliminated}");
        output.WriteLine(string.Join(" ", perMinute.Select(m => $"[{string.Join(",", m)}]")));
        output.WriteLine(string.Join(" | ", sim.CommandLog.TakeLast(4).Select(c => $"{c.Tick}: {c.Command}")));
        foreach (var ai in director.Players)
            output.WriteLine(ai.Last?.Describe());
        foreach (var ai in director.Players)
            Assert.NotEmpty(ai.Last!.Goals);

        foreach (var p in q.Players())
        {
            Assert.True(p.Tiles >= 3, $"player {p.Id} has {p.Tiles} tiles");
            Assert.True(p.Launched >= 1, $"player {p.Id} launched {p.Launched}");
        }
        Assert.Contains(sim.CommandLog, c => c.Command is MoveUnitsCommand);
    }
}
