using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Wunderwaffe.Game.Input;
using Wunderwaffe.Game.Render;
using Wunderwaffe.Sim;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Screens;

/// <summary>
/// Bottom centre, tile view only: a row of tabs (<see cref="BuildingDef.Menus"/>), the open tab's buttons (tinted when
/// unaffordable, numbered by hotkey), and a line saying what the picked building would do at the cursor.
/// </summary>
public partial class Hud
{
    const int ToolButtonWidth = 92;

    readonly List<Button> _menuButtons = [];
    readonly List<Control> _menuRows = [];

    Container BuildToolButtons()
    {
        var col = Ui.Column(6);
        col.MouseFilter = MouseFilterEnum.Ignore;
        var tabs = Ui.Row(6);
        col.AddChild(tabs);
        for (int m = 0; m < BuildingDef.Menus.Length; m++)
        {
            int menu = m;
            var tab = Ui.Button($"{InputActions.MenuName(m)}  ⇧{m + 1}", () => BuildTool.OpenMenu(menu));
            tab.ToggleMode = true;
            tab.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            tab.AddThemeFontSizeOverride("font_size", Ui.SmallFont);
            _menuButtons.Add(tab);
            tabs.AddChild(tab);

            var row = Ui.Row(6);
            row.CustomMinimumSize = new Vector2(BuildBarWidth, 0);
            var slots = BuildTool.ToolsIn(m);
            for (int i = 0; i < slots.Count; i++)
                row.AddChild(ToolButton(slots[i], i < InputActions.Slots ? ((i + 1) % 10).ToString() : ""));
            _menuRows.Add(row);
            col.AddChild(row);
        }
        return col;
    }

    Button ToolButton(int index, string key)
    {
        var tool = BuildTool.Tools[index];
        var icon = tool.IconId == null ? null : Sprites.Get(tool.IconId);
        var b = new Button
        {
            Text = $"{key}  {tool.Short}".Trim(),
            Icon = icon,
            AutowrapMode = TextServer.AutowrapMode.Word,
            ClipText = true,
            ExpandIcon = true,
            IconAlignment = HorizontalAlignment.Center,
            VerticalIconAlignment = VerticalAlignment.Top,
            ToggleMode = true,
            FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(ToolButtonWidth, 84),
        };
        b.AddThemeFontSizeOverride("font_size", Ui.SmallFont);
        b.AddThemeConstantOverride("icon_max_width", 36);
        b.Pressed += () => BuildTool.Select(index);
        _toolButtons[index] = b;
        return b;
    }

    void UpdateBuildBar()
    {
        if (_host.View != ViewMode.Tile)
            return;
        var tile = _host.ViewedTile;
        for (int m = 0; m < _menuButtons.Count; m++)
        {
            _menuButtons[m].SetPressedNoSignal(m == BuildTool.Menu);
            _menuRows[m].Visible = m == BuildTool.Menu;
        }
        foreach (int i in BuildTool.ToolsIn(BuildTool.Menu))
        {
            var tool = BuildTool.Tools[i];
            var b = _toolButtons[i];
            b.SetPressedNoSignal(i == BuildTool.SelectedIndex);
            bool afford = Funds.CanPay(tile, Me, ToolCost(tool));
            b.Modulate = afford ? Colors.White : new Color(1f, 0.55f, 0.55f, 0.6f);
            b.TooltipText = ToolTooltip(tool);
        }

        if (ShowBlueprintStatus())
            return;
        if (BuildTool.Moving)
        {
            ShowMoveStatus();
            return;
        }
        if (BuildTool.Selected is not { } picked)
        {
            _placePanel.Visible = false;
            return;
        }
        _placePanel.Visible = true;
        string facing = $"facing {"→↓←↑"[BuildTool.BuildRotation]}";
        if (BuildTool.PickedIsPort)
            facing = BuildTool.HoveredZone >= 0 ? $"links to {Sim.Tiles[BuildTool.HoveredZone].Name}" : "outside any I/O zone";
        var result = BuildTool.Preview;
        _placeLine.Text = $"{picked.Label} · {facing} · {PlaceMessage(picked, result)}";
        _placeLine.AddThemeColorOverride("font_color", result == PlaceResult.Ok ? Ui.Text : Ui.Bad);
    }

