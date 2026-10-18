using System.Collections.Generic;
using Godot;
using Wunderwaffe.Sim;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Render;

/// <summary>
/// The world map's picture of every tile's local map (WM-007): each world cell coloured by what is on it (a building,
/// frame, deposit, or the terrain shaded by its slope), then shrunk by <see cref="Downsample"/> and drawn with linear
/// filtering and mipmaps, so it reads as a soft, zoomed-out view. A tile's part of the picture only changes when
/// <see cref="Refresh"/> repaints it; the world map does that every few view switches, so the map is a slightly
/// stale overview rather than a live one.
/// </summary>
public sealed class WorldSnapshot
{
    /// <summary>World cells per picture pixel, each way.</summary>
    public const int Downsample = 2;

    const float DepositMix = 0.65f;
    const float FrameMix = 0.5f;

    readonly WorldMap _map;
    readonly byte[] _ground;
    readonly byte[] _cells;
    readonly byte[] _pixels;
    readonly int _pw, _ph;
    readonly Dictionary<int, int> _painted = [];
    Image _image;

    public Simulation Sim { get; }
    public ImageTexture Texture { get; }

    public WorldSnapshot(Simulation sim)
    {
        Sim = sim;
        _map = sim.Map;
        int w = _map.Width, h = _map.Height;
        _ground = new byte[w * h * 3];
        _cells = new byte[w * h * 3];
        _pw = (w + Downsample - 1) / Downsample;
        _ph = (h + Downsample - 1) / Downsample;
        _pixels = new byte[_pw * _ph * 3];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
                Put(_ground, y * w + x, Shaded(x, y));
        }
        System.Array.Copy(_ground, _cells, _ground.Length);
        foreach (var tile in sim.Tiles)
            PaintTile(tile);
        Shrink(0, 0, w, h);
        _image = Upload();
        Texture = ImageTexture.CreateFromImage(_image);
    }

    /// <summary>Repaints the tiles that changed since they were last painted. Returns how many there were.</summary>
    public int Refresh()
    {
        int n = 0;
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = 0, y1 = 0;
        foreach (var tile in Sim.Tiles)
        {
            if (_painted.GetValueOrDefault(tile.Id, -1) == tile.Grid.Revision)
                continue;
            PaintTile(tile);
            x0 = Mathf.Min(x0, tile.OriginX);
            y0 = Mathf.Min(y0, tile.OriginY);
            x1 = Mathf.Max(x1, tile.OriginX + tile.Grid.Width);
            y1 = Mathf.Max(y1, tile.OriginY + tile.Grid.Height);
            n++;
        }
        if (n == 0)
            return 0;
        Shrink(x0, y0, x1, y1);
        _image = Upload();
        Texture.Update(_image);
        return n;
    }

    /// <summary>Terrain colour, lit from the top-left so slopes read as relief. Water is left flat.</summary>
    Color Shaded(int x, int y)
    {
        var c = Palette.Terrain(_map.TerrainAt(x, y));
        if (Sim.Content.TerrainList[_map.TerrainAt(x, y)].Water)
            return c;
        int slope = _map.ElevationAt(Mathf.Max(0, x - 1), Mathf.Max(0, y - 1))
            - _map.ElevationAt(Mathf.Min(_map.Width - 1, x + 1), Mathf.Min(_map.Height - 1, y + 1));
        float light = Mathf.Clamp(slope / 40f, -0.3f, 0.3f);
        return light >= 0 ? c.Lightened(light) : c.Darkened(-light);
    }

    void PaintTile(TileState tile)
    {
        var g = tile.Grid;
        int w = _map.Width;
        for (int y = 0; y < g.Height; y++)
        {
            for (int x = 0; x < g.Width; x++)
            {
                if (!g.InBounds(x, y))
                    continue;
                int i = (tile.OriginY + y) * w + tile.OriginX + x;
                Color c;
                if (g.BuildingAt(x, y) is { } b)
                    c = Palette.Building(b.DefId);
                else
                {
                    c = new Color(_ground[i * 3] / 255f, _ground[i * 3 + 1] / 255f, _ground[i * 3 + 2] / 255f);
                    if (g.DepositAt(x, y) is { } item)
                        c = c.Lerp(Palette.Deposit(item), DepositMix);
                    if (g.Has(BuildingKind.Frame, x, y))
                        c = c.Lerp(Palette.Frame with { A = 1 }, FrameMix);
                }
                Put(_cells, i, c);
            }
        }
        _painted[tile.Id] = g.Revision;
    }

    static void Put(byte[] into, int i, Color c)
    {
        into[i * 3] = (byte)(Mathf.Clamp(c.R, 0, 1) * 255);
        into[i * 3 + 1] = (byte)(Mathf.Clamp(c.G, 0, 1) * 255);
        into[i * 3 + 2] = (byte)(Mathf.Clamp(c.B, 0, 1) * 255);
    }

    /// <summary>Averages each block of cells in the given world rectangle into its picture pixel.</summary>
    void Shrink(int x0, int y0, int x1, int y1)
    {
        int w = _map.Width, h = _map.Height;
        for (int py = y0 / Downsample; py < Mathf.Min(_ph, (y1 + Downsample - 1) / Downsample); py++)
        {
            for (int px = x0 / Downsample; px < Mathf.Min(_pw, (x1 + Downsample - 1) / Downsample); px++)
            {
                int r = 0, gr = 0, b = 0, n = 0;
                for (int y = py * Downsample; y < Mathf.Min(h, (py + 1) * Downsample); y++)
                {
                    for (int x = px * Downsample; x < Mathf.Min(w, (px + 1) * Downsample); x++)
                    {
                        int i = (y * w + x) * 3;
                        r += _cells[i];
                        gr += _cells[i + 1];
                        b += _cells[i + 2];
                        n++;
                    }
                }
                int o = (py * _pw + px) * 3;
                _pixels[o] = (byte)(r / n);
                _pixels[o + 1] = (byte)(gr / n);
                _pixels[o + 2] = (byte)(b / n);
            }
        }
    }

    Image Upload()
    {
        var image = Image.CreateFromData(_pw, _ph, false, Image.Format.Rgb8, _pixels);
        image.GenerateMipmaps();
        return image;
    }
}
