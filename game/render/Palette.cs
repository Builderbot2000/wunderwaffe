using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Game.Render;

/// <summary>Colour sets for the HUD and player colours (UI-005).</summary>
public enum ColourMode
{
    Normal,

    /// <summary>Red-green safe (deuteranopia and protanopia): blues against oranges, after Okabe and Ito.</summary>
    RedGreen,

    /// <summary>Blue-yellow safe (tritanopia): reds and pinks against teals.</summary>
    BlueYellow,
}

/// <summary>
/// Placeholder colours until there is art. Hues are spread by golden-ratio steps over the sorted ids,
/// so new content gets a distinct colour automatically. Player colours and good/bad signals follow
/// <see cref="Mode"/>, so colour-blind players can tell sides and states apart.
/// </summary>
public static class Palette
{
    /// <summary>The colour set in use; set from the settings.</summary>
    public static ColourMode Mode { get; set; }

    static readonly Color[] RedGreenPlayers =
    [
        new(0.0f, 0.45f, 0.70f), new(0.90f, 0.62f, 0.0f), new(0.34f, 0.71f, 0.91f), new(0.94f, 0.89f, 0.26f),
        new(0.80f, 0.47f, 0.65f), new(0.84f, 0.37f, 0.0f), new(0.95f, 0.95f, 0.95f), new(0.5f, 0.5f, 0.5f),
    ];

    static readonly Color[] BlueYellowPlayers =
    [
        new(0.85f, 0.11f, 0.38f), new(0.0f, 0.62f, 0.60f), new(0.98f, 0.55f, 0.55f), new(0.0f, 0.36f, 0.36f),
        new(0.55f, 0.0f, 0.15f), new(0.55f, 0.88f, 0.86f), new(0.95f, 0.95f, 0.95f), new(0.5f, 0.5f, 0.5f),
    ];

    /// <summary>"OK" and "not OK" in the current mode: green/red normally, swapped for pairs the mode can tell apart.</summary>
    public static Color Good => Mode switch
    {
        ColourMode.RedGreen => new(0.34f, 0.71f, 0.91f),
        ColourMode.BlueYellow => new(0.2f, 0.8f, 0.78f),
        _ => new(0.45f, 0.9f, 0.5f),
    };

    public static Color Bad => Mode switch
    {
        ColourMode.RedGreen => new(0.9f, 0.45f, 0.0f),
        ColourMode.BlueYellow => new(0.95f, 0.25f, 0.5f),
        _ => new(1f, 0.42f, 0.38f),
    };

    public const int CellPx = 16;

    public static readonly Color Ground = new(0.16f, 0.17f, 0.19f);
    public static readonly Color Frame = new(0.35f, 0.75f, 0.95f, 0.35f);

    /// <summary>Floating platform deck (FB-017).</summary>
    public static readonly Color Platform = new(0.55f, 0.42f, 0.28f);

    /// <summary>Power cables (FB-020) and a power node's reach while placing it.</summary>
    public static readonly Color Cable = new(0.95f, 0.7f, 0.25f);

    /// <summary>A tile's victory circle on the tile view (WM-014).</summary>
    public static readonly Color Circle = new(1f, 0.9f, 0.4f, 0.55f);

    public static Color GhostOk => Good with { A = 0.45f };
    public static Color GhostBad => Bad with { A = 0.45f };
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
        // Kinds with a colour of their own, whatever their id.
        foreach (var b in content.BuildingList)
        {
            if (b.Kind switch { BuildingKind.Core => Core, BuildingKind.Frame => Frame, BuildingKind.Platform => Platform, BuildingKind.Wire => Cable, _ => (Color?)null } is { } own)
                Buildings[b.Id] = own;
        }
        _terrain = content.TerrainList.Select(t => Color.FromHtml(t.Color)).ToArray();
    }

    /// <summary>A terrain's colour from <c>terrain.json</c>, by index.</summary>
    public static Color Terrain(int index) => index >= 0 && index < _terrain.Length ? _terrain[index] : Colors.Magenta;

    /// <summary>A neighbouring tile's I/O zone colour: hues spread by its id so adjacent zones differ.</summary>
    public static Color Zone(int neighborId) => Color.FromHsv((0.12f + neighborId * 0.618034f) % 1f, 0.7f, 1f);

    /// <summary>A player's colour, hue spread by id: full brightness for units, darker for the world map's tile tint.</summary>
    public static Color Owner(int owner, float value = 1f)
    {
        var table = Mode switch { ColourMode.RedGreen => RedGreenPlayers, ColourMode.BlueYellow => BlueYellowPlayers, _ => null };
        if (table == null)
            return Color.FromHsv((0.45f + owner * 0.618034f) % 1f, 0.6f, value);
        var c = table[Math.Abs(owner) % table.Length];
        return new Color(c.R * value, c.G * value, c.B * value);
    }

    /// <summary>
    /// Unclaimed buildings (WM-019): a dull grey-violet no player colour uses; placeholder blocks are drawn in it and
    /// sprites tinted with it, in every <see cref="Mode"/>.
    /// </summary>
    public static readonly Color Unclaimed = new(0.52f, 0.48f, 0.58f);

    public static Color Deposit(string itemId) => Deposits.GetValueOrDefault(itemId, Colors.Magenta);

    public static Color Building(string buildingId) => Buildings.GetValueOrDefault(buildingId, Colors.Magenta);

    static void Spread(IEnumerable<string> ids, Dictionary<string, Color> into, float saturation, float value)
    {
        into.Clear();
        int i = 0;
        foreach (var id in ids.Order(System.StringComparer.Ordinal))
            into[id] = Color.FromHsv(i++ * 0.618034f % 1f, saturation, value);
    }
}
