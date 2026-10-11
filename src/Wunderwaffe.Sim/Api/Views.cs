using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Api;

// Plain read-only records returned by GameQuery (NF-009). They copy what they show, so they serialise to JSON as is
// and stay valid after the sim moves on. Cell coordinates are tile-local unless a name says otherwise.

/// <summary>The match as a whole.</summary>
/// <param name="TilesToWin">Tiles one player must own to win by domination (MP-001).</param>
/// <param name="Winner">The winner, or -1 (no winner yet, or a draw once <paramref name="Over"/>).</param>
public sealed record MatchView(
    long Tick, int TicksPerSecond, GameSetup? Setup, int TileCount, int TilesToWin,
    bool Over, int Winner, VictoryReason EndReason, long EndTick);

/// <summary>One player's standing.</summary>
/// <param name="Eliminated">True once they lost their last core.</param>
public sealed record PlayerView(
    int Id, bool Ai, bool Eliminated, int Tiles, int Units, int GoalsDone, long Shipped, long Launched,
    IReadOnlyDictionary<string, long> Produced);

/// <summary>A tile as the world map shows it.</summary>
/// <param name="Owner">Player id, or <see cref="TileState.Unowned"/>.</param>
/// <param name="CenterX">World cell of the tile's centre of area.</param>
public sealed record TileSummary(
    int Id, string Name, int Owner, string Biome, int CenterX, int CenterY, IReadOnlyList<int> Neighbors, int Buildings);

/// <summary>Everything the world-map side panel shows about one tile.</summary>
/// <param name="OriginX">World cell of local (0, 0); the tile's box is <paramref name="Width"/> x <paramref name="Height"/>.</param>
/// <param name="Area">Cells that belong to the tile (the rest of its box belongs to neighbours).</param>
/// <param name="Terrain">Share of each terrain id, in percent, largest first; terrains under 5% left out.</param>
/// <param name="Deposits">Deposit cells per item.</param>
/// <param name="MissingRegional">Regional items with no deposit here (RS-003).</param>
/// <param name="Ports">Per neighbour with ports: (neighbour id, exports, imports).</param>
/// <param name="Claim">What claiming it would do for the asking player.</param>
/// <param name="ClaimPayer">The tile that would pay for the claim, or -1.</param>
public sealed record TileInfo(
    int Id, string Name, int Owner, string Biome, int OriginX, int OriginY, int Width, int Height,
    int Area, int Buildable, IReadOnlyList<int> Neighbors, IReadOnlyList<TerrainShare> Terrain,
    IReadOnlyDictionary<string, int> Deposits, IReadOnlyList<string> MissingRegional,
    IReadOnlyDictionary<string, long> Inventory, int Buildings, IReadOnlyList<PortLink> Ports,
    ClaimResult Claim, int ClaimPayer);

/// <summary>How much of a tile is one terrain, in percent.</summary>
public sealed record TerrainShare(string Terrain, int Percent);

/// <summary>Port counts in a tile's zone facing <paramref name="Neighbor"/>.</summary>
public sealed record PortLink(int Neighbor, int Exports, int Imports);

/// <summary>One cell of a tile.</summary>
/// <param name="InTile">False for cells of the box that belong to another tile.</param>
/// <param name="Zone">Neighbour id of the I/O zone the cell is in, or -1.</param>
/// <param name="Building">Id of the building on it, or -1.</param>
public sealed record CellView(int X, int Y, bool InTile, bool Buildable, string Terrain, string? Deposit, int Zone, int Building, bool Frame);

/// <summary>A building and what it is doing.</summary>
/// <param name="Status">Machines and import ports only.</param>
/// <param name="MissingInput">What a starved machine is waiting for.</param>
/// <param name="Partner">Ports only: the neighbouring tile their zone links to.</param>
/// <param name="Holding">Items in its buffers or on its belt.</param>
public sealed record BuildingView(
    int Id, string DefId, int Owner, int X, int Y, int Width, int Height, int Rotation, string? RecipeId, int Hp, int MaxHp,
    MachineStatus? Status, string? MissingInput, int Partner, IReadOnlyDictionary<string, int> Holding);

/// <summary>A unit.</summary>
/// <param name="CellX">World cell under the unit's centre of mass.</param>
/// <param name="DestTileId">Where it is headed, or -1 when not moving.</param>
public sealed record UnitInfo(
    int Id, int Owner, int TileId, int CellX, int CellY, int Blocks, int Hp, int MaxHp, int Mass, int MaxSpeed,
    bool Moving, int DestTileId, int DestX, int DestY);

/// <summary>The goal a player is working on.</summary>
/// <param name="Index">Its place in the chain, from 0; <paramref name="Count"/> goals in all.</param>
public sealed record GoalView(int Index, int Count, string Id, string Name, string Description, string Type, string? Item, long Progress, long Amount);

/// <summary>Where a building could go: a legal placement found by <see cref="Planner"/>.</summary>
public sealed record Spot(int X, int Y, int Rotation, string? RecipeId);

/// <summary>One conveyor of a planned belt: its cell and the way it faces.</summary>
public sealed record BeltStep(int X, int Y, int Rotation);

/// <summary>A deposit cell.</summary>
public sealed record DepositCell(int X, int Y, string Item);
