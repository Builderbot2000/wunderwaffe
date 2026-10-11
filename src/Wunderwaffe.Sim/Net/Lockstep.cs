using System.Text.Json;
using System.Text.Json.Serialization;
using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim.Net;

/// <summary>
/// A lockstep message (MP-003). The transport (ENet in the game, a list in tests) only carries their JSON: every peer
/// runs the same <see cref="Simulation"/> and exchanges inputs, never state.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "msg")]
[JsonDerivedType(typeof(InputMsg), "input")]
[JsonDerivedType(typeof(TurnMsg), "turn")]
[JsonDerivedType(typeof(AckMsg), "ack")]
[JsonDerivedType(typeof(DesyncMsg), "desync")]
public abstract record NetMsg
{
    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>The message, or null if <paramref name="json"/> isn't one.</summary>
    public static NetMsg? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<NetMsg>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Client to host: one command the client's player issued.</summary>
public sealed record InputMsg(Command Command) : NetMsg;

/// <summary>Host to clients: every command to apply at the start of <paramref name="Turn"/>, in order.</summary>
public sealed record TurnMsg(long Turn, List<Command> Commands) : NetMsg;

/// <summary>
/// Client to host: the client has run <paramref name="Turn"/>. <paramref name="Hash"/> is its state hash after it, on
/// turns that are a multiple of <see cref="Lockstep.HashEveryTurns"/>; 0 otherwise.
/// </summary>
public sealed record AckMsg(long Turn, ulong Hash) : NetMsg;

/// <summary>Host to clients: seat <paramref name="Seat"/>'s state differed from the host's after <paramref name="Turn"/>.</summary>
public sealed record DesyncMsg(long Turn, int Seat) : NetMsg;

/// <summary>A message to send: to the host (<see cref="Lockstep.Host"/>), or to one client seat.</summary>
public readonly record struct Outgoing(int To, NetMsg Msg);

/// <summary>
/// Turn-based lockstep over a host (MP-003). Time is cut into turns of <see cref="TurnTicks"/> ticks. Clients send
/// their commands to the host as they happen; when the host starts turn T it seals every command it has received
/// into the bundle for turn T + <see cref="DelayTurns"/> and broadcasts it. Every peer, the host included, applies a
/// turn's bundle at its first tick and may only run a turn once it holds its bundle, so all of them apply the same
/// commands on the same ticks. The bundle order is the host's arrival order: it is the authority, so it needs no
/// further sorting.
/// <para>
/// Clients acknowledge each turn they run, with a state hash every <see cref="HashEveryTurns"/> turns; the host compares
/// it with its own and reports a mismatch to everyone (<see cref="Desync"/>). The host also never runs more than
/// <see cref="MaxLeadTurns"/> ahead of the slowest connected client, so a slow peer slows the match instead of
/// falling ever further behind. There is no pause (OQ-32).
/// </para>
/// This class knows nothing about sockets: the transport feeds <see cref="Receive"/> and drains <see cref="TakeOutbox"/>.
/// </summary>
public sealed class Lockstep
{
    /// <summary>The seat id that addresses the host in <see cref="Outgoing.To"/>.</summary>
    public const int Host = -1;

    public const int TurnTicks = 6;
    public const int DelayTurns = 3;
    public const int HashEveryTurns = 10;
    public const int MaxLeadTurns = 2 * DelayTurns;

    readonly Simulation _sim;
    readonly Dictionary<long, List<Command>> _bundles = [];
    readonly List<Command> _incoming = [];
    readonly List<Outgoing> _outbox = [];

    /// <summary>Host only: last turn each client seat has run, -1 before the first.</summary>
    readonly SortedDictionary<int, long> _acked = [];

    /// <summary>Host only: own hashes after hash turns, until every client has reported them.</summary>
    readonly Dictionary<long, ulong> _hashes = [];

    /// <summary>Host only: client hashes for turns the host hasn't finished yet.</summary>
    readonly Dictionary<long, List<(int Seat, ulong Hash)>> _early = [];

    /// <param name="seat">This peer's player id.</param>
    /// <param name="clientSeats">Host only: the seats of the connected clients.</param>
    public Lockstep(Simulation sim, int seat, bool isHost, IEnumerable<int>? clientSeats = null)
    {
        _sim = sim;
        Seat = seat;
        IsHost = isHost;
        foreach (int s in clientSeats ?? [])
            _acked[s] = -1;
        // The first turns run before any input can arrive: their bundles are empty on every peer.
        for (long t = 0; t < DelayTurns; t++)
            _bundles[t] = [];
    }

    public Simulation Sim => _sim;
    public int Seat { get; }
    public bool IsHost { get; }

    /// <summary>
    /// Host only: extra commands to seal into each bundle, asked once per turn with the ticks the turn spans. The game
    /// AI goes here, so its moves travel to clients like anyone's.
    /// </summary>
    public Func<Simulation, int, IEnumerable<Command>>? HostInputs { get; set; }

