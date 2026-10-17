using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using Wunderwaffe.Sim;
using Wunderwaffe.Sim.Net;

namespace Wunderwaffe.Game.Net;

public enum NetState
{
    Offline,

    /// <summary>Hosting a lobby: others may join, the host edits the match settings.</summary>
    Hosting,

    /// <summary>Connecting to a host.</summary>
    Joining,

    /// <summary>In someone else's lobby, waiting for the host to start.</summary>
    Joined,

    /// <summary>The match is running.</summary>
    Playing,
}

/// <summary>What every machine in the lobby sees: the host's settings and who is in, host first (seat order).</summary>
public sealed record LobbyInfo(GameSetup Setup, List<string> Names)
{
    /// <summary>The setup the match starts with: the people here plus the AI seats.</summary>
    public GameSetup Final => Setup with { Players = Math.Min(GameSetup.MaxPlayers, Names.Count + Setup.AiPlayers) };
}

/// <summary>
/// Online multiplayer over Godot's ENet (MP-003): the host/join lobby, then the transport for <see cref="Lockstep"/>.
/// The host is ENet peer 1 and seat 0; clients take seats 1, 2, ... in the order they joined, and the AI seats come
/// last. A client who leaves mid-match is dropped from lockstep, and the host's AI takes over the seat (OQ-46,
/// <see cref="Lockstep.SeatLeft"/>). Lockstep messages travel as JSON through one reliable RPC (<see cref="Deliver"/>), so the sim decides
/// everything and this node only moves text. A child of <see cref="SimHost"/>, so it outlives scene changes.
/// </summary>
public partial class NetSession : Node
{
    public const int DefaultPort = 30370;
    const int HostPeer = 1;

    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    ENetMultiplayerPeer? _peer;
    string _name = "";

    /// <summary>Host only: ENet peer ids in join order, host first, with their names.</summary>
    readonly List<(long Peer, string Name)> _members = [];

    /// <summary>Host only, once playing: ENet peer of each client seat and back.</summary>
    readonly Dictionary<int, long> _peerOfSeat = [];
    readonly Dictionary<long, int> _seatOfPeer = [];

    public NetState State { get; private set; }

    /// <summary>The lobby as last seen: edited by the host, received by clients.</summary>
    public LobbyInfo? Lobby { get; private set; }

    /// <summary>Why the link failed or someone left, for the lobby page and the HUD; null when there's nothing to say.</summary>
    public string? Notice { get; private set; }

    /// <summary>Raised after <see cref="State"/>, <see cref="Lobby"/> or <see cref="Notice"/> changes.</summary>
    public event Action? Changed;

