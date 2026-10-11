using System.Collections.Generic;
using Godot;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Render;

/// <summary>
/// Draws the viewed tile's ground, deposits and buildings with one MultiMesh per layer, and per sprite within
/// a layer, so draw cost stays flat as the building count grows (NF-001). Content without a sprite is drawn
/// as a flat <see cref="Palette"/> colour. Rebuilds only when the grid's revision changes.
/// The ground is the tile's terrain, one colour per cell. The tile's bounding box also shows the land of the tiles
/// around it, dimmed, with the border drawn over it and each I/O zone outlined in its own colour.
/// </summary>
public partial class TileView : Node2D
{
    const float BuildingInset = 0.12f;

    /// <summary>How much of the ground sprite's texture shows over the terrain colours.</summary>
    const float GroundDetail = 0.3f;

    /// <summary>Item squares on belts, as a fraction of a cell.</summary>
    const float ItemSize = 0.34f;

    /// <summary>Item sprites on belts. Larger than the squares because the icons have transparent margins.</summary>
    const float ItemSpriteSize = 0.45f;

    SimHost _host = null!;
    Sprite2D _terrain = null!;
    Node2D _outline = null!;
    MultiMeshInstance2D _ground = null!;
    MultiMeshInstance2D _deposits = null!;
    MultiMeshInstance2D _frames = null!;
    MultiMeshInstance2D _platforms = null!;
    MultiMeshInstance2D _buildings = null!;
    Node2D _depositIcons = null!;
    Node2D _buildingSprites = null!;
    MultiMeshInstance2D _items = null!;
    Node2D _itemSprites = null!;
    readonly Dictionary<string, int> _itemCounts = [];
    readonly Dictionary<string, MultiMeshInstance2D> _spriteLayers = [];
    int _drawnRevision = -1;
    int _drawnTileId = -1;

    public override void _Ready()
    {
        _host = SimHost.Instance;
        _host.ShowOnlyIn(this, ViewMode.Tile);
        _terrain = new Sprite2D
        {
            Name = "Terrain",
            Centered = false,
            Scale = Vector2.One * Palette.CellPx,
            TextureFilter = TextureFilterEnum.Nearest,
        };
        AddChild(_terrain);
        _ground = AddLayer(this, "Ground", Sprites.Get(Sprites.Ground));
        _ground.SelfModulate = Colors.White with { A = GroundDetail };
        _deposits = AddLayer(this, "Deposits", null);
        _depositIcons = AddGroup("DepositIcons");
        _platforms = AddLayer(this, "Platforms", Sprites.Get(Sprites.Platform));
        _frames = AddLayer(this, "Frames", Sprites.Get(Sprites.Frame));
        _buildings = AddLayer(this, "Buildings", null);
        _buildingSprites = AddGroup("BuildingSprites");
        _items = AddLayer(this, "Items", null);
        _itemSprites = AddGroup("ItemSprites");
        _outline = AddGroup("Outline");
        _outline.Draw += DrawOutline;
    }

    public override void _Process(double delta)
    {
        var tile = _host.ViewedTile;
        TileGrid grid = tile.Grid;
        // Grids of different tiles can share a revision number, so the tile id is part of "what's drawn".
        if (grid.Revision != _drawnRevision || tile.Id != _drawnTileId)
        {
            if (tile.Id != _drawnTileId)
            {
                DrawTerrain(tile);
                _outline.QueueRedraw();
            }
            _drawnRevision = grid.Revision;
            _drawnTileId = tile.Id;
            DrawGround(grid);
            DrawDeposits(grid);
            DrawPlatforms(grid);
            DrawFrames(grid);
            DrawBuildings(grid);
        }
        DrawItems(grid);
    }

    /// <summary>
    /// Items riding on belts, one layer per item sprite plus a flat-colour layer for items without one. They move
    /// every tick, so unlike the layers above these are rewritten each frame. Instance buffers only grow;
    /// <c>VisibleInstanceCount</c> says how much of each is in use.
    /// </summary>
    void DrawItems(TileGrid grid)
    {
        _itemCounts.Clear();
        int plain = 0;
        foreach (var b in grid.Belts)
        {
            var belt = b.Belt!;
            for (int k = 0; k < belt.Count; k++)
            {
                string item = belt.Items[k]!;
                if (Sprites.Get(item) == null)
                    plain++;
                else
                    _itemCounts[item] = _itemCounts.GetValueOrDefault(item) + 1;
            }
        }

        foreach (var child in _itemSprites.GetChildren())
            ((MultiMeshInstance2D)child).Multimesh.VisibleInstanceCount = 0;
        Reserve(_items.Multimesh, plain);
        foreach (var (item, count) in _itemCounts)
            Reserve(SpriteLayer(_itemSprites, item)!.Multimesh, count);

        float px = Palette.CellPx;
        float size = px * ItemSize, spriteSize = px * ItemSpriteSize;
        int iPlain = 0;
        _itemCounts.Clear();
        foreach (var b in grid.Belts)
        {
            var belt = b.Belt!;
            var ahead = Directions.FromRotation(b.Rotation);
            var cell = new Vector2((b.X + 0.5f) * px, (b.Y + 0.5f) * px);
            var step = new Vector2(ahead.Dx(), ahead.Dy()) * px;
            for (int k = 0; k < belt.Count; k++)
            {
                string item = belt.Items[k]!;
                var at = cell + step * ((float)belt.Positions[k] / BeltState.CellLength - 0.5f);
                if (SpriteLayer(_itemSprites, item) is { } layer)
                {
                    int i = _itemCounts.GetValueOrDefault(item);
                    _itemCounts[item] = i + 1;
                    // Negative y scale: QuadMesh UVs are laid out for 3D (see Rect).
                    layer.Multimesh.SetInstanceTransform2D(i, new Transform2D(0f, new Vector2(spriteSize, -spriteSize), 0f, at));
                    layer.Multimesh.SetInstanceColor(i, Colors.White);
                }
                else
                {
                    _items.Multimesh.SetInstanceTransform2D(iPlain, new Transform2D(0f, new Vector2(size, size), 0f, at));
                    _items.Multimesh.SetInstanceColor(iPlain++, Palette.Deposit(item));
                }
            }
        }
    }

