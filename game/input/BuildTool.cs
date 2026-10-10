using System.Collections.Generic;
using System.Linq;
using Godot;
using Wunderwaffe.Game.Render;
using Wunderwaffe.Sim;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Input;

/// <summary>
/// Placement UI: number keys (or the HUD's build bar) pick a building, R rotates, left-click/drag places,
/// right-click/drag removes. With no building picked, the mouse commands units instead.
/// Shows a green/red footprint preview using the sim's own placement check (FB-002).
/// </summary>
public partial class BuildTool : Node2D
{
    public const string FrameId = "frame";

    /// <summary>One build-menu entry: a building, a crafter with a chosen recipe, or the unit frame.</summary>
    public sealed record Tool(string BuildingId, string? RecipeId, string Label)
    {
        /// <summary>The unit frame, painted like a building but laid under them.</summary>
        public bool IsFrame => BuildingId == FrameId;

        /// <summary>The id whose sprite stands for this entry: the recipe's output for crafters.</summary>
        public string? IconId { get; init; }
    }

    readonly List<Tool> _tools = [];
    SimHost _host = null!;
    int _selected = -1;
    int _rotation;
    Vector2I _lastActionCell = new(-1, -1);
    PlaceResult _preview;

    /// <summary>The build menu, in hotkey order: 1-9, then 0.</summary>
    public IReadOnlyList<Tool> Tools => _tools;

    public int SelectedIndex => _selected;
    public Tool? Selected => _selected >= 0 ? _tools[_selected] : null;
    public int Rotation => _rotation;

    /// <summary>The placement check for the picked tool at the hovered cell.</summary>
    public PlaceResult Preview => _preview;

    /// <summary>True while a building is picked. Otherwise the mouse belongs to <see cref="UnitTool"/>.</summary>
    public bool HasTool => _selected >= 0;

    /// <summary>Picks tool <paramref name="i"/>, or drops it if it's already picked. -1 drops whatever is picked.</summary>
    public void Select(int i) => _selected = i < 0 || i >= _tools.Count || _selected == i ? -1 : i;

    /// <summary>True while the cursor is over a HUD panel, which takes the click instead of the map.</summary>
    bool OverUi() => GetViewport().GuiGetHoveredControl() != null;

    public override void _Ready()
    {
        _host = GetNode<SimHost>("/root/SimHost");
        BuildPalette(_host.Sim);
        _host.ViewChanged += OnViewChanged;
        OnViewChanged();
    }

    public override void _ExitTree() => _host.ViewChanged -= OnViewChanged;

    void OnViewChanged()
    {
        bool shown = _host.View == ViewMode.Tile;
        Visible = shown;
        ProcessMode = shown ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        _lastActionCell = new(-1, -1);
    }

    /// <summary>One slot per building, in buildings.json order, or per recipe for crafters that need one chosen.</summary>
    void BuildPalette(Simulation sim)
    {
        var content = sim.Content;
        foreach (var def in content.BuildingList.Where(b => b.Id != Simulation.CoreBuildingId))
        {
            var crafting = content.RecipesByBuilding.GetValueOrDefault(def.Id)?
                .Where(r => r.Inputs.Count > 0).ToList() ?? [];
            if (crafting.Count > 1)
            {
                foreach (var r in crafting)
                {
                    string output = r.Outputs.Keys.First();
                    _tools.Add(new Tool(def.Id, r.Id, $"{def.Name}: {content.Items[output].Name}") { IconId = output });
                }
            }
            else
            {
                _tools.Add(new Tool(def.Id, null, def.Name) { IconId = def.Id });
            }
        }
        _tools.Add(new Tool(FrameId, null, "Unit Frame") { IconId = Sprites.Frame });
    }

    public override void _UnhandledInput(InputEvent e)
    {
        for (int i = 0; i < InputActions.Slots && i < _tools.Count; i++)
        {
            if (e.IsActionPressed(InputActions.Slot(i)))
                Select(i);
        }
        if (e.IsActionPressed(InputActions.Cancel) && _selected >= 0)
        {
            _selected = -1;
            GetViewport().SetInputAsHandled();
        }
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
        bool overUi = OverUi();

        if (_selected >= 0)
        {
            var (x, y, _, _) = Footprint(cell);
            var tool = _tools[_selected];
            _preview = tool.IsFrame
                ? UnitAssembly.CheckFrame(tile, _host.LocalPlayer, x, y)
                : Placement.Check(_host.Sim, tile, _host.LocalPlayer, tool.BuildingId, x, y, _rotation, tool.RecipeId, out _);
            if (!overUi && Godot.Input.IsMouseButtonPressed(MouseButton.Left) && cell != _lastActionCell && _preview == PlaceResult.Ok)
            {
                _host.Sim.Enqueue(tool.IsFrame
                    ? new PlaceFrameCommand(_host.LocalPlayer, tile.Id, x, y)
                    : new PlaceBuildingCommand(_host.LocalPlayer, tile.Id, tool.BuildingId, x, y, _rotation, tool.RecipeId));
                _lastActionCell = cell;
            }
        }
        if (_selected >= 0 && !overUi && Godot.Input.IsMouseButtonPressed(MouseButton.Right) && cell != _lastActionCell)
        {
            _host.Sim.Enqueue(_tools[_selected].IsFrame
                ? new RemoveFrameCommand(_host.LocalPlayer, tile.Id, cell.X, cell.Y)
                : new RemoveBuildingCommand(_host.LocalPlayer, tile.Id, cell.X, cell.Y));
            _lastActionCell = cell;
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_selected < 0 || OverUi())
            return;
        float px = Palette.CellPx;
        if (SelectedIsPort())
            DrawPortZone(px);
        var (x, y, w, h) = Footprint(HoveredCell());
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

    bool SelectedIsPort() =>
        !_tools[_selected].IsFrame
        && _host.Sim.Content.Buildings[_tools[_selected].BuildingId].Kind is BuildingKind.ExportPort or BuildingKind.ImportPort;

    /// <summary>Shades the strip along the edge the port would face, where it can be placed (IO-001).</summary>
    void DrawPortZone(float px)
    {
        var grid = _host.ViewedTile.Grid;
        int depth = PortRules.ZoneDepth;
        var zone = PortRules.SideOf(_rotation) switch
        {
            Direction.North => new Rect2(0, 0, grid.Width, depth),
            Direction.South => new Rect2(0, grid.Height - depth, grid.Width, depth),
            Direction.West => new Rect2(0, 0, depth, grid.Height),
            _ => new Rect2(grid.Width - depth, 0, depth, grid.Height),
        };
        DrawRect(new Rect2(zone.Position * px, zone.Size * px), Palette.PortZone);
    }

    public Vector2I HoveredCell()
    {
        var p = GetGlobalMousePosition() / Palette.CellPx;
        return new Vector2I(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Y));
    }

    /// <summary>Rotated footprint centred on the hovered cell.</summary>
    (int X, int Y, int W, int H) Footprint(Vector2I cell)
    {
        if (_tools[_selected].IsFrame)
            return (cell.X, cell.Y, 1, 1);
        var def = _host.Sim.Content.Buildings[_tools[_selected].BuildingId];
        bool turned = (_rotation & 1) == 1;
        int w = turned ? def.Height : def.Width;
        int h = turned ? def.Width : def.Height;
        return (cell.X - (w - 1) / 2, cell.Y - (h - 1) / 2, w, h);
    }

    /// <summary>True if the picked tool is an import or export port, which face a tile edge.</summary>
    public bool SelectedIsPortTool() => _selected >= 0 && SelectedIsPort();
}
