using System.Collections.Generic;
using Godot;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Render;

/// <summary>
/// Draws the units on the viewed tile as the blocks they are made of, turned to the unit's heading, with a ring on
/// selected ones and a line to the destination of any selected unit that is moving. Units are few next to buildings,
/// so this is a plain redraw each frame rather than a MultiMesh.
/// </summary>
public partial class UnitView : Node2D
{
    SimHost _host = null!;

    // A unit's blocks never change, so the block under each cell is worked out once per unit.
    readonly Dictionary<int, string?[]> _cellBlocks = [];

    public override void _Ready()
    {
        _host = GetNode<SimHost>("/root/SimHost");
        _host.ViewChanged += OnViewChanged;
        OnViewChanged();
    }

    public override void _ExitTree() => _host.ViewChanged -= OnViewChanged;

    void OnViewChanged()
    {
        bool shown = _host.View == ViewMode.Tile;
        Visible = shown;
        ProcessMode = shown ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
    }

    public override void _Process(double delta) => QueueRedraw();

    /// <summary>Screen position of a point on the viewed tile, in pixels.</summary>
    public static Vector2 PixelPos(int x, int y) => new Vector2(x, y) / Unit.CellScale * Palette.CellPx;

    public override void _Draw()
    {
        var tile = _host.ViewedTile;
        foreach (var u in _host.Sim.Units)
        {
            if (u.TileId != tile.Id)
                continue;
            DrawUnit(u);
        }
    }

    void DrawUnit(Unit u)
    {
        float px = Palette.CellPx;
        var pos = PixelPos(u.X, u.Y);
        bool selected = _host.Selected.Contains(u.Id);
        if (selected && u.Moving && u.DestTileId == _host.ViewedTile.Id)
        {
            var dest = PixelPos(u.DestX, u.DestY);
            DrawLine(pos, dest, Colors.White with { A = 0.4f }, 1);
            DrawCircle(dest, 2.5f, Colors.White with { A = 0.7f });
        }

        if (!_cellBlocks.TryGetValue(u.Id, out var blocks))
            _cellBlocks[u.Id] = blocks = BlockPerCell(u);

        var outline = Palette.Unit(u.Owner);
        var body = new Transform2D(u.Angle * Mathf.Tau / Trig.Turn, pos);
        DrawSetTransformMatrix(body);
        var frameTex = Sprites.Get(Sprites.Frame);
        for (int i = 0; i < u.Cells.Count; i++)
        {
            var c = u.Cells[i];
            var centre = new Vector2(c.OffX, c.OffY) / Unit.CellScale * px;
            var cell = new Rect2(centre - Vector2.One * px / 2, Vector2.One * px);
            if (frameTex != null)
                DrawTextureRect(frameTex, cell, false);
            else
                DrawRect(cell, Palette.Frame with { A = 0.7f });
            if (blocks[i] is { } defId && Sprites.Get(defId) == null)
                DrawRect(cell.Grow(-px * 0.12f), Palette.Building(defId));
        }
        foreach (var b in u.Blocks)
        {
            if (Sprites.Get(b.DefId) is { } tex)
                DrawBlockSprite(u, b, tex, body, px);
            else if (IsThruster(b))
                DrawThrusterMark(u, b, px);
        }
        DrawSetTransformMatrix(body);
        foreach (var c in u.Cells)
        {
            var centre = new Vector2(c.OffX, c.OffY) / Unit.CellScale * px;
            DrawRect(new Rect2(centre - Vector2.One * px / 2, Vector2.One * px), outline, false, 1.5f);
        }
        DrawSetTransform(Vector2.Zero);

        if (selected)
            DrawArc(pos, u.Radius / (float)Unit.CellScale * px + 3, 0, Mathf.Tau, 32, Colors.White, 2);
    }

    /// <summary>
    /// A block's sprite across its footprint, turned by its rotation like <see cref="TileView.Rect"/>. Leaves the
    /// canvas transform changed; the caller resets it.
    /// </summary>
    void DrawBlockSprite(Unit u, UnitBlock b, Texture2D tex, Transform2D body, float px)
    {
        foreach (var c in u.Cells)
        {
            if (c.GX != b.GX || c.GY != b.GY)
                continue;
            // The top-left cell's centre, moved to the middle of the footprint.
            var centre = new Vector2(c.OffX, c.OffY) / Unit.CellScale * px + new Vector2(b.Width - 1, b.Height - 1) * px / 2;
            bool turned = (b.Rotation & 1) == 1;
            var size = new Vector2(turned ? b.Height : b.Width, turned ? b.Width : b.Height) * px;
            DrawSetTransformMatrix(body * new Transform2D((b.Rotation & 3) * Mathf.Pi / 2, centre));
            DrawTextureRect(tex, new Rect2(-size / 2, size), false);
            return;
        }
    }

    /// <summary>An arrow on an engine cell pointing the way it pushes. Drawn in the unit's rotated frame.</summary>
    void DrawThrusterMark(Unit u, UnitBlock b, float px)
    {
        foreach (var c in u.Cells)
        {
            if (c.GX != b.GX || c.GY != b.GY)
                continue;
            var centre = new Vector2(c.OffX, c.OffY) / Unit.CellScale * px;
            float a = (b.Rotation & 3) * Mathf.Pi / 2;
            DrawLine(centre, centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * px * 0.45f, Colors.White, 2);
            return;
        }
    }

    bool IsThruster(UnitBlock b) => _host.Sim.Content.Buildings[b.DefId].Kind == Sim.Content.BuildingKind.Thruster;

    static string?[] BlockPerCell(Unit u)
    {
        var result = new string?[u.Cells.Count];
        for (int i = 0; i < result.Length; i++)
        {
            var c = u.Cells[i];
            foreach (var b in u.Blocks)
            {
                if (c.GX >= b.GX && c.GX < b.GX + b.Width && c.GY >= b.GY && c.GY < b.GY + b.Height)
                    result[i] = b.DefId;
            }
        }
        return result;
    }
}
