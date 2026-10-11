using System.Linq;
using Godot;
using Wunderwaffe.Game.Input;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Screens;

/// <summary>
/// Production statistics for the viewed tile (FB-011), toggled with O: per item, made and used per minute over the
/// last full minute, and a 10-minute history graph.
/// </summary>
public partial class Hud
{
    const int GraphWidth = 240, GraphHeight = 34;

    Control _statsPanel = null!;
    VBoxContainer _statsBody = null!;

    void BuildStatsPanel()
    {
        var col = Ui.Column(8);
        var head = Ui.Row();
        var heading = Ui.Label("Production · last minute", 22, Ui.Accent);
        heading.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(heading);
        col.AddChild(head);
        _statsBody = Ui.Column(6);
        col.AddChild(_statsBody);
        _statsPanel = Ui.Pin(Ui.Panel(col, 640), LayoutPreset.Center);
        head.AddChild(Ui.Button("Close  O", () => _statsPanel.Visible = false));
        _statsPanel.Visible = false;
        AddChild(_statsPanel);
    }

    /// <summary>Opens or closes the panel; O in the tile view.</summary>
    public void ToggleStats()
    {
        _statsPanel.Visible = !_statsPanel.Visible && _host.View == ViewMode.Tile;
        RefreshStats();
    }

    void RefreshStats()
    {
        if (!_statsPanel.Visible)
            return;
        var stats = _host.ViewedTile.Stats;
        Ui.Clear(_statsBody);
        var items = stats.Items.ToList();
        if (items.Count == 0)
        {
            _statsBody.AddChild(Ui.Label("Nothing made or used on this tile yet.", Ui.SmallFont, Ui.Muted));
            return;
        }

        var grid = new GridContainer { Columns = 5, MouseFilter = MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 18);
        grid.AddThemeConstantOverride("v_separation", 6);
        foreach (var h in new[] { "Item", "Made/min", "Used/min", "Net", "Last 10 min" })
            grid.AddChild(Ui.Label(h, Ui.SmallFont, Ui.Muted));
        foreach (var item in items)
        {
            long made = stats.MadePerMinute(item), used = stats.UsedPerMinute(item);
            var name = Ui.Row(6);
            name.AddChild(Ui.Icon(item, 22));
            name.AddChild(Ui.Label(GameText.ItemName(Content, item), Ui.SmallFont));
            grid.AddChild(name);
            grid.AddChild(Ui.Label(made.ToString("N0"), Ui.SmallFont, Ui.Good));
            grid.AddChild(Ui.Label(used.ToString("N0"), Ui.SmallFont, Ui.Warn));
            grid.AddChild(Ui.Label((made - used).ToString("+#,0;-#,0;0"), Ui.SmallFont, made >= used ? Ui.Text : Ui.Bad));
            var graph = new HistoryGraph { CustomMinimumSize = new Vector2(GraphWidth, GraphHeight), MouseFilter = MouseFilterEnum.Ignore };
            graph.SetData(stats.MadeHistory(item), stats.UsedHistory(item));
            grid.AddChild(graph);
        }
        _statsBody.AddChild(grid);
        _statsBody.AddChild(Ui.Label($"Green: made, amber: used, per {ProductionStats.BucketTicks / Wunderwaffe.Sim.Simulation.TicksPerSecond} s.",
            Ui.SmallFont, Ui.Muted));
    }

    bool HandleStatsInput(InputEvent e)
    {
        if (e.IsActionPressed(InputActions.StatsToggle) && _host.View == ViewMode.Tile)
            ToggleStats();
        else if (e.IsActionPressed(InputActions.Cancel) && _statsPanel.Visible)
            _statsPanel.Visible = false;
        else
            return false;
        return true;
    }
}
