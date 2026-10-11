using System.Collections.Generic;
using System.Linq;
using Godot;
using Wunderwaffe.Game.Input;
using Wunderwaffe.Game.Screens;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Render;

/// <summary>
/// The world layer (WM-001, WM-007): the irregular tiles over the world's terrain, drawn from a blurred picture of
/// every tile's local map (<see cref="WorldSnapshot"/>, refreshed every few view switches), tinted by owner, with
/// borders, labels, what each tile holds, links between ports and units. Click selects, double-click or Enter opens
/// an owned tile, C claims the selected one (WM-006).
/// </summary>
public partial class WorldMapView : Node2D
{
    /// <summary>Pixels per world cell.</summary>
    public const float CellPx = 2f;

    const int FontSize = 20;
    const float OwnerTint = 0.3f;

    static readonly Color Border = new(0f, 0f, 0f, 0.55f);
    static readonly Color Selected = new(1f, 1f, 1f);
    static readonly Color Claimable = new(1f, 0.85f, 0.25f);
    static readonly Color TooPoor = new(0.8f, 0.45f, 0.2f);

    SimHost _host = null!;
    WorldSnapshot? _snapshot;
    ImageTexture? _owners;
    long _ownerKey = -1;
    Node2D _borders = null!;
    Node2D _overlay = null!;
    readonly Dictionary<int, (string Item, int Count)[]> _deposits = [];
    readonly Dictionary<int, Vector2[]> _outlines = [];

    public int SelectedTileId { get; set; }

    public override void _Ready()
    {
        _host = SimHost.Instance;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        _borders = new Node2D { Name = "Borders" };
        _borders.Draw += DrawBorders;
        AddChild(_borders);
        _overlay = new Node2D { Name = "Overlay", TextureFilter = TextureFilterEnum.Linear };
        _overlay.Draw += DrawOverlay;
        AddChild(_overlay);
        SelectedTileId = _host.ViewedTile.Id;
        _host.ShowOnlyIn(this, ViewMode.World, OnViewChanged);
    }

    void OnViewChanged()
    {
        if (!Visible)
            return;
        SelectedTileId = _host.ViewedTile.Id;
        if (_snapshot?.Sim != _host.Sim)
            Rebuild();
        else if (_host.ToggleCount % GameSettings.SnapshotEvery == 0 && _snapshot.Refresh() > 0)
            QueueRedraw();
    }

