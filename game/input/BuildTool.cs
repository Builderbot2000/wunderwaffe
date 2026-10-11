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

    const int ZoneFontSize = 14;

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
    /// <summary>Quarter turns clockwise from east for the next placement. Not <c>Node2D.Rotation</c>.</summary>
    public int BuildRotation => _rotation;

    /// <summary>The placement check for the picked tool at the hovered cell.</summary>
    public PlaceResult Preview => _preview;

    /// <summary>The neighbour whose I/O zone the hovered cell is in, or -1.</summary>
    public int HoveredZone { get; private set; } = -1;

    /// <summary>True while a building is picked. Otherwise the mouse belongs to <see cref="UnitTool"/>.</summary>
    public bool HasTool => _selected >= 0;

    /// <summary>Picks tool <paramref name="i"/>, or drops it if it's already picked. -1 drops whatever is picked.</summary>
    public void Select(int i) => _selected = i < 0 || i >= _tools.Count || _selected == i ? -1 : i;

    /// <summary>True while the cursor is over a HUD panel, which takes the click instead of the map.</summary>
    bool OverUi() => GetViewport().GuiGetHoveredControl() != null;

    public override void _Ready()
    {
        _host = SimHost.Instance;
        BuildPalette(_host.Sim);
        _host.ShowOnlyIn(this, ViewMode.Tile, () => _lastActionCell = new(-1, -1));
    }

    /// <summary>
    /// One slot per building, in buildings.json order, or per recipe for crafters that need one chosen. The frame keeps
    /// hotkey 0, the tenth slot. Cores and platforms (WM-016, FB-017) come last, since they are rarely built.
    /// </summary>
    void BuildPalette(Simulation sim)
    {
        var content = sim.Content;
        static bool Rare(BuildingDef d) => d.Kind is BuildingKind.Core or BuildingKind.Platform;
        foreach (var def in content.BuildingList.Where(d => !Rare(d)))
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
        // The frame keeps hotkey 0, the tenth slot; buildings past the ninth are picked from the build bar.
        _tools.Insert(System.Math.Min(InputActions.Slots - 1, _tools.Count), new Tool(FrameId, null, "Unit Frame") { IconId = Sprites.Frame });
        foreach (var def in content.BuildingList.Where(Rare))
            _tools.Add(new Tool(def.Id, null, def.Name) { IconId = def.Id });
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
        if (e.IsActionPressed(InputActions.CycleFilter))
            CycleSorterFilter();
        if (e is InputEventMouseButton { Pressed: true })
            _lastActionCell = new(-1, -1);
    }

    /// <summary>Sets the hovered sorter to let the next item through: none, then each item in id order (FB-004).</summary>
    void CycleSorterFilter()
    {
        var tile = _host.ViewedTile;
        var cell = HoveredCell();
        if (tile.Grid.BuildingAt(cell.X, cell.Y)?.Sorter is not { } sorter)
            return;
        var items = _host.Content.Items.Keys.Order(System.StringComparer.Ordinal).Select(k => (string?)k).Prepend(null).ToList();
        string? next = items[(items.IndexOf(sorter.Filter) + 1) % items.Count];
        if (Logistics.CheckFilter(_host.Sim, tile, _host.LocalPlayer, cell.X, cell.Y, next) == FilterResult.Ok)
            _host.Sim.Enqueue(new SetFilterCommand(_host.LocalPlayer, tile.Id, cell.X, cell.Y, next));
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        Vector2I cell = HoveredCell();
        var tile = _host.ViewedTile;
        bool overUi = OverUi();
        HoveredZone = tile.Grid.ZoneAt(cell.X, cell.Y);

        if (_selected >= 0)
        {
            var (x, y, _, _) = Footprint(cell);
            var tool = _tools[_selected];
            _preview = tool.IsFrame
                ? UnitAssembly.CheckFrame(_host.Sim, tile, _host.LocalPlayer, x, y)
                : Placement.Check(_host.Sim, tile, _host.LocalPlayer, tool.BuildingId, x, y, _rotation, tool.RecipeId, out _);
            if (!overUi && Godot.Input.IsMouseButtonPressed(MouseButton.Left) && cell != _lastActionCell && _preview == PlaceResult.Ok)
            {
                _host.Send(tool.IsFrame
                    ? new PlaceFrameCommand(_host.LocalPlayer, tile.Id, x, y)
                    : new PlaceBuildingCommand(_host.LocalPlayer, tile.Id, tool.BuildingId, x, y, _rotation, tool.RecipeId));
                _lastActionCell = cell;
            }
        }
        if (_selected >= 0 && !overUi && Godot.Input.IsMouseButtonPressed(MouseButton.Right) && cell != _lastActionCell)
        {
            _host.Send(_tools[_selected].IsFrame
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
        if (PickedIsPort)
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

    /// <summary>True if the picked tool is an import or export port, which go in an I/O zone.</summary>
    public bool PickedIsPort =>
        Selected is { IsFrame: false } tool
        && _host.Sim.Content.Buildings[tool.BuildingId].Kind is BuildingKind.ExportPort or BuildingKind.ImportPort;

    /// <summary>
    /// Shades every I/O zone, where ports can go, in its neighbour's colour, the hovered one brighter, each labelled
    /// with the tile it links to (IO-001).
    /// </summary>
    void DrawPortZone(float px)
    {
        var tile = _host.ViewedTile;
        foreach (var run in TileShapes.ZoneRuns(tile))
        {
            var color = Palette.Zone(run.Zone) with { A = run.Zone == HoveredZone ? 0.35f : 0.16f };
            DrawRect(new Rect2(run.X * px, run.Y * px, run.Length * px, px), color);
        }
        var font = ThemeDB.FallbackFont;
        foreach (var zone in TileShapes.Zones(tile))
        {
            var to = _host.Sim.Tiles[zone.Neighbor];
            string text = $"→ {to.Name}";
            var size = font.GetStringSize(text, HorizontalAlignment.Left, -1, ZoneFontSize);
            var at = zone.Label * px - size / 2 + new Vector2(0, size.Y * 0.75f);
            DrawRect(new Rect2(at - new Vector2(4, size.Y * 0.8f), size + new Vector2(8, 4)), new Color(0, 0, 0, 0.6f));
            DrawString(font, at, text, HorizontalAlignment.Left, -1, ZoneFontSize, Palette.Zone(zone.Neighbor));
        }
    }

    public Vector2I HoveredCell() => TileView.CellAt(GetGlobalMousePosition());

    /// <summary>Rotated footprint centred on the hovered cell.</summary>
    (int X, int Y, int W, int H) Footprint(Vector2I cell)
    {
        if (_tools[_selected].IsFrame)
            return (cell.X, cell.Y, 1, 1);
        var (w, h) = _host.Sim.Content.Buildings[_tools[_selected].BuildingId].Footprint(_rotation);
        return (cell.X - (w - 1) / 2, cell.Y - (h - 1) / 2, w, h);
    }
}