    static void Reserve(MultiMesh mm, int count)
    {
        if (count > mm.InstanceCount)
            mm.InstanceCount = Mathf.Max(count, mm.InstanceCount * 2);
        mm.VisibleInstanceCount = count;
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

    /// <summary>
    /// One pixel per cell of the bounding box, in its terrain's colour, scaled up to cells. Cells of other tiles are
    /// dimmed, so the tile's shape stands out against the land around it.
    /// </summary>
    void DrawTerrain(TileState tile)
    {
        var g = tile.Grid;
        var image = Image.CreateEmpty(g.Width, g.Height, false, Image.Format.Rgb8);
        for (int y = 0; y < g.Height; y++)
        {
            for (int x = 0; x < g.Width; x++)
            {
                var c = Palette.Terrain(g.TerrainAt(x, y));
                if (((x + y) & 1) == 0)
                    c = c.Darkened(0.04f);
                image.SetPixel(x, y, g.InBounds(x, y) ? c : c.Lerp(Colors.Black, Palette.Beyond.A));
            }
        }
        _terrain.Texture = ImageTexture.CreateFromImage(image);
    }

    /// <summary>The tile's border, and each I/O zone outlined in its neighbour's colour.</summary>
    void DrawOutline()
    {
        if (_drawnTileId < 0)
            return;
        var tile = _host.Sim.Tiles[_drawnTileId];
        float px = Palette.CellPx;
        foreach (var (zone, lines) in TileShapes.ZoneOutlines(tile))
            _outline.DrawMultiline(TileShapes.ToPixels(lines, px), Palette.Zone(zone) with { A = 0.45f }, 1);
        _outline.DrawMultiline(TileShapes.ToPixels(TileShapes.Outline(tile), px), Palette.TileBorder, 2);
        // The victory circle (WM-014): a building with any cell in it counts toward claiming the tile.
        var centre = new Vector2(tile.CircleX + 0.5f, tile.CircleY + 0.5f) * px;
        _outline.DrawArc(centre, (Sim.Balance.CircleRadius + 0.5f) * px, 0, Mathf.Tau, 96, Palette.Circle, 3);
    }

    /// <summary>Floating platforms (FB-017): a deck over the water that buildings stand on.</summary>
    void DrawPlatforms(TileGrid grid)
    {
        var cells = new List<(int X, int Y)>();
        for (int y = 0; grid.PlatformCount > 0 && y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                if (grid.HasPlatform(x, y))
                    cells.Add((x, y));
            }
        }
        // With a sprite the deck tiles edge to edge, like frames.
        bool sprite = _platforms.Texture != null;
        var mm = _platforms.Multimesh;
        mm.InstanceCount = cells.Count;
        for (int i = 0; i < cells.Count; i++)
        {
            mm.SetInstanceTransform2D(i, Rect(cells[i].X, cells[i].Y, 1, 1, sprite ? 0 : 0.03f, 0));
            mm.SetInstanceColor(i, sprite ? Colors.White : Palette.Platform);
        }
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

        // No ground sprite: the terrain colours are the ground.
        mm.InstanceCount = 0;
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

    /// <summary>Unit frames: a floor under the buildings, so what stands on it still shows.</summary>
    void DrawFrames(TileGrid grid)
    {
        var cells = new List<(int X, int Y)>();
        for (int y = 0; grid.FrameCount > 0 && y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                if (grid.HasFrame(x, y))
                    cells.Add((x, y));
            }
        }
        // With a sprite the frame tiles edge to edge; the flat fallback is inset so cells read apart.
        bool sprite = _frames.Texture != null;
        var mm = _frames.Multimesh;
        mm.InstanceCount = cells.Count;
        for (int i = 0; i < cells.Count; i++)
        {
            mm.SetInstanceTransform2D(i, Rect(cells[i].X, cells[i].Y, 1, 1, sprite ? 0 : 0.06f, 0));
            mm.SetInstanceColor(i, sprite ? Colors.White : Palette.Frame);
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

    /// <summary>The tile-local cell under a point of the tile view, in its canvas coordinates.</summary>
    public static Vector2I CellAt(Vector2 world) =>
        new(Mathf.FloorToInt(world.X / Palette.CellPx), Mathf.FloorToInt(world.Y / Palette.CellPx));

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
