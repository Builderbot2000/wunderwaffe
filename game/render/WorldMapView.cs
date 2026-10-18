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
/// a tile you have presence on, C sends the selected units to the selected tile's victory circle (WM-006). Every
/// tile's circle is drawn with each claimant's progress as an arc (WM-014), and Contested tiles are outlined (WM-004).
/// </summary>
public partial class WorldMapView : Node2D
{
    /// <summary>
    /// Pixels per world cell. 2 px for a 96-cell tile, scaled with the tile size (OQ-35) so a tile, its label and its
    /// unit dots keep the same proportions on screen.
    /// </summary>
    public const float CellPx = 2f * 96 / Sim.Simulation.DefaultTileSize;

    const int FontSize = 20;
    const float OwnerTint = 0.3f;

    static readonly Color Border = new(0f, 0f, 0f, 0.55f);
    static readonly Color Selected = new(1f, 1f, 1f);
    static readonly Color Contested = new(1f, 0.3f, 0.2f);
    static readonly Color Circle = new(1f, 1f, 1f, 0.7f);

    SimHost _host = null!;
    WorldSnapshot? _snapshot;
    ImageTexture? _owners;
    long _ownerKey = -1;
    Node2D _borders = null!;
    Node2D _overlay = null!;
    readonly Dictionary<int, (string Item, int Count)[]> _deposits = [];
    readonly Dictionary<int, Vector2[]> _outlines = [];

    public int SelectedTileId { get; set; }

