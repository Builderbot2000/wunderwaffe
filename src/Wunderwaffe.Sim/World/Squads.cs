namespace Wunderwaffe.Sim.World;

/// <summary>How a squad's members arrange themselves around the point they are sent to (UC-003).</summary>
public enum Formation
{
    /// <summary>A grid of up to four columns; also what loose units use.</summary>
    Box,

    /// <summary>Side by side, across the direction of travel.</summary>
    Line,

    /// <summary>One behind the other.</summary>
    Column,

    /// <summary>A V with its point toward the goal.</summary>
    Wedge,

    /// <summary>Slots from <see cref="Squad.Custom"/>.</summary>
    Custom,
}

public enum SquadResult
{
    Ok,
    NoSuchSquad,
    NotOwner,

    /// <summary>None of the given units is the player's.</summary>
    NoUnits,

    /// <summary>Empty, or longer than <see cref="Squads.MaxNameLength"/>.</summary>
    BadName,

    /// <summary>A custom formation without slots, or with too many.</summary>
    BadFormation,
}

/// <summary>
/// A named group of units that share orders, formation and stance (UC-002). <see cref="Members"/> is in the order units
/// joined, which is also the order of formation slots. Sim state: hashed.
/// </summary>
public sealed class Squad(int id, int owner, string name)
{
    public int Id { get; } = id;
    public int Owner { get; } = owner;
    public string Name { get; internal set; } = name;

    internal List<int> MemberList { get; } = [];
    public IReadOnlyList<int> Members => MemberList;

    public Formation Formation { get; internal set; }

    /// <summary>
    /// Custom formation slots, in cells relative to the squad's centre: X forward along the direction of travel, Y to
    /// the right. Members past the last slot queue up behind.
    /// </summary>
    public (int X, int Y)[] Custom { get; internal set; } = [];

    public Stance Stance { get; internal set; }

    /// <summary>The order last given to the squad as a whole, so a formation change can re-form it (UC-003).</summary>
    public UnitOrder? Order { get; internal set; }

    /// <summary>The unit the squad focuses fire on, or -1. Set and read by combat (UC-002).</summary>
    public int TargetId { get; set; } = -1;
}

/// <summary>
/// Where a factory's new units go (UC-006): a rally point (tile -1 for none) and a squad to join (0 for none).
/// Keyed by the factory building.
/// </summary>
public sealed record Rally(int TileId, int BuildingId, int Owner, int RallyTileId, int RallyX, int RallyY, int SquadId);

/// <summary>Every squad and factory rally setting in the match, in id order.</summary>
public sealed class Squads
{
    public const int MaxNameLength = 24;
    public const int MaxCustomSlots = UnitAssembly.MaxCells;

    readonly SortedDictionary<int, Squad> _squads = [];
    readonly SortedDictionary<(int Tile, int Building), Rally> _rallies = [];
    int _nextId = 1;

    public IEnumerable<Squad> All => _squads.Values;
    public IEnumerable<Rally> Rallies => _rallies.Values;
    internal int NextId { get => _nextId; set => _nextId = value; }

    public Squad? ById(int id) => _squads.GetValueOrDefault(id);

    /// <summary>The squad a unit is in, or null.</summary>
    public Squad? Of(Unit u) => u.SquadId == 0 ? null : ById(u.SquadId);

    public IEnumerable<Squad> OwnedBy(int player) => _squads.Values.Where(s => s.Owner == player);

    /// <summary>The rally setting of a factory building, or null.</summary>
    public Rally? RallyOf(int tileId, int buildingId) => _rallies.GetValueOrDefault((tileId, buildingId));

    internal Squad Create(int owner, string name)
    {
        var s = new Squad(_nextId++, owner, name);
        _squads.Add(s.Id, s);
        return s;
    }

    internal void Remove(Squad s) => _squads.Remove(s.Id);

    /// <summary>Adds a squad under a given id, for snapshot restore (NF-008).</summary>
    internal Squad AddRestored(int id, int owner, string name)
    {
        var s = new Squad(id, owner, name);
        _squads.Add(id, s);
        return s;
    }

    internal void SetRally(Rally r) => _rallies[(r.TileId, r.BuildingId)] = r;

    internal void ClearRally(int tileId, int buildingId) => _rallies.Remove((tileId, buildingId));

    public static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Length <= MaxNameLength;

    /// <summary>A deep copy, for state snapshots (NF-008).</summary>
    internal Squads Clone()
    {
        var copy = new Squads();
        copy.RestoreFrom(this);
        return copy;
    }

    /// <summary>Makes this an exact copy of <paramref name="other"/>, for loading state snapshots (NF-008).</summary>
    internal void RestoreFrom(Squads other)
    {
        _squads.Clear();
        foreach (var s in other._squads.Values)
        {
            var c = new Squad(s.Id, s.Owner, s.Name) { Formation = s.Formation, Custom = s.Custom, Stance = s.Stance, Order = s.Order, TargetId = s.TargetId };
            c.MemberList.AddRange(s.MemberList);
            _squads.Add(c.Id, c);
        }
        _rallies.Clear();
        foreach (var (k, r) in other._rallies)
            _rallies.Add(k, r);
        _nextId = other._nextId;
    }
}
