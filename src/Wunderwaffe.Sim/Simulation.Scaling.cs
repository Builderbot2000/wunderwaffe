using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim;

/// <summary>Sim scaling and persistence state: rate mode (FB-012..014), abstract units (UC-009), snapshots (NF-008).</summary>
public sealed partial class Simulation
{
    readonly List<TileRateState> _rateStates = [];
    readonly SortedDictionary<int, AbstractNode> _abstract = [];

    /// <summary>
    /// Tiles nobody views run in rate mode and units on them travel abstractly. On for <see cref="NewGame"/>; off by
    /// default for hand-built sims, so entity tests see every tile fully simulated. Set it before the first
    /// <see cref="Step"/>; it is part of how the match was made, not something that changes in play.
    /// </summary>
    public bool RateModeEnabled { get; set; }

    /// <summary>Ticks between automatic snapshots (NF-008); 0 turns them off. Five minutes of play by default.</summary>
    public int SnapshotIntervalTicks { get; set; } = 5 * 60 * TicksPerSecond;

    /// <summary>The newest automatic snapshot, taken at the start of a tick, or null. Saves carry it (NF-008).</summary>
    public Snapshot? LatestSnapshot { get; internal set; }

    /// <summary>Abstract units by id: units on rate-mode tiles, moved without physics.</summary>
    public IReadOnlyDictionary<int, AbstractNode> Abstract => _abstract;

    internal SortedDictionary<int, AbstractNode> AbstractNodes => _abstract;

    /// <summary>True while the unit is an abstract world-map node: not rendered on a tile, no physics (UC-009).</summary>
    public bool IsAbstract(Unit u) => _abstract.ContainsKey(u.Id);

    internal TileRateState RateState(int tileId)
    {
        while (_rateStates.Count <= tileId)
            _rateStates.Add(new TileRateState());
        return _rateStates[tileId];
    }

    /// <summary>Whether tick phase 3 runs the tile's entity sim this tick.</summary>
    bool FullSim(TileState tile) => !RateModeEnabled || RateState(tile.Id).Full;

    /// <summary>Takes the periodic snapshot at the start of a tick, before its commands, so replay can resume there.</summary>
    void MaybeSnapshot()
    {
        if (Setup != null && SnapshotIntervalTicks > 0 && Tick > 0 && Tick % SnapshotIntervalTicks == 0)
            LatestSnapshot = Snapshot.Capture(this);
    }

    // ---- Snapshot restore (Core/Snapshot.cs). ----

    internal int NextUnitId => _nextUnitId;

    internal void RestoreUnits(IEnumerable<Unit> units)
    {
        _units.Clear();
        _unitsById.Clear();
        _abstract.Clear();
        foreach (var u in units)
        {
            _units.Add(u);
            _unitsById.Add(u.Id, u);
        }
    }

    internal void RestoreClock(long tick, int nextUnitId, IEnumerable<LoggedCommand> log)
    {
        Tick = tick;
        _nextUnitId = nextUnitId;
        _log.Clear();
        _log.AddRange(log);
        _pending.Clear();
    }
}
