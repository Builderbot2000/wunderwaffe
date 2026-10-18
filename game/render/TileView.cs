using System.Collections.Generic;
using Godot;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Render;

/// <summary>
/// Draws the viewed tile's ground, cell layers (FB-022: deposits, platforms, frame, cables, in layers.json order) and
/// buildings with one MultiMesh per layer, and per sprite within a layer, so draw cost stays flat as the building count
/// grows (NF-001). Content without a sprite is drawn as a flat <see cref="Palette"/> colour. Rebuilds only when the
/// grid's revision changes.
/// The ground is the tile's terrain, one colour per cell. The tile's bounding box also shows the land of the tiles
/// around it, dimmed, with the border drawn over it and each I/O zone outlined in its own colour.
/// <para>
/// Near the zoom threshold (WM-007) it fades over the world map at <see cref="SimHost.TileOpacity"/>; on the world map
/// side it shows <see cref="SimHost.FadeTile"/>, scaled into the world map's canvas so both pictures line up.
/// </para>
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
    MultiMeshInstance2D _buildings = null!;
    Node2D _buildingSprites = null!;

    // One group per cell layer, bottom to top: a flat-colour MultiMesh, then one per sprite of its entries.
    readonly List<Node2D> _layerGroups = [];
    readonly List<MultiMeshInstance2D> _layerPlain = [];
    MultiMeshInstance2D _items = null!;
    Node2D _itemSprites = null!;
    readonly Dictionary<string, int> _itemCounts = [];
    readonly Dictionary<string, MultiMeshInstance2D> _spriteLayers = [];
    int _drawnRevision = -1;
    int _drawnPatches = -1;
    int _drawnTileId = -1;

    public override void _Ready()
    {
        _host = SimHost.Instance;
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
        foreach (var layer in _host.Sim.Content.Layers.All)
        {
            var group = AddGroup($"Layer_{layer.Id}");
            _layerGroups.Add(group);
            _layerPlain.Add(AddLayer(group, "Plain", null));
        }
        _buildings = AddLayer(this, "Buildings", null);
        _buildingSprites = AddGroup("BuildingSprites");
        _items = AddLayer(this, "Items", null);
        _itemSprites = AddGroup("ItemSprites");
        _outline = AddGroup("Outline");
        _outline.Draw += DrawOutline;
    }

    public override void _Process(double delta)
    {
        bool onTile = _host.View == ViewMode.Tile;
        var shown = onTile ? _host.ViewedTile : _host.FadeTile;
        Visible = shown != null && _host.TileOpacity > 0;
        if (shown is not { } tile || !Visible)
            return;
        Modulate = Colors.White with { A = _host.TileOpacity };
        // The tile layer's canvas is the tile's local cells; on the world map, map that into world-map pixels.
        const float toMap = WorldMapView.CellPx / Palette.CellPx;
        Scale = onTile ? Vector2.One : Vector2.One * toMap;
        Position = onTile ? Vector2.Zero : new Vector2(tile.OriginX, tile.OriginY) * WorldMapView.CellPx;
        TileGrid grid = tile.Grid;
        // Grids of different tiles can share a revision number, so the tile id is part of "what's drawn".
        // Patches redraw only when one crosses a density threshold or runs out (RS-004), never per extraction.
        if (grid.Revision != _drawnRevision || tile.Id != _drawnTileId || grid.Patches.Revision != _drawnPatches)
        {
            _drawnPatches = grid.Patches.Revision;
            if (tile.Id != _drawnTileId)
            {
                DrawTerrain(tile);
                _outline.QueueRedraw();
            }
            _drawnRevision = grid.Revision;
            _drawnTileId = tile.Id;
            DrawGround(grid);
            for (int l = 0; l < grid.Layers.Count; l++)
                DrawLayer(grid, grid.Layers[l]);
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

    /// <summary>
    /// One cell layer (FB-022), entry by entry: its sprite if it has one, else its <see cref="Palette"/> colour. The
    /// layer's <c>draw</c> style says how: <c>tiled</c> edge to edge (the flat fallback inset so cells read apart),
    /// <c>icon</c> an item sprite on the ground (flat: the whole cell), <c>links</c> a dot per cell and a bar to each
    /// neighbour east or south on the same layer.
    /// </summary>
    void DrawLayer(TileGrid grid, Layer layer)
    {
        var group = _layerGroups[layer.Index];
        ClearLayers(group);
        if (grid.CountOn(layer.Index) == 0)
            return;
        string style = layer.Def.Draw;
        // Keyed by entry, plus (deposits, RS-004) the patch's density level times 256: cells in no patch draw full.
        var cells = new Dictionary<int, List<(int X, int Y)>>();
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                int e = grid.EntryAt(layer.Index, x, y);
                if (e == 0)
                    continue;
                if (layer.HoldsItems)
                    e |= (grid.Patches.At(x, y) is { } patch ? patch.Level : MaxLevel(layer.Ids[e])) << 8;
                if (!cells.TryGetValue(e, out var list))
                    cells[e] = list = [];
                list.Add((x, y));
            }
        }

        const float Thick = 0.22f;
        float px = Palette.CellPx;
        var plain = new List<(Transform2D At, Color Colour)>();
        var sprites = new Dictionary<MultiMeshInstance2D, List<(Transform2D At, Color Colour)>>();
        foreach (var (key, list) in cells)
        {
            int e = key & 0xFF, level = key >> 8;
            string id = layer.Ids[e];
            var colour = layer.HoldsItems ? Palette.Deposit(id) : Palette.Building(id);
            if (layer.HoldsItems)
            {
                // A sprite per density level ({item}_{level}); without one, the item's sprite or colour fades as it thins.
                int max = MaxLevel(id);
                var tint = Colors.White with { A = max == 0 ? 1 : 0.35f + 0.65f * level / max };
                var art = SpriteLayer(group, $"{id}_{level}") is { } levelled ? (levelled, Colors.White) : (SpriteLayer(group, id), tint);
                if (art.Item1 is { } layerOf)
                {
                    if (!sprites.TryGetValue(layerOf, out var at))
                        sprites[layerOf] = at = [];
                    foreach (var (x, y) in list)
                        at.Add((Rect(x, y, 1, 1, 0.25f, 0), art.Item2));
                    continue;
                }
                foreach (var (x, y) in list)
                    plain.Add((Rect(x, y, 1, 1, 0, 0), colour with { A = colour.A * tint.A }));
                continue;
            }
            if (style == "links")
            {
                foreach (var (x, y) in list)
                {
                    var centre = new Vector2(x + 0.5f, y + 0.5f) * px;
                    plain.Add((new Transform2D(0, new Vector2(Thick * 1.6f * px, -Thick * 1.6f * px), 0, centre), colour));
                    if (grid.Has(layer.Index, x + 1, y))
                        plain.Add((new Transform2D(0, new Vector2(px, -Thick * px), 0, centre + new Vector2(px / 2, 0)), colour));
                    if (grid.Has(layer.Index, x, y + 1))
                        plain.Add((new Transform2D(0, new Vector2(Thick * px, -px), 0, centre + new Vector2(0, px / 2)), colour));
                }
                continue;
            }
            if (SpriteLayer(group, id) is { } sprite)
            {
                float inset = style == "icon" ? 0.25f : 0;
                var smm = sprite.Multimesh;
                smm.InstanceCount = list.Count;
                for (int i = 0; i < list.Count; i++)
                {
                    smm.SetInstanceTransform2D(i, Rect(list[i].X, list[i].Y, 1, 1, inset, 0));
                    smm.SetInstanceColor(i, Colors.White);
                }
                continue;
            }
            float flatInset = style == "icon" ? 0 : 0.06f;
            foreach (var (x, y) in list)
                plain.Add((Rect(x, y, 1, 1, flatInset, 0), colour));
        }

        foreach (var (sprite, at) in sprites)
            Fill(sprite.Multimesh, at);
        Fill(_layerPlain[layer.Index].Multimesh, plain);
    }

    static void Fill(MultiMesh mm, List<(Transform2D At, Color Colour)> instances)
    {
        mm.InstanceCount = instances.Count;
        for (int i = 0; i < instances.Count; i++)
        {
            mm.SetInstanceTransform2D(i, instances[i].At);
            mm.SetInstanceColor(i, instances[i].Colour);
        }
    }

    /// <summary>Densest level a deposit of the item is drawn at (RS-004); 0 if it never runs out.</summary>
    int MaxLevel(string item) => _host.Sim.Content.Items[item].Deposit?.MaxLevel ?? 0;

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
