using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Api;

/// <summary>Faction queries (MP-009, MP-010): what the seat's faction can build, its capture record, variants.</summary>
public sealed partial class GameQuery
{
    /// <summary>The faction <paramref name="player"/> (default: this seat) plays, with their capture record.</summary>
    public FactionView Faction(int? player = null)
    {
        int p = player ?? Player;
        var f = Sim.FactionOf(p);
        var subset = Sim.Content.BuildingList.Where(b => f.Has(b.Id)).ToList();
        return new FactionView(f.Id, f.Name, f.Description, [.. subset.Select(b => b.Id)],
            [.. subset.Where(b => Factions.Check(Sim, p, b.Id) == FactionResult.NotCaptured).Select(b => b.Id)],
            f.Variants ?? new SortedDictionary<string, string>(StringComparer.Ordinal), f.Rules ?? [],
            [.. Sim.PlayerOrNull(p)?.Acquired ?? new SortedSet<string>()]);
    }

    /// <summary>Whether this seat's faction lets it build <paramref name="buildingId"/> now (the build menu's lock).</summary>
    public FactionResult CheckFaction(string buildingId) => Factions.Check(Sim, Player, buildingId);

    /// <summary>A shared blueprint as this seat's faction builds it: its variants in place of bases (OQ-55).</summary>
    public Blueprint ForFaction(Blueprint blueprint) => Factions.Substitute(Sim, Player, blueprint);

    /// <summary>
    /// The first of the blueprint's blocks (after <see cref="ForFaction"/>) this seat's faction can't build now, as
    /// its result; Ok if it can build them all. The library's "doesn't work for your faction" marker.
    /// </summary>
    public FactionResult CheckBlueprintFaction(Blueprint blueprint) =>
        ForFaction(blueprint).Blocks.Select(b => CheckFaction(b.DefId)).FirstOrDefault(r => r != FactionResult.Ok);

    /// <summary>The filter roles search with (MP-007): what this seat can build now.</summary>
    internal Func<BuildingDef, bool> Can => Factions.Buildable(Sim, Player);
}
