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
/// <param name="CircleX">Local cell of the victory circle's centre (WM-014); a building of yours in it claims the tile.</param>
/// <param name="Inventory">The asking player's build funds here: what their cores on the tile hold (FB-019).</param>
/// <param name="Presence">Whether the asking player may build or make frames here (WM-015).</param>
/// <param name="ClaimTicks">The asking player's claim progress, 0 to <paramref name="CaptureTicks"/> (WM-006).</param>
/// <param name="Contested">Whether more than one player has buildings or units here (WM-004).</param>
public sealed record TileInfo(
    int Id, string Name, int Owner, string Biome, int OriginX, int OriginY, int Width, int Height,
    int Area, int Buildable, IReadOnlyList<int> Neighbors, IReadOnlyList<TerrainShare> Terrain,
    IReadOnlyDictionary<string, int> Deposits, IReadOnlyList<string> MissingRegional,
    IReadOnlyDictionary<string, long> Inventory, int Buildings, IReadOnlyList<PortLink> Ports,
    int CircleX, int CircleY, bool Presence, int ClaimTicks, int CaptureTicks, bool Contested);

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
/// <param name="Holding">Items in its buffers or on its belt, or a core's stock.</param>
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

/// <summary>How a unit would get somewhere (UC-009).</summary>
/// <param name="Tiles">Tiles it passes through after its own, ending with the destination.</param>
/// <param name="Length">Path length in sub-cells.</param>
/// <param name="Ticks">Estimated travel time, or -1 if it can't move.</param>
public sealed record TravelView(IReadOnlyList<int> Tiles, long Length, long Ticks);

/// <summary>A squad (UC-002); <paramref name="Members"/> in slot order.</summary>
public sealed record SquadView(
    int Id, int Owner, string Name, IReadOnlyList<int> Members, Formation Formation, Stance Stance, OrderKind? Order, int TargetId);

/// <summary>A unit factory's progress (UD-006).</summary>
/// <param name="Next">Index of the step being built, of <paramref name="Steps"/>.</param>
public sealed record FactoryView(
    int BuildingId, FactoryStatus Status, bool Projecting, int Next, int Steps, int Built, int Radius, int BlockCap,
    IReadOnlyList<StageProgressView> Stages, IReadOnlyDictionary<string, int> Buffer, IReadOnlyDictionary<string, int> Missing);

public sealed record StageProgressView(BuildStage Stage, int Done, int Total);

/// <summary>A power network's balance this tick (FB-005).</summary>
/// <param name="Satisfaction">Permille of demand met.</param>
public sealed record PowerView(int Id, IReadOnlyList<int> Members, long Supply, long Demand, long Stored, long Capacity, int Satisfaction);

/// <summary>A tile's production over the last minute (FB-011).</summary>
public sealed record ProductionView(IReadOnlyList<ItemRate> Items, IReadOnlyList<PartnerRate> Sent);

public sealed record ItemRate(string Item, long MadePerMinute, long UsedPerMinute);

public sealed record PartnerRate(int Partner, long SentPerMinute);

/// <summary>A unit's command state (silo E): <paramref name="Order"/> null when idle; <paramref name="SquadId"/> 0 and <paramref name="EscortId"/> -1 when none.</summary>
public sealed record UnitOrderView(
    int Id, OrderKind? Order, Stance Stance, int SquadId, int EscortId, LocomotionClass Locomotion, bool Unsupplied);

/// <summary>A design's locomotion (UD-004): <paramref name="Class"/> means something only when <paramref name="Result"/> is Ok.</summary>
public sealed record LocomotionView(LocomotionResult Result, LocomotionClass Class);

/// <summary>Per-10 s made and used counts over the last 10 minutes, oldest first (FB-011).</summary>
public sealed record HistoryView(long[] Made, long[] Used);
