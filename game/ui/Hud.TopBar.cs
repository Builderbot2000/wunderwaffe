using System.Collections.Generic;
using System.Linq;
using Godot;
using Wunderwaffe.Sim;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Screens;

/// <summary>
/// Top bar, full width (UI-001, UI-002): the viewed tile's name and stock (what pays for buildings there), or on the world
/// map the totals over every tile you own; the tile's power balance with each network in its tooltip (FB-005); the
/// clock and the menu buttons. The left and right columns start under it.
/// </summary>
public partial class Hud
{
    HFlowContainer _resources = null!;
    Label _scope = null!, _power = null!;
    VBoxContainer _right = null!;
    Control _topBar = null!;
    string _resourceKey = "";

    void BuildTopBar()
    {
        var col = Ui.Column(2);
        var row = Ui.Row(16);
        _title = Ui.Label("", 20, Ui.Text);
        _title.MouseFilter = MouseFilterEnum.Pass;
        row.AddChild(_title);
        _scope = Ui.Label("All your tiles", Ui.SmallFont, Ui.Muted);
        row.AddChild(_scope);
        _resources = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _resources.AddThemeConstantOverride("h_separation", 18);
        row.AddChild(_resources);
        _power = Ui.Label("", Ui.FontSize);
        _power.MouseFilter = MouseFilterEnum.Pass;
        row.AddChild(_power);
        _clock = Ui.Label("", Ui.FontSize, Ui.Muted);
        row.AddChild(_clock);
        row.AddChild(Ui.Button("Blueprints  B", ToggleLibrary));
        row.AddChild(Ui.Button("Help  F1", () => Toggle(_help)));
        row.AddChild(Ui.Button("Menu  Esc", PauseMenu.Open));
        foreach (var label in new[] { _title, _scope, _power, _clock })
            label.VerticalAlignment = VerticalAlignment.Center;
        col.AddChild(row);

        _stats = Ui.Label("", Ui.SmallFont, Ui.Muted);
        _stats.Visible = false;
        col.AddChild(_stats);
        BuildMatch(col);

        _topBar = Ui.Pin(Ui.Panel(col), LayoutPreset.TopWide);
        AddChild(_topBar);
    }

    /// <summary>Per frame: the columns start under the bar, whose height grows with the F2 line and the online status line.</summary>
    void PlaceColumns() => _left.OffsetTop = _right.OffsetTop = _topBar.Position.Y + _topBar.Size.Y + 10;

    void RefreshTopBar()
    {
        bool tile = _host.View == ViewMode.Tile;
        var viewed = _host.ViewedTile;
        _title.Text = tile ? viewed.Name : "World map";
        _title.TooltipText = tile ? GameText.Plural(viewed.Grid.BuildingCount, "building", "buildings") : "";
        _scope.Visible = !tile;

        var items = tile ? viewed.Inventory.Entries.ToList() : Query.ResourceTotals().ToList();
        string key = tile + string.Join(",", items.Select(kv => $"{kv.Key}{kv.Value}"));
        if (key != _resourceKey)
        {
            _resourceKey = key;
            Ui.Clear(_resources);
            if (items.Count == 0)
                _resources.AddChild(Ui.Label("No stock yet: belt items into the core.", Ui.SmallFont, Ui.Muted));
            foreach (var (id, n) in items)
            {
                var item = Ui.Row(4);
                item.AddChild(Ui.Icon(id, 22));
                item.AddChild(Ui.Label(Short(n)));
                item.TooltipText = $"{GameText.ItemName(Content, id)}: {n:N0}" + (tile ? $" on {viewed.Name}" : " over all your tiles");
                item.MouseFilter = MouseFilterEnum.Pass;
                _resources.AddChild(item);
            }
        }

        var nets = tile ? ShownNetworks(viewed) : [];
        _power.Visible = nets.Count > 0;
        if (nets.Count == 0)
            return;
        int shortNets = nets.Count(n => n.Satisfaction < 1000);
        _power.Text = $"Power  makes {nets.Sum(n => n.Supply)} · uses {nets.Sum(n => n.Demand)}"
            + (shortNets > 0 ? $" · {GameText.Plural(shortNets, "network", "networks")} short" : "");
        _power.AddThemeColorOverride("font_color", shortNets == 0 ? Ui.Text : Ui.Warn);
        _power.TooltipText = PowerTooltip(nets);
    }

    /// <summary>Networks worth a line: those that make, use or store power, busiest first.</summary>
    static List<PowerNetwork> ShownNetworks(TileState tile) => PowerNet.Networks(tile)
        .Where(n => n.Supply > 0 || n.Demand > 0 || n.Capacity > 0)
        .OrderByDescending(n => n.Demand).ThenBy(n => n.Id).ToList();

    /// <summary>Each power network's balance (FB-005): made, used, batteries, and how much of the demand is met.</summary>
    static string PowerTooltip(List<PowerNetwork> nets) => string.Join("\n", nets.Select(n =>
    {
        var text = $"{n.Satisfaction / 10}% · {GameText.Plural(n.Members.Count, "block", "blocks")} · makes {n.Supply} · uses {n.Demand}";
        if (n.Capacity > 0)
            text += $" · stored {n.Stored / Simulation.TicksPerSecond}/{n.Capacity / Simulation.TicksPerSecond}";
        return text;
    }).Prepend("Power networks on this tile (touching blocks, or joined by wire):"));

    static string Short(long n) => n >= 1_000_000 ? $"{n / 1_000_000.0:0.#}M" : n >= 10_000 ? $"{n / 1000.0:0.#}k" : n.ToString("N0");
}
