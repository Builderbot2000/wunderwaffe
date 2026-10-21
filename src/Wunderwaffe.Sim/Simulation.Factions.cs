using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim;

/// <summary>Partial: each seat's faction (MP-009), fixed for the match by the setup.</summary>
public sealed partial class Simulation
{
    FactionDef?[] _factions = [];

    /// <summary>
    /// The faction <paramref name="player"/> plays: <see cref="GameSetup.FactionFor"/> of the setup, or the default for
    /// hand-built sims. Derived from the setup, so not hashed.
    /// </summary>
    public FactionDef FactionOf(int player)
    {
        if (player < 0)
            return Content.DefaultFaction;
        if (player >= _factions.Length)
            Array.Resize(ref _factions, player + 1);
        return _factions[player] ??= Setup?.FactionFor(player, Content) ?? Content.DefaultFaction;
    }

    /// <summary>Gives a seat of a hand-built sim a faction (tests).</summary>
    internal void SetFaction(int player, string factionId)
    {
        FactionOf(player);
        _factions[player] = Content.Factions[factionId];
    }
}
