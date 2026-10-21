using System.Collections.Generic;
using System.Linq;
using Godot;
using Wunderwaffe.Game.Render;
using Wunderwaffe.Sim.Api;
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
    const int ZoneFontSize = 14;

    /// <summary>
    /// One build-menu entry: a building (the unit frame and other layer buildings included) or a crafter with a chosen
    /// recipe. <paramref name="Short"/> is the button text; <paramref name="Menu"/> is its tab, an index into
    /// <see cref="BuildingDef.Menus"/>.
    /// </summary>
    public sealed record Tool(string BuildingId, string? RecipeId, string Label, string Short, int Menu)
    {
        /// <summary>A unit frame (UD-010), painted like a building but laid under them; right-click takes up frame, even under a building.</summary>
        public bool IsFrame { get; init; }

        /// <summary>The id whose sprite stands for this entry: the recipe's output for crafters.</summary>
        public string? IconId { get; init; }
    }

    readonly List<Tool> _tools = [];
    readonly List<int>[] _menus = [.. BuildingDef.Menus.Select(_ => new List<int>())];
    SimHost _host = null!;
    int _selected = -1;
    int _menu;
    int _rotation;
    Vector2I _lastActionCell = new(-1, -1);
    PlaceResult _preview;

    /// <summary>Every build-menu entry, tab by tab.</summary>
    public IReadOnlyList<Tool> Tools => _tools;

    /// <summary>The open tab, an index into <see cref="BuildingDef.Menus"/>.</summary>
    public int Menu => _menu;

    /// <summary>Indices into <see cref="Tools"/> on tab <paramref name="menu"/>, in hotkey order: 1-9, then 0.</summary>
    public IReadOnlyList<int> ToolsIn(int menu) => _menus[menu];

    /// <summary>Opens tab <paramref name="menu"/>; the picked building, if any, stays picked.</summary>
    public void OpenMenu(int menu) => _menu = menu;

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

    /// <summary>
    /// Picks tool <paramref name="i"/> and opens its tab, or drops it if it's already picked. -1 drops whatever is picked.
    /// </summary>
    public void Select(int i)
    {
        _selected = i < 0 || i >= _tools.Count || _selected == i ? -1 : i;
        if (_selected >= 0)
            _menu = _tools[_selected].Menu;
    }

    /// <summary>True while the cursor is over a HUD panel, which takes the click instead of the map.</summary>
    bool OverUi() => GetViewport().GuiGetHoveredControl() != null;

    public override void _Ready()
    {
        _host = SimHost.Instance;
        BuildPalette(_host.Sim);
        _host.ShowOnlyIn(this, ViewMode.Tile, () => _lastActionCell = new(-1, -1));
    }

    /// <summary>
    /// One entry per building, in buildings.json order within its tab, or per recipe for crafters that need one chosen.
    /// The frame leads the units tab. Platforms (FB-017) come last in theirs, since they are rarely built.
    /// </summary>
    void BuildPalette(Simulation sim)
    {
        var content = sim.Content;
        static int Order(BuildingDef d) => d.Kind switch { BuildingKind.Frame => 0, BuildingKind.Platform => 2, _ => 1 };
        var tools = new List<Tool>();
        foreach (var def in content.BuildingList.OrderBy(Order))
        {
            int menu = System.Array.IndexOf(BuildingDef.Menus, def.Menu);
            var crafting = content.RecipesByBuilding.GetValueOrDefault(def.Id)?
                .Where(r => r.Inputs.Count > 0).ToList() ?? [];
            if (crafting.Count <= 1)
            {
                tools.Add(new Tool(def.Id, null, def.Name, def.Name, menu) { IconId = def.Id, IsFrame = def.Kind == BuildingKind.Frame });
                continue;
            }
            foreach (var r in crafting)
            {
                string output = r.Outputs.Keys.First();
                // Two recipes for one item (fuel from ore or coal) are told apart by what they use.
                string name = content.Items[output].Name;
                if (crafting.Count(o => o.Outputs.Keys.First() == output) > 1)
                    name += " from " + string.Join(" + ", r.Inputs.Keys.Select(id => content.Items[id].Name));
                tools.Add(new Tool(def.Id, r.Id, $"{def.Name}: {name}", name, menu) { IconId = output });
            }
        }
        // Tab by tab, so the tools' indices run in the bar's order.
        foreach (var t in tools.OrderBy(t => t.Menu))
        {
            _menus[t.Menu].Add(_tools.Count);
            _tools.Add(t);
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        // Exact matches, so Ctrl/Alt+number (control groups) and Shift+number (tabs) don't also pick a building.
        var slots = _menus[_menu];
        for (int i = 0; i < InputActions.Slots && i < slots.Count; i++)
        {
            if (e.IsActionPressed(InputActions.Slot(i), false, true))
                Select(slots[i]);
        }
        for (int i = 0; i < _menus.Length; i++)
        {
            if (e.IsActionPressed(InputActions.MenuSlot(i), false, true))
                OpenMenu(i);
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
            _host.Send(new SetFilterCommand(_host.LocalPlayer, tile.Id, cell.X, cell.Y, next));
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
            _preview = Placement.Check(_host.Sim, tile, _host.LocalPlayer, tool.BuildingId, x, y, _rotation, tool.RecipeId, out _);
            if (!overUi && Godot.Input.IsMouseButtonPressed(MouseButton.Left) && cell != _lastActionCell && _preview == PlaceResult.Ok)
            {
                _host.Send(new PlaceBuildingCommand(_host.LocalPlayer, tile.Id, tool.BuildingId, x, y, _rotation, tool.RecipeId));
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
        DrawBuildRadii(this, _host);
        if (PickedIsPort)
            DrawPortZone(px);
        var (x, y, w, h) = Footprint(HoveredCell());
        var tint = _preview == PlaceResult.Ok ? Palette.GhostOk : Palette.GhostBad;
        if (Selected is { IsFrame: false } picked && _host.Sim.Content.Buildings[picked.BuildingId] is { Kind: BuildingKind.PowerNode } node)
            DrawNodeRadius(px, x, y, w, h, node.PowerRadius);
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

    /// <summary>
    /// The local player's build radii on the viewed tile (WM-015, <see cref="GameQuery.BuildRadii"/>), outlined in their
    /// colour: each footprint grown by its radius with round corners, the exact edge of where they may build. Drawn while
    /// the build or blueprint tool is active.
    /// </summary>
    public static void DrawBuildRadii(CanvasItem canvas, SimHost host)
    {
        float px = Palette.CellPx;
        var line = Palette.Owner(host.LocalPlayer) with { A = 0.7f };
        foreach (var b in new GameQuery(host.Sim, host.LocalPlayer).BuildRadii(host.ViewedTile.Id))
        {
            // The covered cells' outer edge: r + 1/2 cell out from the footprint's edge cells' centres.
            float r = (b.Radius + 0.5f) * px;
            float x0 = (b.X + 0.5f) * px, y0 = (b.Y + 0.5f) * px, x1 = (b.X + b.Width - 0.5f) * px, y1 = (b.Y + b.Height - 0.5f) * px;
            canvas.DrawLine(new Vector2(x0, y0 - r), new Vector2(x1, y0 - r), line, 2);
            canvas.DrawLine(new Vector2(x0, y1 + r), new Vector2(x1, y1 + r), line, 2);
            canvas.DrawLine(new Vector2(x0 - r, y0), new Vector2(x0 - r, y1), line, 2);
            canvas.DrawLine(new Vector2(x1 + r, y0), new Vector2(x1 + r, y1), line, 2);
            canvas.DrawArc(new Vector2(x0, y0), r, Mathf.Pi, Mathf.Pi * 1.5f, 24, line, 2);
            canvas.DrawArc(new Vector2(x1, y0), r, Mathf.Pi * 1.5f, Mathf.Pi * 2, 24, line, 2);
            canvas.DrawArc(new Vector2(x1, y1), r, 0, Mathf.Pi * 0.5f, 24, line, 2);
            canvas.DrawArc(new Vector2(x0, y1), r, Mathf.Pi * 0.5f, Mathf.Pi, 24, line, 2);
        }
    }

    /// <summary>
    /// A power node's reach while placing it (FB-020): every cell within its radius, shaded, and a line to each
    /// generator, battery and node it would join (<see cref="TilePower.InRadius(TileGrid, int, int, int, int, int, PlacedBuilding?)"/>).
    /// </summary>
    void DrawNodeRadius(float px, int x, int y, int w, int h, int r)
    {
        var grid = _host.ViewedTile.Grid;
        var shade = Palette.Cable with { A = 0.12f };
        for (int cy = y - r; cy < y + h + r; cy++)
        {
            for (int cx = x - r; cx < x + w + r; cx++)
            {
                int dx = Mathf.Max(0, Mathf.Max(cx - (x + w - 1), x - cx)), dy = Mathf.Max(0, Mathf.Max(cy - (y + h - 1), y - cy));
                if (dx * dx + dy * dy <= r * r && grid.InBounds(cx, cy))
                    DrawRect(new Rect2(cx * px, cy * px, px, px), shade);
            }
        }
        var from = new Vector2(x + w * 0.5f, y + h * 0.5f) * px;
        foreach (var b in TilePower.InRadius(grid, x, y, w, h, r))
        {
            DrawLine(from, new Vector2(b.X + b.Width * 0.5f, b.Y + b.Height * 0.5f) * px, Palette.Cable, 2);
            DrawRect(new Rect2(b.X * px, b.Y * px, b.Width * px, b.Height * px), Palette.Cable, false, 2);
        }
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
        var (w, h) = _host.Sim.Content.Buildings[_tools[_selected].BuildingId].Footprint(_rotation);
        return (cell.X - (w - 1) / 2, cell.Y - (h - 1) / 2, w, h);
    }
}
