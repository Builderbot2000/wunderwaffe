using System.Collections.Generic;
using System.Linq;
using Godot;
using Wunderwaffe.Game.Render;
using Wunderwaffe.Sim.Api;
using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Game.Screens;

/// <summary>
/// Right column, under the objective: cross-tile alerts (UI-003) from the sim's event list, newest first, with a
/// filter per kind of alert (behind the Filter button, since it is rarely changed). Read at the slow refresh (5 Hz), so an alert shows within a second of the event. Clicking
/// one jumps to its tile or unit (UI-001).
/// </summary>
public partial class Hud
{
    const int AlertRows = 6;

    /// <summary>Alert groups the filter toggles, and which event kinds each covers.</summary>
    static readonly (AlertFilter Filter, string Name, GameEventKind[] Kinds)[] AlertGroups =
    [
        (AlertFilter.Combat, "Combat", [GameEventKind.TileUnderAttack, GameEventKind.TileLost, GameEventKind.PlayerEliminated]),
        (AlertFilter.Production, "Production", [GameEventKind.InputsStarved]),
        (AlertFilter.Units, "Units", [GameEventKind.UnitCompleted, GameEventKind.FactoryStopped]),
        (AlertFilter.Territory, "Territory", [GameEventKind.TileClaimed]),
    ];

    VBoxContainer _alertList = null!;
    long _alertsShownSeq = -1;
    long _alertsToastedSeq;

    void BuildAlerts(Control parent)
    {
        var col = Ui.Column(6);
        var head = Ui.Row(6);
        var heading = Ui.Heading("Alerts");
        heading.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(heading);
        var filters = Ui.Row(6);
        filters.Visible = false;
        var toggleFilters = Ui.Button("Filter", () => filters.Visible = !filters.Visible);
        toggleFilters.AddThemeFontSizeOverride("font_size", Ui.SmallFont - 2);
        head.AddChild(toggleFilters);
        foreach (var (filter, name, _) in AlertGroups)
        {
            var toggle = new Button
            {
                Text = name, ToggleMode = true, ButtonPressed = GameSettings.Alerts.HasFlag(filter),
                FocusMode = FocusModeEnum.None, TooltipText = $"Show {name.ToLowerInvariant()} alerts",
            };
            toggle.AddThemeFontSizeOverride("font_size", Ui.SmallFont - 2);
            toggle.Toggled += on =>
            {
                GameSettings.SetAlert(filter, on);
                _alertsShownSeq = -1;
                RefreshAlerts();
            };
            filters.AddChild(toggle);
        }
        col.AddChild(head);
        col.AddChild(filters);
        _alertList = Ui.Column(4);
        col.AddChild(_alertList);
        parent.AddChild(Ui.Panel(col));
        // Old events (from before a load) are listed but don't pop up again.
        _alertsToastedSeq = Sim.LastEventSeq;
    }

    static bool Shown(GameEventKind kind) =>
        AlertGroups.Any(g => g.Kinds.Contains(kind) && GameSettings.Alerts.HasFlag(g.Filter));

    /// <summary>Events for the alert list: the local player's, plus any player's elimination.</summary>
    IEnumerable<GameEvent> AlertEvents(long since) =>
        Query.AllEvents(since).Where(e => Shown(e.Kind) && (e.Player == Me || e.Kind == GameEventKind.PlayerEliminated));

    void RefreshAlerts()
    {
        if (Sim.LastEventSeq == _alertsShownSeq)
            return;
        _alertsShownSeq = Sim.LastEventSeq;

        // Combat alerts also pop up as a toast, so they're seen even with the panel scrolled out of mind.
        foreach (var e in AlertEvents(_alertsToastedSeq).Where(e => e.Kind is GameEventKind.TileUnderAttack or GameEventKind.TileLost))
            Toast(AlertText(e), Ui.Bad, 5);
        _alertsToastedSeq = Sim.LastEventSeq;

        Ui.Clear(_alertList);
        var recent = AlertEvents(0).Reverse().Take(AlertRows).ToList();
        if (recent.Count == 0)
            _alertList.AddChild(Ui.Label("Nothing to report.", Ui.SmallFont, Ui.Muted));
        foreach (var e in recent)
        {
            var row = Ui.Button($"{Ui.Clock(e.Tick)}  {AlertText(e)}", () => JumpTo(e));
            row.Alignment = HorizontalAlignment.Left;
            row.ClipText = true;
            row.CustomMinimumSize = new Vector2(RightWidth - 40, 0);
            row.AddThemeFontSizeOverride("font_size", Ui.SmallFont);
            row.AddThemeColorOverride("font_color", AlertColor(e.Kind));
            _alertList.AddChild(row);
        }
    }

    string AlertText(GameEvent e)
    {
        string tile = Sim.TryGetTile(e.TileId)?.Name ?? "";
        return e.Kind switch
        {
            GameEventKind.TileUnderAttack => $"{tile} is under attack",
            GameEventKind.InputsStarved => $"{tile}: machines short of {GameText.ItemName(Content, e.ItemId ?? "")}",
            GameEventKind.UnitCompleted => $"Unit {e.UnitId} ready{(tile.Length > 0 ? $" on {tile}" : "")}",
            GameEventKind.FactoryStopped => $"{tile}: a factory stopped, its unit can't lift off",
            GameEventKind.TileClaimed => $"You took {tile}",
            GameEventKind.TileLost => $"You lost {tile}",
            GameEventKind.PlayerEliminated => e.Player == Me ? "You were eliminated" : $"{PlayerName(e.Player)} was eliminated",
            _ => e.Kind.ToString(),
        };
    }

    static Color AlertColor(GameEventKind kind) => kind switch
    {
        GameEventKind.TileUnderAttack or GameEventKind.TileLost or GameEventKind.PlayerEliminated => Ui.Bad,
        GameEventKind.InputsStarved or GameEventKind.FactoryStopped => Ui.Warn,
        _ => Ui.Good,
    };

    void JumpTo(GameEvent e)
    {
        if (e.UnitId >= 0 && Query.Unit(e.UnitId) is { } unit)
            JumpToUnit(unit);
        else if (e.TileId >= 0 && Sim.TryGetTile(e.TileId) is { } tile)
            JumpToCell(tile.Id, tile.CenterX, tile.CenterY);
    }

    void JumpToUnit(UnitInfo unit)
    {
        _host.Selected.Clear();
        _host.Selected.Add(unit.Id);
        JumpToCell(unit.TileId, unit.CellX, unit.CellY);
    }

    /// <summary>Opens the tile if it is ours, else the world map with it selected; centres the camera on the world cell.</summary>
    void JumpToCell(int tileId, int worldX, int worldY)
    {
        if (!_host.EnterTile(tileId))
        {
            WorldMap.SelectedTileId = tileId;
            if (_host.View != ViewMode.World)
                _host.ShowWorld();
        }
        _host.FocusCell = new Vector2I(worldX, worldY);
    }

    /// <summary>A player as the HUD names them: the lobby name online, else you / AI / player n.</summary>
    string PlayerName(int id)
    {
        if (_host.IsMultiplayer && _host.Net.Lobby is { } lobby && id < lobby.Names.Count)
            return lobby.Names[id];
        if (id == Me)
            return "You";
        return Sim.Setup?.IsAi(id) == true ? $"AI {id}" : $"Player {id + 1}";
    }

    /// <summary>A player's colour swatch name for labels.</summary>
    static Color PlayerColor(int id) => Palette.Owner(id);
}
