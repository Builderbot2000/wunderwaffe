namespace Wunderwaffe.Sim.Core;

/// <summary>
/// What a <see cref="GameEvent"/> reports (UI-003). Silos append new kinds at the end; never reorder or rename, the
/// external driver sends them by name.
/// </summary>
public enum GameEventKind
{
    /// <summary>A building or unit of <see cref="GameEvent.Player"/> on the tile took damage (combat raises it).</summary>
    TileUnderAttack,

    /// <summary>A machine on the tile has been waiting for inputs for a while; <see cref="GameEvent.ItemId"/> is what it lacks.</summary>
    InputsStarved,

    /// <summary>A unit of the player's was finished: lifted off, or built by a factory.</summary>
    UnitCompleted,

    /// <summary>The player took the tile.</summary>
    TileClaimed,

    /// <summary>The player lost the tile.</summary>
    TileLost,

    /// <summary>The player lost their last core (MP-001).</summary>
    PlayerEliminated,

    /// <summary>The player won the match (MP-001).</summary>
    Victory,

    /// <summary>The player completed a goal; <see cref="GameEvent.ItemId"/> is the goal id.</summary>
    GoalCompleted,
}

/// <summary>
/// A notification for a player: alerts, toasts, AI reactions. Events are output, not state: they are rebuilt the same
/// way on replay but aren't hashed, and nothing in the sim reads them back. <see cref="Seq"/> grows by one per event,
/// so a reader keeps the last one it saw and asks for <see cref="Simulation.EventsSince"/>.
/// </summary>
/// <param name="TileId">The tile it happened on, or -1.</param>
/// <param name="UnitId">The unit involved, or -1.</param>
public readonly record struct GameEvent(long Seq, long Tick, GameEventKind Kind, int Player, int TileId, int UnitId, string? ItemId);
