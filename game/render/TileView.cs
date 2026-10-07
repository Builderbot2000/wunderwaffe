using System.Collections.Generic;
using Godot;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Render;

/// <summary>
/// Draws the viewed tile's ground, deposits and buildings with one MultiMesh per layer, and per sprite within
/// a layer, so draw cost stays flat as the building count grows (NF-001). Content without a sprite is drawn
/// as a flat <see cref="Palette"/> colour. Rebuilds only when the grid's revision changes.
/// </summary>
public partial class TileView : Node2D
{
    const float BuildingInset = 0.12f;

    SimHost _host = null!;
    MultiMeshInstance2D _ground = null!;
    MultiMeshInstance2D _deposits = null!;
    MultiMeshInstance2D _buildings = null!;
    Node2D _depositIcons = null!;
    Node2D _buildingSprites = null!;
    readonly Dictionary<string, MultiMeshInstance2D> _spriteLayers = [];
    int _drawnRevision = -1;

    public override void _Ready()
    {
        _host = GetNode<SimHost>("/root/SimHost");
        Palette.Init(_host.Sim.Content);
        _ground = AddLayer(this, "Ground", Sprites.Get(Sprites.Ground));
        _deposits = AddLayer(this, "Deposits", null);
        _depositIcons = AddGroup("DepositIcons");
        _buildings = AddLayer(this, "Buildings", null);
        _buildingSprites = AddGroup("BuildingSprites");
    }

    public override void _Process(double delta)
    {
        TileGrid grid = _host.ViewedTile.Grid;
        if (grid.Revision == _drawnRevision)
            return;
        _drawnRevision = grid.Revision;
        DrawGround(grid);
        DrawDeposits(grid);
        DrawBuildings(grid);
    }

    Node2D AddGroup(string name)
    {
        var group = new Node2D { Name = name };
        AddChild(group);
        return group;
    }

    static MultiMeshInstance2D AddLayer(Node parent, string name, Texture2D? texture)
    {
        var layer = new MultiMeshInstance2D
        {
            Name = name,
            Texture = texture,
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
                UseColors = true,
                Mesh = new QuadMesh { Size = Vector2.One },
            },
        };
        parent.AddChild(layer);
        return layer;
    }

    /// <summary>The textured layer for <paramref name="id"/> under <paramref name="group"/>, or null if it has no sprite.</summary>
    MultiMeshInstance2D? SpriteLayer(Node2D group, string id)
    {
        string key = $"{group.Name}/{id}";
        if (_spriteLayers.TryGetValue(key, out var layer))
            return layer;
        var tex = Sprites.Get(id);
        if (tex == null)
            return null;
        return _spriteLayers[key] = AddLayer(group, id, tex);
    }

    void DrawGround(TileGrid grid)
    {
        var mm = _ground.Multimesh;
        if (_ground.Texture is { } tex)
        {
            // One instance per texture-sized block of cells; the texture is drawn at its native pixel size.
            int block = Mathf.Max(1, tex.GetWidth() / Palette.CellPx);
            int bw = (grid.Width + block - 1) / block, bh = (grid.Height + block - 1) / block;
            mm.InstanceCount = bw * bh;
            int i = 0;
            for (int y = 0; y < bh; y++)
            {
                for (int x = 0; x < bw; x++)
                {
                    mm.SetInstanceTransform2D(i, Rect(x * block, y * block, block, block, 0, 0));
                    mm.SetInstanceColor(i++, Colors.White);
                }
            }
            return;
        }

        mm.InstanceCount = grid.Width * grid.Height;
        for (int y = 0, i = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++, i++)
            {
                mm.SetInstanceTransform2D(i, Rect(x, y, 1, 1, 0, 0));
                mm.SetInstanceColor(i, ((x + y) & 1) == 0 ? Palette.Ground : Palette.GroundAlt);
            }
        }
    }

    /// <summary>Deposit cells show the item's sprite on the ground, or a flat colour if the item has none.</summary>
    void DrawDeposits(TileGrid grid)
    {
        var cells = new List<(int X, int Y, string Item)>();
        var icons = new Dictionary<string, List<(int X, int Y)>>();
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                if (grid.DepositAt(x, y) is not { } item)
                    continue;
                if (Sprites.Get(item) == null)
                {
                    cells.Add((x, y, item));
                    continue;
                }
                if (!icons.TryGetValue(item, out var list))
                    icons[item] = list = [];
                list.Add((x, y));
            }
        }

        var mm = _deposits.Multimesh;
        mm.InstanceCount = cells.Count;
        for (int i = 0; i < cells.Count; i++)
        {
            var (x, y, item) = cells[i];
            mm.SetInstanceTransform2D(i, Rect(x, y, 1, 1, 0, 0));
            mm.SetInstanceColor(i, Palette.Deposit(item));
        }

        ClearLayers(_depositIcons);
        foreach (var (item, list) in icons)
        {
            var imm = SpriteLayer(_depositIcons, item)!.Multimesh;
            imm.InstanceCount = list.Count;
            for (int i = 0; i < list.Count; i++)
            {
                imm.SetInstanceTransform2D(i, Rect(list[i].X, list[i].Y, 1, 1, 0.25f, 0));
                imm.SetInstanceColor(i, Colors.White);
            }
        }
    }

    void DrawBuildings(TileGrid grid)
    {
        var plain = new List<PlacedBuilding>();
        var byDef = new Dictionary<string, List<PlacedBuilding>>();
        foreach (var b in grid.Buildings)
        {
            if (Sprites.Get(b.DefId) == null)
            {
                plain.Add(b);
                continue;
            }
            if (!byDef.TryGetValue(b.DefId, out var list))
                byDef[b.DefId] = list = [];
            list.Add(b);
        }

        var mm = _buildings.Multimesh;
        mm.InstanceCount = plain.Count;
        for (int i = 0; i < plain.Count; i++)
        {
            var b = plain[i];
            mm.SetInstanceTransform2D(i, Rect(b.X, b.Y, b.Width, b.Height, BuildingInset, 0));
            mm.SetInstanceColor(i, Palette.Building(b.DefId));
        }

        ClearLayers(_buildingSprites);
        foreach (var (defId, list) in byDef)
        {
            var smm = SpriteLayer(_buildingSprites, defId)!.Multimesh;
            smm.InstanceCount = list.Count;
            for (int i = 0; i < list.Count; i++)
            {
                var b = list[i];
                smm.SetInstanceTransform2D(i, Rect(b.X, b.Y, b.Width, b.Height, 0, b.Rotation));
                smm.SetInstanceColor(i, Colors.White);
            }
        }
    }

    static void ClearLayers(Node2D group)
    {
        foreach (var child in group.GetChildren())
            ((MultiMeshInstance2D)child).Multimesh.InstanceCount = 0;
    }

    /// <summary>
    /// Transform that maps the unit quad onto a cell rectangle, shrunk by <paramref name="inset"/> cells and turned
    /// <paramref name="rotation"/> quarter turns clockwise. <paramref name="w"/>/<paramref name="h"/> are the
    /// footprint after rotation. Y is negated because QuadMesh UVs are laid out for 3D (y up).
    /// </summary>
    public static Transform2D Rect(int x, int y, int w, int h, float inset, int rotation)
    {
        float px = Palette.CellPx;
        bool turned = (rotation & 1) == 1;
        float sw = (turned ? h : w) - inset, sh = (turned ? w : h) - inset;
        var center = new Vector2((x + w * 0.5f) * px, (y + h * 0.5f) * px);
        return new Transform2D(rotation * Mathf.Pi / 2, new Vector2(sw * px, -sh * px), 0, center);
    }
}
