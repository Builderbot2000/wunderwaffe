using Godot;
using Wunderwaffe.Game.Net;
using Wunderwaffe.Sim;
using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Game.Debug;

/// <summary>
/// Debug (`-- --net-smoke host` and `-- --net-smoke join`, headless is fine): a host and a client on this machine
/// play the first <see cref="Ticks"/> ticks of a match with an AI seat over real ENet, the client issuing a command
/// on the way, then each prints its tick, hash and whether lockstep's hash checks found a desync, and quits.
/// </summary>
public partial class NetSmoke : Node
{
    const int Ticks = 1200;
    const double TimeoutSeconds = 90;

    SimHost _host = null!;
    bool _isHost, _sent, _done;
    double _time;

    public static void Attach(SimHost host, string role)
    {
        var smoke = new NetSmoke { _host = host, _isHost = role == "host", ProcessMode = ProcessModeEnum.Always };
        host.AddChild(smoke);
        string? error = smoke._isHost
            ? host.Net.Host("smoke-host", NetSession.DefaultPort, new GameSetup(4, 4, 77, AiPlayers: 1))
            : host.Net.Join("smoke-client", "127.0.0.1", NetSession.DefaultPort);
        if (error != null)
            Finish(smoke, $"FAIL {error}");
    }

    public override void _Process(double delta)
    {
        if (_done)
            return;
        _time += delta;
        if (_time > TimeoutSeconds)
        {
            Finish(this, $"FAIL timed out at tick {(_host.HasGame ? _host.Sim.Tick : -1)}, net {_host.Net.State}");
            return;
        }
        var net = _host.Net;
        if (_isHost && net.State == NetState.Hosting && net.Lobby?.Names.Count == 2)
            net.Start();
        if (!_host.HasGame || _host.Lockstep == null)
            return;
        var sim = _host.Sim;
        if (!_isHost && !_sent && sim.Tick >= 60)
        {
            _sent = true;
            _host.Send(new SetViewedTileCommand(_host.LocalPlayer, sim.StartTileIds[_host.LocalPlayer]));
        }
        // The host runs on a while so the client's last hash reports arrive and are compared.
        if (sim.Tick >= Ticks + (_isHost ? 240 : 0))
        {
            Finish(this, $"tick {sim.Tick} hash {sim.StateHash():X16} log {sim.CommandLog.Count} desync {_host.Lockstep.Desync?.ToString() ?? "none"}");
        }
    }

    static void Finish(NetSmoke smoke, string line)
    {
        smoke._done = true;
        GD.Print($"[net-smoke {(smoke._isHost ? "host" : "join")}] {line}");
        smoke.GetTree().CreateTimer(1.0).Timeout += () => smoke.GetTree().Quit();
    }
}
