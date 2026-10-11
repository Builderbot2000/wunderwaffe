using System.Linq;
using Godot;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Screens;

/// <summary>
/// The match as a whole: the end screen with every player's stats once someone wins (MP-001), and the multiplayer
/// status line (waiting for players, someone left, desync detected; MP-003).
/// </summary>
public partial class Hud
{
    Control _end = null!;
    VBoxContainer _endBody = null!;
    Label _netLine = null!;
    bool _endShown;
    double _waiting;

    void BuildMatch(Control top)
    {
        _netLine = Ui.Label("", Ui.SmallFont, Ui.Warn);
        _netLine.Visible = false;
        top.AddChild(_netLine);

        _endBody = Ui.Column(10);
        _end = Modal("Match over", null, _endBody);
    }

    /// <summary>Per frame: online status, and the end screen the moment the match is decided.</summary>
    void UpdateMatch(double delta)
    {
        if (_host.Lockstep is { } lockstep)
        {
            _waiting = lockstep.Waiting ? _waiting + delta : 0;
            string? line = lockstep.Desync is { } turn
                ? $"Out of sync with {PlayerName(lockstep.DesyncSeat)} since {Ui.Clock(turn * global::Wunderwaffe.Sim.Net.Lockstep.TurnTicks)}: " +
                  "your games now differ. Save and report it."
                : _host.Net.Notice
                ?? (_waiting > 1 ? "Waiting for other players…" : null);
            _netLine.Visible = line != null;
            _netLine.Text = line ?? "";
            _netLine.AddThemeColorOverride("font_color", lockstep.Desync != null ? Ui.Bad : Ui.Warn);
        }

        if (Sim.IsOver && !_endShown)
        {
            _endShown = true;
            FillEnd();
            _end.Visible = true;
        }
    }

    void FillEnd()
    {
        Ui.Clear(_endBody);
        var q = Query;
        var match = q.Match();
        string headline = match.Winner == Me ? "Victory" : match.Winner >= 0 ? $"{PlayerName(match.Winner)} wins" : "Draw";
        string why = match.EndReason switch
        {
            VictoryReason.Domination => $"by holding {match.TilesToWin} of {match.TileCount} tiles",
            VictoryReason.LastStanding => "as the last player with a core",
            _ => "everyone was eliminated at once",
        };
        _endBody.AddChild(Ui.Label(headline, 30, match.Winner == Me ? Ui.Good : Ui.Text));
        _endBody.AddChild(Ui.Label($"{why}, after {Ui.Clock(match.EndTick)}.", Ui.FontSize, Ui.Muted));

        var grid = new GridContainer { Columns = 7, MouseFilter = MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 18);
        foreach (string h in new[] { "Player", "Tiles", "Units", "Launched", "Shipped", "Produced", "Goals" })
            grid.AddChild(Ui.Label(h, Ui.SmallFont, Ui.Muted));
        foreach (var p in q.Players())
        {
            string name = PlayerName(p.Id) + (p.Eliminated ? " (out)" : "") + (p.Id == match.Winner ? "  ★" : "");
            grid.AddChild(Ui.Label(name, Ui.FontSize, PlayerColor(p.Id)));
            foreach (long n in new[] { p.Tiles, p.Units, p.Launched, p.Shipped, p.Produced.Values.Sum(), p.GoalsDone })
                grid.AddChild(Ui.Label(n.ToString("N0")));
        }
        _endBody.AddChild(grid);

        var row = Ui.Row();
        row.AddChild(Ui.Button("Keep watching", () => _end.Visible = false));
        if (!_host.IsMultiplayer)
            row.AddChild(Ui.Button("Save game", () => { SaveNow(); _end.Visible = false; }));
        row.AddChild(Ui.Button("Quit to title", _host.QuitToTitle));
        _endBody.AddChild(row);
    }
}
