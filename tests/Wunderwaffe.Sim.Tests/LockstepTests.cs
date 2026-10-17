using Wunderwaffe.Sim.Ai;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.Net;

namespace Wunderwaffe.Sim.Tests;

/// <summary>Lockstep multiplayer (MP-003) over an in-memory network with lag and uneven frame rates.</summary>
public class LockstepTests
{
    /// <summary>A host and clients, each with its own sim, linked by a laggy message queue that round-trips JSON.</summary>
    sealed class Net
    {
        readonly List<(int At, int From, int To, string Json)> _wire = [];
        readonly Rng _rng = new(99);
        int _frame;

        public Net(GameSetup setup, int clients)
        {
            var seats = Enumerable.Range(1, clients).ToList();
            Peers.Add(new Lockstep(Simulation.NewGame(TestContent.Load(), setup), 0, true, seats));
            foreach (int s in seats)
                Peers.Add(new Lockstep(Simulation.NewGame(TestContent.Load(), setup), s, false));
            Sims = Peers.Select(p => p.Sim).ToList();
        }

        public List<Lockstep> Peers { get; } = [];
        public List<Simulation> Sims { get; }

        readonly HashSet<int> _cut = [];

        /// <summary>Seat <paramref name="seat"/>'s machine drops off: it stops running, and nothing reaches it or comes from it.</summary>
        public void Cut(int seat) => _cut.Add(seat);

        /// <summary>One frame: every peer runs 0 to 2 ticks, messages arrive 1 to 6 frames after they were sent.</summary>
        public void Frame()
        {
            _frame++;
            foreach (var (at, from, to, json) in _wire.Where(m => m.At <= _frame).ToList())
            {
                _wire.Remove((at, from, to, json));
                if (!_cut.Contains(from) && !_cut.Contains(to))
                    Peers[to].Receive(from, NetMsg.FromJson(json)!);
            }
            foreach (var peer in Peers.Where(p => !_cut.Contains(p.Seat)))
            {
                peer.Advance(_rng.Range(0, 3));
                foreach (var o in peer.TakeOutbox())
                    _wire.Add((_frame + _rng.Range(1, 7), peer.Seat, o.To == Lockstep.Host ? 0 : o.To, o.Msg.ToJson()));
            }
        }
    }

    [Fact]
    public void PeersApplyTheSameCommandsOnTheSameTicks()
    {
        // Seat 2 is an AI the host runs; seats 0 and 1 are people issuing commands on their own peers.
        var setup = new GameSetup(5, 5, 11, Players: 3, AiPlayers: 1);
        var net = new Net(setup, 1);
        var ai = new AiDirector(setup);
        net.Peers[0].HostInputs = (sim, ticks) => ai.Collect(sim, ticks);

        for (int f = 0; f < 3000; f++)
        {
            if (f == 50)
            {
                foreach (var (peer, sim) in net.Peers.Zip(net.Sims))
                    peer.Submit(new SetViewedTileCommand(peer.Seat, sim.StartTileIds[peer.Seat]));
            }
            net.Frame();
        }

        var (host, client) = (net.Sims[0], net.Sims[1]);
        Assert.True(client.Tick > 1000, $"client reached tick {client.Tick}");
        long tick = Math.Min(host.Tick, client.Tick);
        Assert.InRange(host.Tick - client.Tick, -Lockstep.MaxLeadTurns * Lockstep.TurnTicks, Lockstep.MaxLeadTurns * Lockstep.TurnTicks);
        var hostLog = host.CommandLog.Where(c => c.Tick < tick).Select(c => $"{c.Tick} {c.Command}").ToList();
        var clientLog = client.CommandLog.Where(c => c.Tick < tick).Select(c => $"{c.Tick} {c.Command}").ToList();
        Assert.Equal(hostLog, clientLog);
        Assert.Contains(host.CommandLog, c => c.Command.Player == 2);
        Assert.Contains(host.CommandLog, c => c.Command is SetViewedTileCommand { Player: 1 });
        Assert.Null(net.Peers[0].Desync);
        Assert.Null(net.Peers[1].Desync);

        // Both run on to the same tick and agree.
        while (client.Tick < host.Tick)
            net.Frame();
        Assert.Null(net.Peers[0].Desync);
    }

