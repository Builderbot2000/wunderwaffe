using System.Collections.Generic;
using System.Linq;
using Godot;
using Wunderwaffe.Game.Input;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Render;

/// <summary>
/// The world layer (WM-001, WM-007): every tile as a square with its owner, what it holds and whether it can
/// be claimed. Click selects, double-click or Enter opens an owned tile, C claims the selected one (WM-006).
/// </summary>
public partial class WorldMapView : Node2D
{
    public const int TilePx = 200;
    const int Gap = 8;
    const int FontSize = 20;

    static readonly Color Unclaimed = new(0.2f, 0.21f, 0.23f);
    static readonly Color Selected = new(1f, 1f, 1f);
    static readonly Color Claimable = new(1f, 0.85f, 0.25f);
    static readonly Color TooPoor = new(0.8f, 0.45f, 0.2f);
    static readonly Color Disputed = new(0.95f, 0.25f, 0.25f);

    SimHost _host = null!;
    readonly Dictionary<int, (string Item, int Count)[]> _deposits = [];

    public int SelectedTileId { get; set; }

    public override void _Ready()
    {
        _host = GetNode<SimHost>("/root/SimHost");
        SelectedTileId = _host.ViewedTile.Id;
        _host.ViewChanged += OnViewChanged;
        OnViewChanged();
    }

    public override void _ExitTree() => _host.ViewChanged -= OnViewChanged;

