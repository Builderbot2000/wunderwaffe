namespace Wunderwaffe.Sim;

/// <summary>
/// Economy and world-setup tuning that isn't in the JSON content yet. Placeholders until the open questions they
/// belong to are decided; change them here rather than where they are used.
/// </summary>
public static class Balance
{
    /// <summary>What a new player's core starts with (WM-005).</summary>
    public static readonly IReadOnlyDictionary<string, int> StartingItems = Items(("copper_ore", 300), ("iron_ore", 150));

    /// <summary>Paid from a neighbouring tile the claimer owns to claim a tile (WM-006, OQ-3).</summary>
    public static readonly IReadOnlyDictionary<string, int> ClaimCost = Items(("copper_ore", 100), ("iron_ore", 50));

    /// <summary>What a newly claimed tile's core starts with: enough for a few drills, a belt and an export port.</summary>
    public static readonly IReadOnlyDictionary<string, int> ClaimedTileItems = Items(("copper_ore", 120));

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
