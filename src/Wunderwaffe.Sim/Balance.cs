namespace Wunderwaffe.Sim;

/// <summary>
/// Economy and world-setup tuning that isn't in the JSON content yet. Placeholders until the open questions they
/// belong to are decided; change them here rather than where they are used.
/// </summary>
public static class Balance
{
    /// <summary>Radius of every tile's victory circle, in cells (WM-014): 1/8 of a nominal tile (OQ-35).</summary>
    public const int CircleRadius = Simulation.DefaultTileSize / 8;

    /// <summary>Cells kept between a victory circle and its tile's border where the tile has room (WM-014): 1/32 of a nominal tile, twice the port zone depth.</summary>
    public const int CircleClearance = Simulation.DefaultTileSize / 32;

    /// <summary>
    /// Seconds a player must keep a building in a circle to claim its tile (WM-006), when the match setup doesn't say.
    /// </summary>
    public const int CaptureSeconds = 30;

    /// <summary>Fair-start attempts tried by world generation; the fairest is kept (WM-005, OQ-22).</summary>
    public const int StartAttempts = 8;

    /// <summary>A start should have a regional deposit within this many tile steps (OQ-22).</summary>
    public const int RegionalHops = 2;

    /// <summary>Share of a building's cost returned on removal (FB-002).</summary>
    public const int RefundPercent = 100;

    /// <summary>Chance, in percent, that a tile other than a starting tile has a given regional deposit (RS-003).</summary>
    public const int RegionalChancePercent = 35;

    /// <summary>A generated starting tile has at least this share of buildable ground, in percent.</summary>
    public const int StartLandPercent = 60;

    /// <summary>A neighbour counts as land, for the start and for regional deposits, with this share of buildable ground.</summary>
    public const int LandPercent = 30;

    /// <summary>Share of a building's cost returned for deconstructing its wreck (CB-006).</summary>
    public const int WreckRefundPercent = 25;


    /// <summary>Ticks a unit factory spends placing a block, per item of the block's cost (UD-002, UD-006).</summary>
    public const int BuildTicksPerItem = 6;

    /// <summary>Units with more frame cells than this are Wunderwaffe class, for the UI and alerts (WW-001).</summary>
    public const int WunderwaffeCells = 48;

    /// <summary>Ticks between upkeep payments; upkeep is declared per minute (UD-013).</summary>
    public const int UpkeepPeriod = 60 * Simulation.TicksPerSecond;

    /// <summary>
    /// A flying unit's build radius in cells (WM-015, OQ-58): this base plus <see cref="VehicleRadiusPerBlock"/> per live
    /// block, at most <see cref="VehicleRadiusMax"/>. The three-block scout reaches 12 cells.
    /// </summary>
    public const int VehicleRadiusBase = 6;

    /// <inheritdoc cref="VehicleRadiusBase"/>
    public const int VehicleRadiusPerBlock = 2;

    /// <inheritdoc cref="VehicleRadiusBase"/>
    public const int VehicleRadiusMax = 32;

    /// <summary>
    /// Claim radius in cells (WM-019): placing a building or landing a unit with any cell within this distance
    /// (straight-line, nearest cells, as a build radius) of an unclaimed building claims it. Tuned in playtesting.
    /// </summary>
    public const int ClaimRadius = 8;
}
