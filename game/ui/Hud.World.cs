using System.Linq;
using Godot;
using Wunderwaffe.Sim.Api;

namespace Wunderwaffe.Game.Screens;

/// <summary>
/// The world HUD (UI-001): a resource summary over every tile you own (top left, both views) and, on the world map,
/// your armies, each clickable to select it and jump to it. Read through <see cref="GameQuery"/> (NF-009).
/// </summary>
public partial class Hud
{
    const int ArmyRows = 8;

    HFlowContainer _resources = null!;
    VBoxContainer _armies = null!;
    Control _armyPanel = null!;
    string _resourceKey = "", _armyKey = "";

    /// <summary>The local player's view of the match. Cheap to make: it holds only the sim and the seat.</summary>
    GameQuery Query => new(Sim, Me);

    void BuildWorldHud(Control top, Control leftColumn)
    {
        _resources = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(SideWidth - 24, 0) };
        _resources.AddThemeConstantOverride("h_separation", 12);
        top.AddChild(_resources);

        var col = Ui.Column(4);
        col.AddChild(Ui.Heading("Armies"));
        _armies = Ui.Column(4);
        col.AddChild(_armies);
        _armyPanel = Ui.Panel(col);
        leftColumn.AddChild(_armyPanel);
    }

    void RefreshWorldHud()
    {
        var q = Query;
        var totals = q.ResourceTotals();
        string key = string.Join(",", totals.Select(kv => $"{kv.Key}{kv.Value}"));
        if (key != _resourceKey)
        {
            _resourceKey = key;
            Ui.Clear(_resources);
            foreach (var (id, n) in totals)
            {
                var item = Ui.Row(4);
                item.AddChild(Ui.Icon(id, 20));
                item.AddChild(Ui.Label(Short(n), Ui.SmallFont));
                item.TooltipText = $"{GameText.ItemName(Content, id)}: {n:N0} over all your tiles";
                item.MouseFilter = MouseFilterEnum.Pass;
                _resources.AddChild(item);
            }
        }

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

    static string Short(long n) => n >= 1_000_000 ? $"{n / 1_000_000.0:0.#}M" : n >= 10_000 ? $"{n / 1000.0:0.#}k" : n.ToString("N0");
}
