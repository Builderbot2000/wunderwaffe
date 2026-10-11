namespace Wunderwaffe.Sim;

/// <summary>
/// Economy and world-setup tuning that isn't in the JSON content yet. Placeholders until the open questions they
/// belong to are decided; change them here rather than where they are used.
/// </summary>
public static class Balance
{
    /// <summary>What a new player's core starts with (WM-005).</summary>
    public static readonly IReadOnlyDictionary<string, int> StartingItems = Items(("copper_ore", 300), ("iron_ore", 150));

    /// <summary>Radius of every tile's victory circle, in cells (WM-014): 1/8 of a nominal tile until playtested.</summary>
    public const int CircleRadius = 12;

    /// <summary>Cells kept between a victory circle and its tile's border where the tile has room (WM-014).</summary>
    public const int CircleClearance = 4;

    /// <summary>
    /// Seconds a player must keep a building in a circle to claim its tile (WM-006), when the match setup doesn't say.
    /// </summary>
    public const int CaptureSeconds = 30;

    /// <summary>Fair-start attempts tried by world generation; the fairest is kept (WM-005, OQ-22).</summary>
    public const int StartAttempts = 8;

    /// <summary>A start should have a regional deposit within this many tile steps (OQ-22).</summary>
    public const int RegionalHops = 2;

    /// <summary>What a unit frame cell costs, paid from the tile inventory and refunded on removal.</summary>
    public static readonly IReadOnlyDictionary<string, int> FrameCost = Items(("iron_ore", 2));

    /// <summary>Share of a building's cost returned on removal (FB-002).</summary>
    public const int RefundPercent = 100;

    /// <summary>Chance, in percent, that a tile other than a starting tile has a given regional deposit (RS-003).</summary>
    public const int RegionalChancePercent = 35;

    /// <summary>A generated starting tile has at least this share of buildable ground, in percent.</summary>
    public const int StartLandPercent = 60;

    /// <summary>A neighbour counts as land, for the start and for regional deposits, with this share of buildable ground.</summary>
    public const int LandPercent = 30;

    static IReadOnlyDictionary<string, int> Items(params (string Id, int N)[] items) =>
        new SortedDictionary<string, int>(items.ToDictionary(i => i.Id, i => i.N), StringComparer.Ordinal);
}