    /// <summary>True on the machine that opened the lobby, until it leaves.</summary>
    public bool IsHost { get; private set; }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnected;
        Multiplayer.ConnectionFailed += () => Fail("Couldn't reach the host.");
        Multiplayer.ServerDisconnected += () => Fail("Lost the connection to the host.");
    }

    /// <summary>Opens a lobby on <paramref name="port"/>. Returns an error message, or null.</summary>
    public string? Host(string name, int port, GameSetup setup)
    {
        Leave();
        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateServer(port, GameSetup.MaxPlayers - 1);
        if (err != Error.Ok)
            return $"Couldn't open port {port} ({err}).";
        _peer = peer;
        Multiplayer.MultiplayerPeer = peer;
        _name = Clean(name, "Host");
        _members.Add((HostPeer, _name));
        IsHost = true;
        State = NetState.Hosting;
        Lobby = new LobbyInfo(setup with { AiPlayers = Math.Min(setup.AiPlayers, GameSetup.MaxPlayers - 1) }, [_name]);
        Notice = null;
        Changed?.Invoke();
        return null;
    }

    /// <summary>Connects to a host's lobby. Returns an error message, or null (the result arrives through <see cref="Changed"/>).</summary>
    public string? Join(string name, string address, int port)
    {
        Leave();
        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateClient(address.Trim().Length == 0 ? "127.0.0.1" : address.Trim(), port);
        if (err != Error.Ok)
            return $"Couldn't connect to {address}:{port} ({err}).";
        _peer = peer;
        Multiplayer.MultiplayerPeer = peer;
        _name = Clean(name, "Player");
        State = NetState.Joining;
        Notice = null;
        Changed?.Invoke();
        return null;
    }

    /// <summary>Host: changes the match settings everyone sees. The player count follows who has joined.</summary>
    public void SetSetup(GameSetup setup)
    {
        if (State != NetState.Hosting || Lobby == null)
            return;
        Lobby = Lobby with { Setup = setup with { AiPlayers = Math.Clamp(setup.AiPlayers, 0, GameSetup.MaxPlayers - _members.Count) } };
        Broadcast();
    }

    /// <summary>Host: starts the match on every machine.</summary>
    public void Start()
    {
        if (State != NetState.Hosting || Lobby == null)
            return;
        var setup = Lobby.Final;
        _peerOfSeat.Clear();
        _seatOfPeer.Clear();
        for (int seat = 1; seat < _members.Count; seat++)
        {
            _peerOfSeat[seat] = _members[seat].Peer;
            _seatOfPeer[_members[seat].Peer] = seat;
            RpcId(_members[seat].Peer, MethodName.StartMatch, JsonSerializer.Serialize(setup, Json), seat);
        }
        State = NetState.Playing;
        Changed?.Invoke();
        SimHost.Instance.StartMultiplayer(setup, 0, true, _peerOfSeat.Keys);
    }

    /// <summary>Closes the link and forgets the lobby.</summary>
    public void Leave()
    {
        if (_peer != null)
        {
            _peer.Close();
            Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
        }
        _peer = null;
        IsHost = false;
        _members.Clear();
        _peerOfSeat.Clear();
        _seatOfPeer.Clear();
        Lobby = null;
        if (State != NetState.Offline)
        {
            State = NetState.Offline;
            Changed?.Invoke();
        }
    }

    /// <summary>Sends what lockstep has queued. <see cref="SimHost"/> calls it every frame of a match.</summary>
    public void Flush(Lockstep lockstep)
    {
        foreach (var o in lockstep.TakeOutbox())
        {
            long to = o.To == Lockstep.Host ? HostPeer : _peerOfSeat.GetValueOrDefault(o.To, 0);
            if (to != 0 && _peer != null)
                RpcId(to, MethodName.Deliver, o.Msg.ToJson());
        }
    }

    // ---- Lobby ----

    void OnConnected()
    {
        State = NetState.Joined;
        Changed?.Invoke();
        RpcId(HostPeer, MethodName.Hello, _name);
    }

    void OnPeerConnected(long id)
    {
        // Clients learn of each other too; only the host's view matters.
        if (State == NetState.Playing && IsHost)
            _peer?.DisconnectPeer((int)id); // No joining a running match.
    }

    void OnPeerDisconnected(long id)
    {
        if (!IsHost)
            return;
        int index = _members.FindIndex(m => m.Peer == id);
        string who = index >= 0 ? _members[index].Name : "A player";
        if (State == NetState.Hosting && index >= 0)
        {
            _members.RemoveAt(index);
            if (Lobby != null)
                Lobby = Lobby with { Names = _members.Select(m => m.Name).ToList() };
            Broadcast();
        }
        else if (State == NetState.Playing && _seatOfPeer.Remove(id, out int seat))
        {
            _peerOfSeat.Remove(seat);
            SimHost.Instance.Lockstep?.Drop(seat);
            Notice = $"{who} left the match. The AI plays their side now.";
            Changed?.Invoke();
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void Hello(string name)
    {
        long id = Multiplayer.GetRemoteSenderId();
        if (State != NetState.Hosting || Lobby == null)
            return;
        if (_members.Count >= GameSetup.MaxPlayers)
        {
            _peer?.DisconnectPeer((int)id);
            return;
        }
        _members.Add((id, Clean(name, $"Player {_members.Count + 1}")));
        // A full match makes room for people by dropping AI seats.
        int ai = Math.Min(Lobby.Setup.AiPlayers, GameSetup.MaxPlayers - _members.Count);
        Lobby = Lobby with { Setup = Lobby.Setup with { AiPlayers = ai }, Names = _members.Select(m => m.Name).ToList() };
        Broadcast();
    }

    void Broadcast()
    {
        if (Lobby == null)
            return;
        string json = JsonSerializer.Serialize(Lobby, Json);
        foreach (var (peer, _) in _members.Skip(1))
            RpcId(peer, MethodName.LobbyUpdate, json);
        Changed?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void LobbyUpdate(string json)
    {
        Lobby = JsonSerializer.Deserialize<LobbyInfo>(json, Json);
        Changed?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void StartMatch(string setupJson, int seat)
    {
        if (JsonSerializer.Deserialize<GameSetup>(setupJson, Json) is not { } setup || setup.Problem() != null)
        {
            Fail("The host sent a match this game can't start.");
            return;
        }
        State = NetState.Playing;
        Changed?.Invoke();
        SimHost.Instance.StartMultiplayer(setup, seat, false, []);
    }

    // ---- Match ----

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void Deliver(string json)
    {
        if (SimHost.Instance.Lockstep is not { } lockstep || NetMsg.FromJson(json) is not { } msg)
            return;
        long from = Multiplayer.GetRemoteSenderId();
        if (lockstep.IsHost)
        {
            if (_seatOfPeer.TryGetValue(from, out int seat))
                lockstep.Receive(seat, msg);
        }
        else if (from == HostPeer)
        {
            lockstep.Receive(Lockstep.Host, msg);
        }
    }

    void Fail(string why)
    {
        bool playing = State == NetState.Playing;
        if (_peer != null)
        {
            _peer.Close();
            Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
            _peer = null;
        }
        // A running match stays on screen, frozen, with the notice; the lobby goes back to the menu.
        State = playing ? NetState.Playing : NetState.Offline;
        Lobby = playing ? Lobby : null;
        Notice = why;
        Changed?.Invoke();
    }

    static string Clean(string name, string fallback)
    {
        name = name.Trim();
        return name.Length == 0 ? fallback : name.Length > 20 ? name[..20] : name;
    }
}