    /// <summary>Cut and paste (FB-023): what is cut, which way it faces, and why a paste here would be refused.</summary>
    void ShowMoveStatus()
    {
        _placePanel.Visible = true;
        var result = BuildTool.Preview;
        string text;
        if (BuildTool.Cut is { } cut)
        {
            string message = result switch
            {
                PlaceResult.Ok => "click to paste it here (on a unit's frame it rides along at lift-off)",
                PlaceResult.OtherTile => "a building stays on its tile: graft it onto one of your landed units and fly it out",
                _ => PlaceMessage(null, result),
            };
            text = $"Move {Content.Buildings[cut.DefId].Name} · facing {"→↓←↑"[BuildTool.BuildRotation]} · {message}";
        }
        else
        {
            text = "Move · " + result switch
            {
                PlaceResult.Ok => "click to cut this building",
                PlaceResult.NotOwner => "not your building",
                _ => "click one of your buildings to cut it",
            };
        }
        _placeLine.Text = text;
        _placeLine.AddThemeColorOverride("font_color", result == PlaceResult.Ok ? Ui.Text : Ui.Bad);
    }

    IReadOnlyDictionary<string, int> ToolCost(BuildTool.Tool tool) =>
        Content.Buildings[tool.BuildingId].Cost;

    string ToolTooltip(BuildTool.Tool tool)
    {
        var sb = new StringBuilder(tool.Label).Append('\n');
        sb.Append("Cost: ").Append(GameText.Cost(Content, ToolCost(tool)));
        if (tool.RecipeId is { } rid)
            sb.Append('\n').Append(GameText.Recipe(Content, Content.Recipes[rid]));
        else if (!tool.IsFrame && Content.RecipesByBuilding.GetValueOrDefault(tool.BuildingId) is { Count: > 0 } rs && rs.All(r => r.Inputs.Count == 0))
            sb.Append("\nMines whatever deposit it sits on");
        return sb.ToString();
    }

    string PlaceMessage(BuildTool.Tool? tool, PlaceResult result)
    {
        var tile = _host.ViewedTile;
        switch (result)
        {
            case PlaceResult.Ok:
                return "click or drag to place";
            case PlaceResult.CannotAfford:
                var missing = ToolCost(tool!).Where(kv => Funds.Available(tile, Me, kv.Key) < kv.Value)
                    .Select(kv => $"{kv.Value - Funds.Available(tile, Me, kv.Key)} more {GameText.ItemName(Content, kv.Key)}");
                return "needs " + string.Join(", ", missing) + " in your cores here";
            case PlaceResult.NeedsDeposit:
                return "must sit on an ore deposit";
            case PlaceResult.Blocked:
                return tool is { IsFrame: true } ? "already framed" : "something is in the way";
            case PlaceResult.OutOfBounds:
                return "off the edge of the tile";
            case PlaceResult.NotInPortZone:
                return "ports go in an I/O zone along the border with a neighbour";
            case PlaceResult.BadTerrain:
                var cell = BuildTool.HoveredCell();
                return $"can't build on {Content.TerrainList[tile.Grid.TerrainAt(cell.X, cell.Y)].Name.ToLowerInvariant()}";
            case PlaceResult.Wreck:
                return "a wreck is in the way: right-click it with a building picked to salvage it";
            case PlaceResult.NoPresence:
                return "land a unit here first: you need a building or unit on a tile to build on it";
            case PlaceResult.OutOfRange:
                return "out of range: build within the radius of your cores, factories, turrets or units here";
            case PlaceResult.EnemyCircle:
                return "an enemy building holds this victory circle";
            case PlaceResult.NotOwner:
                return "not your building";
            default:
                return result.ToString();
        }
    }
}