    /// <summary>The first turn whose state differed between peers, or null while in sync.</summary>
    public long? Desync { get; private set; }

    /// <summary>The seat whose state differed at <see cref="Desync"/> (as the host saw it).</summary>
    public int DesyncSeat { get; private set; } = -1;

    /// <summary>True when the next tick can't run yet: waiting for the host's bundle, or (host) for a slow client.</summary>
    public bool Waiting => !CanRun();

    long Turn => _sim.Tick / TurnTicks;

    /// <summary>A command the local player issued. Applied on every peer a few turns from now.</summary>
    public void Submit(Command command)
    {
        if (IsHost)
            _incoming.Add(command);
        else
            _outbox.Add(new Outgoing(Host, new InputMsg(command)));
    }

    /// <summary>A message from the transport: from client seat <paramref name="from"/> on the host, from the host on a client.</summary>
    public void Receive(int from, NetMsg msg)
    {
        switch (msg)
        {
            case InputMsg input when IsHost:
                // A client can only speak for its own seat.
                if (input.Command.Player == from && input.Command is not DebugGrantCommand)
                    _incoming.Add(input.Command);
                break;
            case TurnMsg turn when !IsHost:
                _bundles[turn.Turn] = turn.Commands;
                break;
            case AckMsg ack when IsHost && _acked.ContainsKey(from):
                _acked[from] = Math.Max(_acked[from], ack.Turn);
                if (ack.Hash != 0)
                {
                    if (_hashes.TryGetValue(ack.Turn, out ulong mine))
                        Compare(ack.Turn, from, mine, ack.Hash);
                    else if (ack.Turn >= Turn)
                        (_early.TryGetValue(ack.Turn, out var list) ? list : _early[ack.Turn] = []).Add((from, ack.Hash));
                }
                ForgetOldHashes();
                break;
            case DesyncMsg d when !IsHost:
                if (Desync == null)
                    (Desync, DesyncSeat) = (d.Turn, d.Seat);
                break;
        }
    }

    /// <summary>Host: a client left. It no longer holds the match back.</summary>
    public void Drop(int seat) => _acked.Remove(seat);

    /// <summary>Messages to send since the last call.</summary>
    public IReadOnlyList<Outgoing> TakeOutbox()
    {
        var list = _outbox.ToList();
        _outbox.Clear();
        return list;
    }

    /// <summary>Runs up to <paramref name="maxTicks"/> ticks, stopping early when <see cref="Waiting"/>. Returns ticks run.</summary>
    public int Advance(int maxTicks)
    {
        int run = 0;
        while (run < maxTicks && CanRun())
        {
            StepOnce();
            run++;
        }
        return run;
    }

    bool CanRun()
    {
        long turn = Turn;
        if (!_bundles.ContainsKey(turn))
            return false;
        if (IsHost && _sim.Tick % TurnTicks == 0)
        {
            foreach (long acked in _acked.Values)
            {
                if (acked < turn - MaxLeadTurns)
                    return false;
            }
        }
        return true;
    }

    void StepOnce()
    {
        long turn = Turn;
        bool first = _sim.Tick % TurnTicks == 0;
        if (first)
        {
            if (IsHost)
                Seal(turn + DelayTurns);
            foreach (var command in _bundles[turn])
                _sim.Enqueue(command);
        }
        _sim.Step();
        if (_sim.Tick % TurnTicks != 0)
            return;

        // The turn is over.
        _bundles.Remove(turn);
        ulong hash = turn % HashEveryTurns == 0 ? _sim.StateHash() : 0;
        if (IsHost)
        {
            if (hash != 0 && _acked.Count > 0)
                _hashes[turn] = hash;
            if (_early.Remove(turn, out var reports))
            {
                foreach (var (seat, theirs) in reports)
                    Compare(turn, seat, hash, theirs);
            }
        }
        else
        {
            _outbox.Add(new Outgoing(Host, new AckMsg(turn, hash)));
        }
    }

    void Seal(long turn)
    {
        var commands = _incoming.ToList();
        _incoming.Clear();
        if (HostInputs?.Invoke(_sim, TurnTicks) is { } extra)
            commands.AddRange(extra);
        _bundles[turn] = commands;
        foreach (int seat in _acked.Keys)
            _outbox.Add(new Outgoing(seat, new TurnMsg(turn, commands)));
    }

    void Compare(long turn, int seat, ulong mine, ulong theirs)
    {
        if (mine == theirs || Desync != null)
            return;
        (Desync, DesyncSeat) = (turn, seat);
        foreach (int s in _acked.Keys)
            _outbox.Add(new Outgoing(s, new DesyncMsg(turn, seat)));
    }

    void ForgetOldHashes()
    {
        long oldest = _acked.Count == 0 ? long.MaxValue : _acked.Values.Min();
        foreach (long t in _hashes.Keys.Where(t => t <= oldest).ToList())
            _hashes.Remove(t);
    }
}