    /// <summary>The logistics overlay (IO-005): link arrows with items per minute and transit time. L toggles it.</summary>
    public bool ShowLogistics { get; set; } = true;

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
        // Under the tile layer, which fades over it near the zoom threshold (WM-007).
        ZIndex = -1;
        _host.WatchView(this, OnViewChanged);
    }

    void OnViewChanged()
    {
        if (_host.View != ViewMode.World)
            return;
        SelectedTileId = _host.ViewedTile.Id;
        if (_snapshot?.Sim == _host.Sim && _host.ToggleCount % GameSettings.SnapshotEvery == 0 && _snapshot.Refresh() > 0)
            QueueRedraw();
    }

    /// <summary>
    /// Shown on the world map, and under the tile view while it fades (WM-007). The world map's canvas is world-map
    /// pixels; in the tile view it is scaled into the viewed tile's local canvas so the two line up.
    /// </summary>
    bool Place()
    {
        bool world = _host.View == ViewMode.World;
        Visible = world || _host.TileOpacity < 1;
        var origin = new Vector2(_host.ViewedTile.OriginX, _host.ViewedTile.OriginY);
        Scale = world ? Vector2.One : Vector2.One * (Palette.CellPx / CellPx);
        Position = world ? Vector2.Zero : -origin * Palette.CellPx;
        return Visible;
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
        if (_snapshot?.Sim != _host.Sim)
            Rebuild();
        if (!Place())
            return;
        if (OwnersChanged())
            QueueRedraw();
        _overlay.QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_host.View != ViewMode.World)
            return;
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            if (TileUnderMouse() is { } hit)
            {
                SelectedTileId = hit.Id;
                if (click.DoubleClick)
                    _host.ZoomInto(hit.Id);
            }
        }
        else if (e.IsActionPressed(InputActions.WorldEnter))
        {
            _host.ZoomInto(SelectedTileId);
        }
        else if (e.IsActionPressed(InputActions.WorldClaim))
        {
            if (_host.Sim.TryGetTile(SelectedTileId) is { } tile && _host.Selected.Count > 0)
                _host.Send(new MoveUnitsCommand(_host.LocalPlayer, [.. _host.Selected.Order()], tile.Id, tile.CircleX, tile.CircleY));
        }
        else if (e.IsActionPressed(InputActions.LogisticsToggle))
        {
            ShowLogistics = !ShowLogistics;
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
        // Filled as bytes, not SetPixel: a 7 x 7 world of 256-cell tiles is ~800k pixels (OQ-35).
        var pixels = new byte[w * h * 4];
        var tiles = _host.Sim.Tiles;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int id = map.TileAtCell(x * d, y * d);
                if (id < 0 || tiles[id].Owner == TileState.Unowned)
                    continue;
                var c = Palette.Owner(tiles[id].Owner, 0.55f);
                int i = (y * w + x) * 4;
                pixels[i] = (byte)(c.R * 255);
                pixels[i + 1] = (byte)(c.G * 255);
                pixels[i + 2] = (byte)(c.B * 255);
                pixels[i + 3] = (byte)(OwnerTint * 255);
            }
        }
        var image = Image.CreateFromData(w, h, false, Image.Format.Rgba8, pixels);
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
        var contested = Territory.ContestedTiles(sim);
        int capture = Expansion.CaptureTicks(sim);
        foreach (var tile in sim.Tiles)
        {
            if (contested[tile.Id])
                _overlay.DrawMultiline(Outline(tile), Contested, 4);
            DrawCircle(tile, capture);
        }
        if (sim.TryGetTile(SelectedTileId) is { } selected)
            _overlay.DrawMultiline(Outline(selected), Selected, 3);

        // Labels stop growing past twice their size as the camera zooms toward a tile (WM-007).
        float labelScale = Mathf.Min(1f, 2f / _overlay.GetGlobalTransformWithCanvas().Scale.X);
        foreach (var tile in sim.Tiles)
        {
            _overlay.DrawSetTransform(Center(tile), 0, Vector2.One * labelScale);
            string name = tile.Name, what = Describe(tile);
            float width = Mathf.Max(font.GetStringSize(name, HorizontalAlignment.Left, -1, FontSize).X,
                font.GetStringSize(what, HorizontalAlignment.Left, -1, FontSize - 4).X);
            var box = new Rect2(-width / 2 - 6, -FontSize - 8, width + 12, FontSize * 2 + 22);
            _overlay.DrawRect(box, new Color(0, 0, 0, 0.45f));
            _overlay.DrawString(font, new Vector2(box.Position.X + 6, -4), name, HorizontalAlignment.Left, -1, FontSize, Colors.White);
            _overlay.DrawString(font, new Vector2(box.Position.X + 6, FontSize - 6), what, HorizontalAlignment.Left, -1, FontSize - 4, Colors.White);
            DrawDeposits(tile, new Rect2(box.Position.X, box.End.Y - 8, box.Size.X, 6));
            DrawRegional(tile, new Vector2(box.End.X + 4, box.Position.Y));
        }
        _overlay.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        if (ShowLogistics)
            DrawLinks();
        DrawDisrupted();
        DrawUnits();
    }

    /// <summary>
    /// One arrow per pair of linked zones that has export ports, between the two tiles' centres, with a dot for each
    /// item still on its way, its items per minute over the last minute and its transit time (IO-002, IO-005).
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
                _overlay.DrawString(ThemeDB.FallbackFont, (a + b) / 2 + perp * 20, LinkLabel(tile, to.Id, n),
                    HorizontalAlignment.Left, -1, FontSize - 4, Colors.White);
            }
        }
    }

    /// <summary>Throughput and latency of one link, as the world map and side panel show it (IO-002).</summary>
    public static string LinkLabel(TileState from, int to, int ports) =>
        $"{(ports == 1 ? "1 port" : $"{ports} ports")} · {from.Stats.SentPerMinute(to)}/min · {PortRules.TransitTicksPerTile / Sim.Simulation.TicksPerSecond} s";

    /// <summary>A red broken line between tiles whose port link lost a port to combat (IO-006).</summary>
    void DrawDisrupted()
    {
        var sim = _host.Sim;
        var red = new Color(1f, 0.3f, 0.25f);
        foreach (var tile in sim.Tiles)
        {
            foreach (var n in sim.Neighbors(tile))
            {
                if (n.Id < tile.Id || !Combat.LinkDisrupted(sim, tile, n))
                    continue;
                Vector2 a = Center(tile), b = Center(n);
                _overlay.DrawDashedLine(a, b, red, 3, 10);
                _overlay.DrawString(ThemeDB.FallbackFont, (a + b) / 2 + new Vector2(6, -6), "Disrupted",
                    HorizontalAlignment.Left, -1, FontSize - 2, red);
            }
        }
    }

    /// <summary>
    /// The tile's victory circle, in its owner's colour, with an arc per player claiming it showing how far along they are.
    /// </summary>
    void DrawCircle(TileState tile, int capture)
    {
        var c = new Vector2(tile.OriginX + tile.CircleX + 0.5f, tile.OriginY + tile.CircleY + 0.5f) * CellPx;
        float r = Sim.Balance.CircleRadius * CellPx;
        var ring = tile.Owner == TileState.Unowned ? Circle : Palette.Owner(tile.Owner);
        _overlay.DrawArc(c, r, 0, Mathf.Tau, 48, ring, 2.5f);
        float inner = r - 5;
        foreach (var (player, ticks) in tile.ClaimTicks)
        {
            float t = Mathf.Clamp((float)ticks / capture, 0f, 1f);
            _overlay.DrawArc(c, inner, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * t, 48, Palette.Owner(player), 5f);
            inner -= 6;
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
