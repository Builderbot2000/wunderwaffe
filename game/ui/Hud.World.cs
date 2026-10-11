using System.Linq;
using Godot;
using Wunderwaffe.Sim.Api;

namespace Wunderwaffe.Game.Screens;

/// <summary>
/// The world HUD (UI-001): on the world map, your armies, each clickable to select it and jump to it (the resource
/// totals are in the top bar). Read through <see cref="GameQuery"/> (NF-009).
/// </summary>
public partial class Hud
{
    const int ArmyRows = 8;

    VBoxContainer _armies = null!;
    Control _armyPanel = null!;
    string _armyKey = "";

    /// <summary>The local player's view of the match. Cheap to make: it holds only the sim and the seat.</summary>
    GameQuery Query => new(Sim, Me);

    void BuildArmies()
    {
        var col = Ui.Column(4);
        col.AddChild(Ui.Heading("Armies"));
        _armies = Ui.Column(4);
        col.AddChild(_armies);
        _armyPanel = AddLeft(Ui.Panel(col, SideWidth));
    }

    void RefreshArmies()
    {
        var q = Query;
        _armyPanel.Visible = _host.View == ViewMode.World;
        if (!_armyPanel.Visible)
            return;
        var units = q.Units(Me);
        string armyKey = string.Join(",", units.Select(u => $"{u.Id}:{u.TileId}:{u.Hp}:{u.Moving}")) + string.Join(",", _host.Selected);
        if (armyKey == _armyKey)
            return;
        _armyKey = armyKey;
        Ui.Clear(_armies);
        if (units.Count == 0)
            _armies.AddChild(Ui.Label("No units yet. Build one on frame and lift it off (G).", Ui.SmallFont, Ui.Muted, SideWidth - 40));
        foreach (var u in units.Take(ArmyRows))
        {
            string where = Sim.TryGetTile(u.TileId)?.Name ?? "in transit";
            string doing = u.Moving ? $" → {Sim.TryGetTile(u.DestTileId)?.Name ?? "?"}" : "";
            var row = Ui.Button($"Unit {u.Id} · {u.Blocks} blocks · {u.Hp * 100 / System.Math.Max(1, u.MaxHp)}% · {where}{doing}", () => JumpToUnit(u));
            row.Alignment = HorizontalAlignment.Left;
            row.ClipText = true;
            row.AddThemeFontSizeOverride("font_size", Ui.SmallFont);
            if (_host.Selected.Contains(u.Id))
                row.AddThemeColorOverride("font_color", Ui.Accent);
            _armies.AddChild(row);
        }
        if (units.Count > ArmyRows)
            _armies.AddChild(Ui.Label($"and {units.Count - ArmyRows} more", Ui.SmallFont, Ui.Muted));
    }
}
