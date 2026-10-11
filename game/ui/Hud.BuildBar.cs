using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Wunderwaffe.Game.Input;
using Wunderwaffe.Game.Render;
using Wunderwaffe.Sim;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Screens;

/// <summary>
/// Bottom centre, tile view only: one button per <see cref="BuildTool.Tools"/> entry (tinted when unaffordable), and a line saying what the picked building would do at the cursor.
/// </summary>
public partial class Hud
{
    HBoxContainer BuildToolButtons()
    {
        var bar = Ui.Row(6);
        for (int i = 0; i < BuildTool.Tools.Count; i++)
        {
            var tool = BuildTool.Tools[i];
            int index = i;
            var b = new Button
            {
                Text = i < InputActions.Slots ? ((i + 1) % 10).ToString() : "",
                Icon = tool.IconId == null ? null : Sprites.Get(tool.IconId),
                ExpandIcon = true,
                IconAlignment = HorizontalAlignment.Center,
                VerticalIconAlignment = VerticalAlignment.Top,
                ToggleMode = true,
                FocusMode = FocusModeEnum.None,
                CustomMinimumSize = new Vector2(66, 74),
            };
            b.AddThemeFontSizeOverride("font_size", Ui.SmallFont);
            b.AddThemeConstantOverride("icon_max_width", 40);
            b.Pressed += () => BuildTool.Select(index);
            _toolButtons.Add(b);
            bar.AddChild(b);
        }
        return bar;
    }

    void UpdateBuildBar()
    {
        if (_host.View != ViewMode.Tile)
            return;
        var tile = _host.ViewedTile;
        for (int i = 0; i < _toolButtons.Count; i++)
        {
            var tool = BuildTool.Tools[i];
            var b = _toolButtons[i];
            b.SetPressedNoSignal(i == BuildTool.SelectedIndex);
            bool afford = tile.Inventory.Has(ToolCost(tool));
            b.Modulate = afford ? Colors.White : new Color(1f, 0.7f, 0.7f, 0.75f);
            b.TooltipText = ToolTooltip(tool);
        }

        if (ShowBlueprintStatus())
            return;
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

    IReadOnlyDictionary<string, int> ToolCost(BuildTool.Tool tool) =>
        tool.IsFrame ? Balance.FrameCost : Content.Buildings[tool.BuildingId].Cost;

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

    string PlaceMessage(BuildTool.Tool tool, PlaceResult result)
    {
        var tile = _host.ViewedTile;
        switch (result)
        {
            case PlaceResult.Ok:
                return "click or drag to place";
            case PlaceResult.CannotAfford:
                var missing = ToolCost(tool).Where(kv => tile.Inventory.Get(kv.Key) < kv.Value)
                    .Select(kv => $"{kv.Value - tile.Inventory.Get(kv.Key)} more {GameText.ItemName(Content, kv.Key)}");
                return "needs " + string.Join(", ", missing);
            case PlaceResult.NeedsDeposit:
                return "must sit on an ore deposit";
            case PlaceResult.Blocked:
                return tool.IsFrame ? "already framed" : "something is in the way";
            case PlaceResult.OutOfBounds:
                return "off the edge of the tile";
            case PlaceResult.NotInPortZone:
                return "ports go in an I/O zone along the border with a neighbour";
            case PlaceResult.BadTerrain:
                var cell = BuildTool.HoveredCell();
                return $"can't build on {Content.TerrainList[tile.Grid.TerrainAt(cell.X, cell.Y)].Name.ToLowerInvariant()}";
            case PlaceResult.NotOwner:
                return "not your tile";
            default:
                return result.ToString();
        }
    }
}