    [Fact]
    public void FiveSeatsStayInSyncAndAnAiTakesOverALeaver()
    {
        // MP-003 with 5 seats: the host, three clients and an AI; client seat 2 leaves mid-match and the host's AI
        // takes over its side (OQ-46) from wherever it stands.
        var setup = new GameSetup(5, 5, 21, Players: 5, AiPlayers: 1);
        var net = new Net(setup, 3);
        var host = net.Peers[0];
        var ai = new AiDirector(setup);
        host.HostInputs = (sim, ticks) => ai.Collect(sim, ticks);
        host.SeatLeft += seat => ai.TakeOver(seat, host.Sim);

        long leftAt = -1;
        for (int f = 0; f < 2400; f++)
        {
            if (f == 50)
            {
                foreach (var (peer, sim) in net.Peers.Zip(net.Sims))
                    peer.Submit(new SetViewedTileCommand(peer.Seat, sim.StartTileIds[peer.Seat]));
            }
            if (f == 600)
            {
                net.Cut(2);
                host.Drop(2);
                leftAt = host.Sim.Tick;
            }
            net.Frame();
        }

        Assert.Equal([2], host.Left);
        Assert.NotNull(ai.For(2));
        var hostSim = net.Sims[0];
        Assert.Contains(hostSim.CommandLog, c => c.Command.Player == 2 && c.Tick > leftAt && c.Command is not SetViewedTileCommand);
        Assert.Contains(hostSim.CommandLog, c => c.Command.Player == 4);
        // The clients still in agree with the host, command for command, the AI's moves for seat 2 included.
        foreach (int seat in new[] { 1, 3 })
        {
            var client = net.Sims[seat];
            long tick = Math.Min(hostSim.Tick, client.Tick);
            Assert.True(client.Tick > 1500, $"seat {seat} reached tick {client.Tick}");
            Assert.Equal(hostSim.CommandLog.Where(c => c.Tick < tick).Select(c => $"{c.Tick} {c.Command}"),
                client.CommandLog.Where(c => c.Tick < tick).Select(c => $"{c.Tick} {c.Command}"));
            Assert.Null(net.Peers[seat].Desync);
        }
        Assert.Null(host.Desync);
    }

    [Fact]
    public void AStateMismatchIsReportedToEveryone()
    {
        var net = new Net(new GameSetup(4, 4, 5, Players: 3), 2);
        for (int f = 0; f < 200; f++)
            net.Frame();
        // Something outside lockstep changes one client's state: the next hash turn catches it.
        net.Sims[2].Enqueue(new DebugGrantCommand(2, net.Sims[2].StartTileIds[2], "iron_ore", 1));
        for (int f = 0; f < 400; f++)
            net.Frame();

        Assert.NotNull(net.Peers[0].Desync);
        Assert.Equal(2, net.Peers[0].DesyncSeat);
        Assert.Equal(net.Peers[0].Desync, net.Peers[1].Desync);
        Assert.Equal(net.Peers[0].Desync, net.Peers[2].Desync);
    }

    [Fact]
    public void ClientsCannotSpeakForOtherSeatsOrCheat()
    {
        var net = new Net(new GameSetup(4, 4, 5, Players: 2), 1);
        net.Peers[1].Submit(new SetViewedTileCommand(0, 1));
        net.Peers[1].Submit(new DebugGrantCommand(1, net.Sims[1].StartTileIds[1], "iron_ore", 1000));
        for (int f = 0; f < 200; f++)
            net.Frame();
        Assert.Empty(net.Sims[0].CommandLog);
    }

    [Fact]
    public void MessagesRoundTripThroughJson()
    {
        NetMsg msg = new TurnMsg(7, [new PlaceBuildingCommand(1, 2, "drill", 3, 4, 1), new MoveUnitsCommand(1, [5, 6], 2, 9, 9)]);
        var back = (TurnMsg)NetMsg.FromJson(msg.ToJson())!;
        Assert.Equal(7, back.Turn);
        Assert.Equal(msg.ToJson(), back.ToJson());
        Assert.Null(NetMsg.FromJson("{nonsense"));
    }
}
