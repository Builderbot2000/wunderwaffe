using System.Collections.Generic;
using System.Linq;
using Godot;
using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Game.Render;

/// <summary>
/// Placeholder colours until there is art. Hues are spread by golden-ratio steps over the sorted ids,
/// so new content gets a distinct colour automatically.
/// </summary>
public static class Palette
{
    public const int CellPx = 16;

    public static readonly Color Ground = new(0.16f, 0.17f, 0.19f);
    public static readonly Color Frame = new(0.35f, 0.75f, 0.95f, 0.35f);
    public static readonly Color GhostOk = new(0.3f, 0.9f, 0.4f, 0.45f);
    public static readonly Color GhostBad = new(0.95f, 0.25f, 0.25f, 0.45f);
    static readonly Color Core = new(0.95f, 0.8f, 0.3f);

    /// <summary>Outline of a tile's border on its local map, and the dimming over land beyond it.</summary>
    public static readonly Color TileBorder = new(1f, 0.95f, 0.75f, 0.9f);
    public static readonly Color Beyond = new(0f, 0f, 0f, 0.55f);

    static readonly Dictionary<string, Color> Deposits = [];
    static readonly Dictionary<string, Color> Buildings = [];
    static Color[] _terrain = [];

    public static void Init(ContentDb content)
    {
        Spread(content.Items.Keys, Deposits, saturation: 0.55f, value: 0.5f);
        Spread(content.Buildings.Keys, Buildings, saturation: 0.45f, value: 0.9f);
        _terrain = content.TerrainList.Select(t => Color.FromHtml(t.Color)).ToArray();
    }

    /// <summary>A terrain's colour from <c>terrain.json</c>, by index.</summary>
    public static Color Terrain(int index) => index >= 0 && index < _terrain.Length ? _terrain[index] : Colors.Magenta;

    /// <summary>A neighbouring tile's I/O zone colour: hues spread by its id so adjacent zones differ.</summary>
    public static Color Zone(int neighborId) => Color.FromHsv((0.12f + neighborId * 0.618034f) % 1f, 0.7f, 1f);

    /// <summary>A unit's body colour: its owner's hue, brighter than the world map's tile colour.</summary>
    public static Color Unit(int owner) => Color.FromHsv((0.45f + owner * 0.618034f) % 1f, 0.6f, 1f);

    public static Color Deposit(string itemId) => Deposits.GetValueOrDefault(itemId, Colors.Magenta);

    public static Color Building(string buildingId) =>
        buildingId == Sim.Simulation.CoreBuildingId ? Core : Buildings.GetValueOrDefault(buildingId, Colors.Magenta);

    static void Spread(IEnumerable<string> ids, Dictionary<string, Color> into, float saturation, float value)
    {
        into.Clear();
        int i = 0;
        foreach (var id in ids.Order(System.StringComparer.Ordinal))
            into[id] = Color.FromHsv(i++ * 0.618034f % 1f, saturation, value);
    }
}
