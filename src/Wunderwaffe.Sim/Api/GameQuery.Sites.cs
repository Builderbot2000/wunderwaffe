using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Api;

/// <summary>Neutral sites (WM-010, WM-018): where they are, how strong, what they hold. For the world map, hover, alerts and the AI.</summary>
public sealed partial class GameQuery
{
    /// <summary>Every neutral site, in id order.</summary>
    public IReadOnlyList<SiteView> Sites() => [.. Sim.Sites.Select(ViewOf)];

    /// <summary>The site a neutral player holds, or null for a seat.</summary>
    public SiteView? SiteOf(int player) => Sim.SiteOf(player) is { } s ? ViewOf(s) : null;

    /// <summary>True for a neutral site's player (WM-010): not a seat, never in player lists or the end screen.</summary>
    public bool IsNeutral(int player) => Sim.IsNeutral(player);

    SiteView ViewOf(NeutralSite s)
    {
        var tile = Sim.Tiles[s.TileId];
        bool fallen = Sim.HasFallen(s);
        // Once fallen, what stood there is unclaimed: the unclaimed buildings overlapping the site's box.
        var buildings = tile.Grid.Buildings.Where(b => fallen
            ? Territory.IsUnclaimed(b) && b.X < s.X + s.Width && s.X < b.X + b.Width && b.Y < s.Y + s.Height && s.Y < b.Y + b.Height
            : b.Owner == s.Player).ToList();
        var guards = Sim.Units.Where(u => u.Owner == s.Player).ToList();
        int dps = buildings.Where(b => !fallen).Sum(b => Combat.Dps(b.Def)) + guards.Sum(u => UnitStats.Of(Content, u).Dps);
        int hp = buildings.Sum(b => b.Hp) + guards.Sum(u => UnitStats.Of(Content, u).Hp);
        var stock = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var core in buildings.Where(b => b.Stock != null))
        {
            foreach (var (item, n) in core.Stock!.Entries)
                stock[item] = stock.GetValueOrDefault(item) + n;
        }
        var faction = Sim.FactionOf(Player);
        var tech = buildings.Select(b => b.DefId).Where(id => !faction.Has(id)).Distinct().Order(StringComparer.Ordinal).ToList();
        var patch = tile.Grid.Patches.ById(s.Patch);
        var def = Content.Sites.GetValueOrDefault(s.Template);
        return new SiteView(s.Id, s.Name, s.Player, s.TileId, s.X, s.Y, s.Width, s.Height, s.Template, fallen,
            buildings.Count(b => b.Kind == BuildingKind.Core), buildings.Count(b => b.Kind == BuildingKind.Weapon), guards.Count,
            dps, hp, stock, tech, patch?.Item, patch?.Amount ?? 0, def?.Strength(Content) ?? 0, def?.Value(Content) ?? 0);
    }
}
