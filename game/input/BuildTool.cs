using System.Collections.Generic;
using System.Linq;
using Godot;
using Wunderwaffe.Game.Render;
using Wunderwaffe.Sim;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Input;

/// <summary>
/// Placement UI: number keys pick a building, R rotates, left-click/drag places, right-click/drag removes.
/// Shows a green/red footprint preview using the sim's own placement check (FB-002).
/// </summary>
public partial class BuildTool : Node2D
{
    sealed record Tool(string BuildingId, string? RecipeId, string Label);

    readonly List<Tool> _tools = [];
    SimHost _host = null!;
    int _selected = -1;
    int _rotation;
    Vector2I _lastActionCell = new(-1, -1);
    PlaceResult _preview;

    public string Status { get; private set; } = "";

    public override void _Ready()
    {
        _host = GetNode<SimHost>("/root/SimHost");
        BuildPalette(_host.Sim);
    }

    /// <summary>One slot per building, or per recipe for crafters that need one chosen.</summary>
    void BuildPalette(Simulation sim)
    {
        foreach (var def in sim.Content.Buildings.Values
                     .Where(b => b.Id != Simulation.CoreBuildingId)
                     .OrderBy(b => b.Id, System.StringComparer.Ordinal))
        {
            var crafting = sim.Content.RecipesByBuilding.GetValueOrDefault(def.Id)?
                .Where(r => r.Inputs.Count > 0).ToList() ?? [];
            if (crafting.Count > 1)
            {
                foreach (var r in crafting)
                    _tools.Add(new Tool(def.Id, r.Id, $"{def.Name} ({r.Id})"));
            }
            else
            {
                _tools.Add(new Tool(def.Id, null, def.Name));
            }
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        for (int i = 0; i < InputActions.Slots && i < _tools.Count; i++)
        {
            if (e.IsActionPressed(InputActions.Slot(i)))
                _selected = _selected == i ? -1 : i;
        }
        if (e.IsActionPressed(InputActions.Cancel))
            _selected = -1;
        if (e.IsActionPressed(InputActions.Rotate))
            _rotation = (_rotation + 1) & 3;
        if (e.IsActionPressed(InputActions.Stress))
            _host.StressFill();
        if (e is InputEventMouseButton { Pressed: true })
            _lastActionCell = new(-1, -1);
    }

    public override void _Process(double delta)
    {
        Vector2I cell = HoveredCell();
        var tile = _host.ViewedTile;

        if (_selected >= 0)
        {
            var (x, y, _, _) = Footprint(cell);
            var tool = _tools[_selected];
            _preview = Placement.Check(_host.Sim.Content, tile, _host.LocalPlayer, tool.BuildingId, x, y, _rotation, tool.RecipeId, out _);
            if (Godot.Input.IsMouseButtonPressed(MouseButton.Left) && cell != _lastActionCell && _preview == PlaceResult.Ok)
            {
                _host.Sim.Enqueue(new PlaceBuildingCommand(_host.LocalPlayer, tile.Id, tool.BuildingId, x, y, _rotation, tool.RecipeId));
                _lastActionCell = cell;
            }
        }
        if (Godot.Input.IsMouseButtonPressed(MouseButton.Right) && cell != _lastActionCell)
        {
            _host.Sim.Enqueue(new RemoveBuildingCommand(_host.LocalPlayer, tile.Id, cell.X, cell.Y));
            _lastActionCell = cell;
        }

        Status = BuildStatus();
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_selected < 0)
            return;
        var (x, y, w, h) = Footprint(HoveredCell());
        float px = Palette.CellPx;
        var tint = _preview == PlaceResult.Ok ? Palette.GhostOk : Palette.GhostBad;
        if (Sprites.Get(_tools[_selected].BuildingId) is { } tex)
        {
            // Same transform as the placed building, drawn as a tinted ghost.
            bool turned = (_rotation & 1) == 1;
            var size = new Vector2(turned ? h : w, turned ? w : h) * px;
            DrawSetTransform(new Vector2(x + w * 0.5f, y + h * 0.5f) * px, _rotation * Mathf.Pi / 2);
            DrawTextureRect(tex, new Rect2(-size / 2, size), false, tint with { A = 0.75f });
            DrawSetTransform(Vector2.Zero);
            DrawRect(new Rect2(x * px, y * px, w * px, h * px), tint with { A = 0.2f });
            return;
        }
        DrawRect(new Rect2(x * px, y * px, w * px, h * px), tint);
    }

    Vector2I HoveredCell()
    {
        var p = GetGlobalMousePosition() / Palette.CellPx;
        return new Vector2I(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Y));
    }

    /// <summary>Rotated footprint centred on the hovered cell.</summary>
    (int X, int Y, int W, int H) Footprint(Vector2I cell)
    {
        var def = _host.Sim.Content.Buildings[_tools[_selected].BuildingId];
        bool turned = (_rotation & 1) == 1;
        int w = turned ? def.Height : def.Width;
        int h = turned ? def.Width : def.Height;
        return (cell.X - (w - 1) / 2, cell.Y - (h - 1) / 2, w, h);
    }

    string BuildStatus()
    {
        var lines = _tools.Select((t, i) => $"{(i == _selected ? ">" : " ")} {i + 1} {t.Label}");
        string state = _selected < 0 ? "no tool (1-9 to pick)" : $"rot {_rotation * 90}°  {_preview}";
        return string.Join("\n", lines) + "\n" + state;
    }
}