    /// <summary>A new match: everything cached about the old world goes.</summary>
    void Rebuild()
    {
        _snapshot = new WorldSnapshot(_host.Sim);
        _deposits.Clear();
        _outlines.Clear();
        _ownerKey = -1;
        _borders.QueueRedraw();
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (OwnersChanged())
            QueueRedraw();
        _overlay.QueueRedraw();
    }

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
            _host.Send(new ClaimTileCommand(_host.LocalPlayer, SelectedTileId));
        }
    }

    /// <summary>The world cell under a point of the world map.</summary>
    public static Vector2I CellAt(Vector2 world) => new(Mathf.FloorToInt(world.X / CellPx), Mathf.FloorToInt(world.Y / CellPx));

    TileState? TileUnderMouse()
    {
        var cell = CellAt(GetGlobalMousePosition());
        return _host.Sim.TileAtCell(cell.X, cell.Y);
    }

    /// <summary>Where a tile's label sits: the centre of its cells.</summary>
    public static Vector2 Center(TileState tile) => new Vector2(tile.CenterX + 0.5f, tile.CenterY + 0.5f) * CellPx;

    Vector2[] Outline(TileState tile)
    {
        if (!_outlines.TryGetValue(tile.Id, out var lines))
            lines = _outlines[tile.Id] = TileShapes.ToPixels(TileShapes.Outline(tile), CellPx, new Vector2(tile.OriginX, tile.OriginY));
        return lines;
    }

    Rect2 WorldRect => new(0, 0, _host.Sim.Map.Width * CellPx, _host.Sim.Map.Height * CellPx);

    /// <summary>The picture of the tiles and the owner tint over it. Redrawn only when either changes.</summary>
    public override void _Draw()
    {
        if (_snapshot == null)
            return;
        DrawTextureRect(_snapshot.Texture, WorldRect, false);
        if (_owners != null)
            DrawTextureRect(_owners, WorldRect, false);
    }

    /// <summary>Rebuilds the owner tint when any tile's owner changed. Returns true if it did.</summary>
    bool OwnersChanged()
    {
        long key = 17;
        foreach (var t in _host.Sim.Tiles)
            key = key * 31 + t.Owner;
        if (key == _ownerKey && _owners != null)
            return false;
        _ownerKey = key;

        var map = _host.Sim.Map;
        int d = WorldSnapshot.Downsample;
        int w = (map.Width + d - 1) / d, h = (map.Height + d - 1) / d;
        var image = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        var tiles = _host.Sim.Tiles;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int id = map.TileAtCell(x * d, y * d);
                if (id < 0)
                    continue;
                int owner = tiles[id].Owner;
                if (owner != TileState.Unowned)
                    image.SetPixel(x, y, Palette.Owner(owner, 0.55f) with { A = OwnerTint });
            }
        }
        image.GenerateMipmaps();
        _owners = ImageTexture.CreateFromImage(image);
        return true;
    }

    void DrawBorders()
    {
        foreach (var t in _host.Sim.Tiles)
            _borders.DrawMultiline(Outline(t), Border, 1.5f);
    }

    void DrawOverlay()
    {
        var sim = _host.Sim;
        var font = ThemeDB.FallbackFont;
        foreach (var tile in sim.Tiles)
        {
            var claim = Expansion.Check(sim, _host.LocalPlayer, tile.Id, out _);
            if (claim == ClaimResult.Ok)
                _overlay.DrawMultiline(Outline(tile), Claimable, 3);
            else if (claim == ClaimResult.CannotAfford)
                _overlay.DrawMultiline(Outline(tile), TooPoor, 3);
        }
        if (sim.TryGetTile(SelectedTileId) is { } selected)
            _overlay.DrawMultiline(Outline(selected), Selected, 3);

        foreach (var tile in sim.Tiles)
        {
            var c = Center(tile);
            string name = tile.Name, what = Describe(tile);
            float width = Mathf.Max(font.GetStringSize(name, HorizontalAlignment.Left, -1, FontSize).X,
                font.GetStringSize(what, HorizontalAlignment.Left, -1, FontSize - 4).X);
            var box = new Rect2(c.X - width / 2 - 6, c.Y - FontSize - 8, width + 12, FontSize * 2 + 22);
            _overlay.DrawRect(box, new Color(0, 0, 0, 0.45f));
            _overlay.DrawString(font, new Vector2(box.Position.X + 6, c.Y - 4), name, HorizontalAlignment.Left, -1, FontSize, Colors.White);
            _overlay.DrawString(font, new Vector2(box.Position.X + 6, c.Y + FontSize - 6), what, HorizontalAlignment.Left, -1, FontSize - 4, Colors.White);
            DrawDeposits(tile, new Rect2(box.Position.X, box.End.Y - 8, box.Size.X, 6));
            DrawRegional(tile, new Vector2(box.End.X + 4, box.Position.Y));
        }
        DrawLinks();
        DrawUnits();
    }

    /// <summary>
    /// One arrow per pair of linked zones that has export ports, between the two tiles' centres, with a dot for each
    /// item still on its way (IO-005).
    /// </summary>
    void DrawLinks()
    {
        var sim = _host.Sim;
        var line = new Color(0.75f, 0.95f, 1f);
        foreach (var tile in sim.Tiles)
        {
            foreach (var group in tile.Grid.ExportPorts.GroupBy(p => PortRules.PartnerOf(tile.Grid, p)))
            {
                if (sim.TryGetTile(group.Key) is not { } to)
                    continue;
                var from = Center(tile);
                var dir = (Center(to) - from).Normalized();
                var perp = new Vector2(-dir.Y, dir.X);
                // Opposite links between the same two tiles run side by side instead of on top of each other.
                var a = from + dir * 40 + perp * 12;
                var b = Center(to) - dir * 40 + perp * 12;
                _overlay.DrawLine(a, b, line, 4);
                _overlay.DrawColoredPolygon([b + dir * 10, b - dir * 6 + perp * 9, b - dir * 6 - perp * 9], line);

                foreach (var shipment in group.SelectMany(p => p.Export!.InFlight))
                {
                    float t = 1f - (float)(shipment.ArriveTick - sim.Tick) / PortRules.TransitTicksPerTile;
                    _overlay.DrawCircle(a.Lerp(b, Mathf.Clamp(t, 0f, 1f)), 3.5f, Palette.Deposit(shipment.ItemId).Lightened(0.5f));
                }

                int n = group.Count();
                _overlay.DrawString(ThemeDB.FallbackFont, (a + b) / 2 + perp * 20, n == 1 ? "1 port" : $"{n} ports",
                    HorizontalAlignment.Left, -1, FontSize - 4, Colors.White);
            }
        }
    }

    /// <summary>Each unit as a dot where it is in the world, ringed if selected.</summary>
    void DrawUnits()
    {
        foreach (var u in _host.Sim.Units)
        {
            var pos = new Vector2(u.X, u.Y) / Sim.World.Unit.CellScale * CellPx;
            _overlay.DrawCircle(pos, 7, Palette.Owner(u.Owner));
            _overlay.DrawArc(pos, 7, 0, Mathf.Tau, 16, Colors.Black, 1.5f);
            if (_host.Selected.Contains(u.Id))
                _overlay.DrawArc(pos, 11, 0, Mathf.Tau, 20, Colors.White, 2.5f);
        }
    }

    /// <summary>Deposits never change after generation, so each tile is counted once.</summary>
    (string Item, int Count)[] Deposits(TileState tile)
    {
        if (!_deposits.TryGetValue(tile.Id, out var items))
            items = _deposits[tile.Id] = tile.Grid.DepositCounts().Select(kv => (kv.Key, kv.Value)).ToArray();
        return items;
    }

    /// <summary>An icon beside the label for each regional deposit, which only some tiles have (RS-003).</summary>
    void DrawRegional(TileState tile, Vector2 at)
    {
        var content = _host.Sim.Content;
        foreach (var (item, _) in Deposits(tile))
        {
            if (!content.Items[item].Regional)
                continue;
            var box = new Rect2(at, new Vector2(30, 30));
            _overlay.DrawRect(box, new Color(0, 0, 0, 0.45f));
            if (Sprites.Get(item) is { } tex)
                _overlay.DrawTextureRect(tex, box, false);
            else
                _overlay.DrawCircle(box.GetCenter(), 11, Palette.Deposit(item));
            at.X += 34;
        }
    }

    /// <summary>A stripe split between the tile's deposits in proportion to their size (WM-002).</summary>
    void DrawDeposits(TileState tile, Rect2 stripe)
    {
        var items = Deposits(tile);
        int total = items.Sum(i => i.Count);
        if (total == 0)
            return;
        float x = stripe.Position.X;
        foreach (var (item, count) in items)
        {
            float w = stripe.Size.X * count / total;
            _overlay.DrawRect(new Rect2(x, stripe.Position.Y, w, stripe.Size.Y), Palette.Deposit(item));
            x += w;
        }
    }

    string Describe(TileState tile) => tile.Owner switch
    {
        TileState.Unowned => "unclaimed",
        _ when tile.Owner == _host.LocalPlayer => $"you · {tile.Grid.BuildingCount} bldgs",
        _ => $"player {tile.Owner}",
    };

    /// <summary>Screen pixels kept clear for the HUD's side panels.</summary>
    const float HudLeft = 440, HudRight = 460;

    /// <summary>Centre and zoom that fit the whole world in the part of the viewport the HUD doesn't cover.</summary>
    public (Vector2 Center, float Zoom) Fit(Vector2 viewport)
    {
        var size = WorldRect.Size;
        float zoom = Mathf.Min((viewport.X - HudLeft - HudRight) / size.X, (viewport.Y - 120) / size.Y) * 0.92f;
        // Shifting the camera toward the wider panel pushes the map into the gap between them.
        var center = size / 2 - new Vector2((HudLeft - HudRight) / 2 / zoom, 0);
        return (center, zoom);
    }
}
