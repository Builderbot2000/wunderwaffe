using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim;

/// <summary>The event list (UI-003): notifications any silo raises during a tick, read by the HUD, AI and drivers.</summary>
public sealed partial class Simulation
{
    /// <summary>Events kept for late readers; older ones drop off.</summary>
    public const int EventCapacity = 4096;

    readonly List<GameEvent> _events = [];
    long _nextEventSeq = 1;

    /// <summary>Tiles <see cref="World.Alerts"/> has reported as starved and not seen recover. Output-only.</summary>
    internal SortedSet<int> StarvedTiles { get; } = [];

    /// <summary>The most recent events (at most <see cref="EventCapacity"/>), oldest first.</summary>
    public IReadOnlyList<GameEvent> Events => _events;

    /// <summary>The <see cref="GameEvent.Seq"/> of the newest event, or 0.</summary>
    public long LastEventSeq => _nextEventSeq - 1;

    /// <summary>Events after <paramref name="seq"/>, oldest first. Pass 0 for every kept event.</summary>
    public IEnumerable<GameEvent> EventsSince(long seq)
    {
        int start = _events.Count;
        while (start > 0 && _events[start - 1].Seq > seq)
            start--;
        for (int i = start; i < _events.Count; i++)
            yield return _events[i];
    }

    /// <summary>Records an event on the current tick. Never read back by the sim: events are output only.</summary>
    internal void Raise(GameEventKind kind, int player, int tileId = -1, int unitId = -1, string? itemId = null)
    {
        if (_events.Count == EventCapacity)
            _events.RemoveRange(0, EventCapacity / 4);
        _events.Add(new GameEvent(_nextEventSeq++, Tick, kind, player, tileId, unitId, itemId));
    }
}
