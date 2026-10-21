using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim;

/// <summary>
/// A neutral site (WM-010): an installation of a hidden guardian faction, held by its own neutral player
/// (<see cref="Player"/>, after every seat of the setup). It falls when that player has no core left (OQ-54), on a tile
/// or a guard unit: its buildings become unclaimed (WM-019). The record never changes; what stands there is ordinary
/// sim state.
/// </summary>
/// <param name="X">Tile-local top-left of the layout; <paramref name="Width"/> x <paramref name="Height"/> its box.</param>
/// <param name="Template">The <see cref="SiteDef"/> id it was built from.</param>
public sealed record NeutralSite(int Id, int Player, int TileId, int X, int Y, int Width, int Height, string Template, string Name)
{
    /// <summary>Id of the deposit patch it guards (RS-004, made richer by its template), or 0.</summary>
    public int Patch { get; internal set; }

    public int CentreX => X + Width / 2;
    public int CentreY => Y + Height / 2;
}

/// <summary>Partial: neutral sites (WM-010, WM-018): the neutral seats, and placing sites when a world is generated.</summary>
public sealed partial class Simulation
{
    readonly List<NeutralSite> _sites = [];
    readonly Dictionary<int, NeutralSite> _siteOf = [];

    /// <summary>Every neutral site, in id order (WM-010). Fixed once the world is built.</summary>
    public IReadOnlyList<NeutralSite> Sites => _sites;

    /// <summary>The site a neutral player holds, or null for a seat of the setup (or nobody).</summary>
    public NeutralSite? SiteOf(int player) => _siteOf.GetValueOrDefault(player);

    /// <summary>
    /// True for a neutral player (WM-010): outside victory and the AI director, never claims, expands or attacks beyond
    /// its site. Neutral players come after every seat of the setup.
    /// </summary>
    public bool IsNeutral(int player) => _siteOf.ContainsKey(player);

    /// <summary>Whether the site has fallen: its neutral player has no core left (OQ-54), so its buildings are unclaimed.</summary>
    public bool HasFallen(NeutralSite site) => CoreCount(site.Player) == 0;

    /// <summary>Registers a site under a new neutral player, after every seat and existing player. Builds nothing.</summary>
    internal NeutralSite AddSite(TileState tile, SiteDef def, int x, int y, string? name = null, int patch = 0)
    {
        int player = Math.Max(_players.Count, Setup?.Players ?? 0);
        var (w, h) = def.Layout.Blueprint.Size(Content.Buildings);
        var site = new NeutralSite(_sites.Count, player, tile.Id, x, y, w, h, def.Id, name ?? $"{def.Name}, {tile.Name}") { Patch = patch };
        _sites.Add(site);
        _siteOf[player] = site;
        Player(player);
        if (player < _factions.Length)
            _factions[player] = null;
        return site;
    }

    /// <summary>
    /// Places neutral sites on a generated world (WM-018), after the starts are settled: <paramref name="percent"/> of
    /// the tiles, picked by seed among those neither a start nor beside one (and reachable), each taking the template
    /// whose rank matches its distance from the nearest start (nearest gets the weakest, farthest the strongest). A tile whose
    /// patches leave no spot clear of the victory circle and the borders is skipped.
    /// </summary>
    void PlaceSites(int percent, ulong seed)
    {
        var templates = Content.SiteList;
        if (percent <= 0 || templates.Count == 0 || _startTileIds.Count == 0)
            return;
        var steps = TileSteps([.. _startTileIds]);
        var near = new HashSet<int>(_startTileIds);
        foreach (int s in _startTileIds)
            near.UnionWith(_tiles[s].Neighbors);
        var candidates = _tiles.Where(t => !near.Contains(t.Id) && steps[t.Id] != int.MaxValue).ToList();
        if (candidates.Count == 0)
            return;
        var rng = new Rng(seed ^ 0x517E517E517E517EUL);
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = rng.Range(0, i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }
        // Strength by distance from the nearest start (tile centres, in cells): rank among every candidate, so the
        // nearest third gets the weakest template and the farthest the strongest, whatever the map's size.
        long Dist(TileState t) => _startTileIds.Min(s => Sq(t.CenterX - _tiles[s].CenterX) + Sq(t.CenterY - _tiles[s].CenterY));
        var byDistance = candidates.OrderBy(Dist).ThenBy(t => t.Id).Select(t => t.Id).ToList();
        int want = (_tiles.Count * percent + 50) / 100, placed = 0;
        foreach (var tile in candidates)
        {
            if (placed >= want)
                break;
            int rank = byDistance.IndexOf(tile.Id) * templates.Count / byDistance.Count;
            if (TryBuildSite(tile, templates[rank]) != null)
                placed++;
        }
    }