    void OnViewChanged()
    {
        bool shown = _host.View == ViewMode.World;
        Visible = shown;
        ProcessMode = shown ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        if (shown)
            SelectedTileId = _host.ViewedTile.Id;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            if (TileUnderMouse() is { } hit)
            {
                SelectedTileId = hit.Id;
                if (click.DoubleClick)
                    _host.EnterTile(hit.Id);
            }
        }
        else if (e.IsActionPressed(InputActions.WorldEnter))
        {
            _host.EnterTile(SelectedTileId);
        }
        else if (e.IsActionPressed(InputActions.WorldClaim))
        {
            _host.Sim.Enqueue(new ClaimTileCommand(_host.LocalPlayer, SelectedTileId));
        }
    }

    static Vector2I TileCoord(Vector2 world) =>
        new(Mathf.FloorToInt(world.X / TilePx), Mathf.FloorToInt(world.Y / TilePx));

    TileState? TileUnderMouse()
    {
        var cell = TileCoord(GetGlobalMousePosition());
        return _host.Sim.TileAt(cell.X, cell.Y);
    }

    static Vector2 Center(TileState tile) => new((tile.X + 0.5f) * TilePx, (tile.Y + 0.5f) * TilePx);

    /// <summary>
    /// One arrow per tile edge that has export ports, with a dot for each item still on its way (IO-005).
    /// </summary>
    void DrawLinks()
    {
        var sim = _host.Sim;
        var line = new Color(0.75f, 0.95f, 1f);
        foreach (var tile in sim.Tiles)
        {
            foreach (var side in Directions.All)
            {
                var ports = tile.Grid.ExportPorts.Where(p => PortRules.SideOf(p.Rotation) == side).ToList();
                if (ports.Count == 0 || sim.Neighbor(tile, side) is not { } to)
                    continue;

                var dir = new Vector2(side.Dx(), side.Dy());
                var perp = new Vector2(-dir.Y, dir.X);
                // Opposite links between the same two tiles run side by side instead of on top of each other.
                var a = Center(tile) + dir * 34 + perp * 12;
                var b = Center(to) - dir * 34 + perp * 12;
                DrawLine(a, b, line, 4);
                DrawColoredPolygon([b + dir * 10, b - dir * 6 + perp * 9, b - dir * 6 - perp * 9], line);

                foreach (var shipment in ports.SelectMany(p => p.Export!.InFlight))
                {
                    float t = 1f - (float)(shipment.ArriveTick - sim.Tick) / PortRules.TransitTicksPerTile;
                    DrawCircle(a.Lerp(b, Mathf.Clamp(t, 0f, 1f)), 3.5f, Palette.Deposit(shipment.ItemId).Lightened(0.5f));
                }

                // Drawn last so the dots can't cover it, on the outer side of the arrow.
                var textPos = (a + b) / 2 + perp * 20 + new Vector2(-24, dir.X != 0 ? 4 : 0);
                DrawString(ThemeDB.FallbackFont, textPos, ports.Count == 1 ? "1 port" : $"{ports.Count} ports",
                    HorizontalAlignment.Left, -1, FontSize - 4, Colors.White);
            }
        }
    }

    public override void _Draw()
    {
        var sim = _host.Sim;
        var font = ThemeDB.FallbackFont;
        foreach (var tile in sim.Tiles)
        {
            var rect = new Rect2(tile.X * TilePx + Gap / 2, tile.Y * TilePx + Gap / 2, TilePx - Gap, TilePx - Gap);
            DrawRect(rect, tile.Owner == TileState.Unowned ? Unclaimed : OwnerColor(tile.Owner));
            DrawDeposits(tile, rect);
            DrawRegional(tile, rect);

            var claim = Expansion.Check(sim, _host.LocalPlayer, tile.Id, out _);
            if (claim == ClaimResult.Ok)
                DrawRect(rect, Claimable, false, 3);
            else if (claim == ClaimResult.CannotAfford)
                DrawRect(rect, TooPoor, false, 3);
            if (tile.Contested)
                DrawRect(rect.Grow(-4), Disputed, false, 4);
            if (tile.Id == SelectedTileId)
                DrawRect(rect.Grow(2), Selected, false, 3);

            var pos = rect.Position + new Vector2(10, FontSize + 6);
            DrawString(font, pos, $"({tile.X},{tile.Y})", HorizontalAlignment.Left, -1, FontSize, Colors.White);
            DrawString(font, pos + new Vector2(0, FontSize + 4), Describe(tile), HorizontalAlignment.Left, -1, FontSize - 4, Colors.White);
        }
        DrawLinks();
        DrawUnits();
    }

    /// <summary>Each unit as a dot where it stands on its tile, ringed if selected.</summary>
    void DrawUnits()
    {
        var sim = _host.Sim;
        foreach (var u in sim.Units)
        {
            var tile = sim.Tiles[u.TileId];
            var rect = new Rect2(tile.X * TilePx + Gap / 2, tile.Y * TilePx + Gap / 2, TilePx - Gap, TilePx - Gap);
            var frac = new Vector2(u.X / (float)(tile.Grid.Width * Sim.World.Unit.CellScale), u.Y / (float)(tile.Grid.Height * Sim.World.Unit.CellScale));
            var pos = rect.Position + frac * rect.Size;
            DrawCircle(pos, 7, Palette.Unit(u.Owner));
            DrawArc(pos, 7, 0, Mathf.Tau, 16, Colors.Black, 1.5f);
            if (_host.Selected.Contains(u.Id))
                DrawArc(pos, 11, 0, Mathf.Tau, 20, Colors.White, 2.5f);
        }
    }

    /// <summary>Deposits never change after generation, so each tile is counted once.</summary>
    (string Item, int Count)[] Deposits(TileState tile)
    {
        if (!_deposits.TryGetValue(tile.Id, out var items))
            items = _deposits[tile.Id] = tile.Grid.DepositCounts().Select(kv => (kv.Key, kv.Value)).ToArray();
        return items;
    }

    /// <summary>An icon in the top-right corner for each regional deposit, which only some tiles have (RS-003).</summary>
    void DrawRegional(TileState tile, Rect2 rect)
    {
        var content = _host.Sim.Content;
        float x = rect.End.X - 42;
        foreach (var (item, _) in Deposits(tile))
        {
            if (!content.Items[item].Regional)
                continue;
            var box = new Rect2(x, rect.Position.Y + 8, 34, 34);
            DrawRect(box, new Color(0, 0, 0, 0.45f));
            if (Sprites.Get(item) is { } tex)
                DrawTextureRect(tex, box, false);
            else
                DrawCircle(box.GetCenter(), 12, Palette.Deposit(item));
            x -= 38;
        }
    }

    /// <summary>A stripe along the bottom, split between the tile's deposits in proportion to their size (WM-002).</summary>
    void DrawDeposits(TileState tile, Rect2 rect)
    {
        var items = Deposits(tile);
        int total = items.Sum(i => i.Count);
        if (total == 0)
            return;
        float x = rect.Position.X + 8, width = rect.Size.X - 16, y = rect.End.Y - 18;
        foreach (var (item, count) in items)
        {
            float w = width * count / total;
            DrawRect(new Rect2(x, y, w, 10), Palette.Deposit(item));
            x += w;
        }
    }

    string Describe(TileState tile) => tile.Ownership switch
    {
        TileOwnership.Contested => "Contested",
        TileOwnership.Unclaimed => "Unclaimed",
        _ when tile.Owner == _host.LocalPlayer => $"You · {tile.Grid.BuildingCount} bldgs",
        _ => $"Player {tile.Owner}",
    };

    static Color OwnerColor(int owner) => Color.FromHsv((0.45f + owner * 0.618034f) % 1f, 0.55f, 0.55f);

    /// <summary>Screen pixels kept clear for the HUD's side panels.</summary>
    const float HudLeft = 440, HudRight = 460;

    /// <summary>Centre and zoom that fit the whole world in the part of the viewport the HUD doesn't cover.</summary>
    public (Vector2 Center, float Zoom) Fit(Vector2 viewport)
    {
        var sim = _host.Sim;
        int w = sim.Tiles.Max(t => t.X) + 1, h = sim.Tiles.Max(t => t.Y) + 1;
        float zoom = Mathf.Min((viewport.X - HudLeft - HudRight) / (w * TilePx), (viewport.Y - 120) / (h * TilePx)) * 0.92f;
        // Shifting the camera toward the wider panel pushes the map into the gap between them.
        var center = new Vector2(w, h) * TilePx / 2 - new Vector2((HudLeft - HudRight) / 2 / zoom, 0);
        return (center, zoom);
    }
}