    /// <summary>
    /// Builds a site from <paramref name="def"/> on the tile beside its richest deposit patch that leaves room: the layout
    /// clear of the victory circle by <see cref="Balance.SiteCircleClearance"/> and off the port zones. Null if none does.
    /// </summary>
    internal NeutralSite? TryBuildSite(TileState tile, SiteDef def)
    {
        var (w, h) = def.Layout.Blueprint.Size(Content.Buildings);
        var grid = tile.Grid;
        foreach (var patch in grid.Patches.All.Where(p => !p.Empty).OrderByDescending(p => p.Amount).ThenBy(p => p.Id))
        {
            int px = (patch.X0 + patch.X1) / 2, py = (patch.Y0 + patch.Y1) / 2;
            if (FreeSpot(tile, w, h, px - w / 2, py - h / 2) is not { } spot || !SiteSpotOk(tile, def, spot.X, spot.Y, w, h))
                continue;
            var site = BuildSite(tile, def, spot.X, spot.Y, patch: patch.Id);
            grid.Patches.Enrich(patch, def.Richness);
            return site;
        }
        return null;
    }

    bool SiteSpotOk(TileState tile, SiteDef def, int x, int y, int w, int h)
    {
        int c = Balance.SiteCircleClearance;
        if (tile.InCircle(x - c, y - c, w + 2 * c, h + 2 * c))
            return false;
        for (int cy = y; cy < y + h; cy++)
        {
            for (int cx = x; cx < x + w; cx++)
            {
                if (tile.Grid.ZoneAt(cx, cy) >= 0)
                    return false;
            }
        }
        // As BuildSite will stamp it: for the next neutral player, presence lent, the guardian faction's catalogue.
        int owner = tile.Owner, player = Math.Max(_players.Count, Setup?.Players ?? 0);
        FactionOf(player);
        var was = _factions[player];
        (_factions[player], tile.Owner) = (Content.Factions[def.Faction], player);
        var result = Blueprints.Check(this, tile, player, def.Layout.Blueprint, x, y, 0, false, out _, ignoreRadius: true);
        (_factions[player], tile.Owner) = (was, owner);
        return result is BlueprintResult.Ok or BlueprintResult.CannotAfford;
    }

    /// <summary>
    /// Registers a site and builds it: the layout stamped for its neutral player at (x, y) (free, its first core holding
    /// the layout's items), then its guards beside it on defensive stance (WM-010).
    /// </summary>
    internal NeutralSite BuildSite(TileState tile, SiteDef def, int x, int y, string? name = null, int patch = 0)
    {
        var site = AddSite(tile, def, x, y, name, patch);
        int owner = tile.Owner;
        tile.Owner = site.Player; // the stamp needs presence; sites never own their tile
        Blueprints.Stamp(this, tile, site.Player, def.Layout, x, y);
        tile.Owner = owner;
        if (def.Guard is { } guard)
        {
            var (gw, gh) = guard.Blueprint.Size(Content.Buildings);
            for (int i = 0; i < def.Guards; i++)
            {
                int sx = x + (i % 2 == 0 ? -gw - 2 : site.Width + 2), sy = y + (i / 2) * (gh + 2);
                if (FreeSpot(tile, gw, gh, sx, sy) is { } spot)
                    SpawnGuard(site, tile, guard, spot.X, spot.Y);
            }
        }
        return site;
    }

    /// <summary>A guard unit from a unit prefab with its top-left at local (x, y), its cargo the prefab's items, defending where it stands.</summary>
    void SpawnGuard(NeutralSite site, TileState tile, PrefabDef guard, int x, int y)
    {
        var core = guard.Blocks.First(b => Content.Buildings[b.DefId].Kind == BuildingKind.Core);
        var blocks = guard.Blocks.Select(b =>
        {
            var def = Content.Buildings[b.DefId];
            var (bw, bh) = def.Footprint(b.Rotation);
            return new UnitBlock(b.DefId, b.X - core.X, b.Y - core.Y, bw, bh, b.Rotation, b.RecipeId, def.Hp);
        }).ToList();
        var unit = UnitAssembly.Spawn(this, tile, site.Player, x + core.X, y + core.Y,
            [.. guard.Frames.Select(f => (f.X - core.X, f.Y - core.Y))], blocks);
        if (guard.Items is { Count: > 0 } items)
            unit.Cargo.Add(items);
        unit.Stance = Stance.Defensive;
    }

    static long Sq(long v) => v * v;
}
