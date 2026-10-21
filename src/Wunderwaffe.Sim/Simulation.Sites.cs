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

    /// <summary>Id of the regional item's patch laid around it (WM-018, RS-003; <see cref="SiteDef.Regional"/>), or 0.</summary>
    public int Regional { get; internal set; }

    /// <summary>
    /// Index into <see cref="Simulation.StartTileIds"/> of the start whose share of sites this is (WM-018 fair starts),
    /// or -1 (a scenario's own site).
    /// </summary>
    public int Start { get; internal set; } = -1;

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
    internal NeutralSite AddSite(TileState tile, SiteDef def, int x, int y, string? name = null, int patch = 0, int regional = 0)
    {
        int player = Math.Max(_players.Count, Setup?.Players ?? 0);
        var (w, h) = def.Layout.Blueprint.Size(Content.Buildings);
        var site = new NeutralSite(_sites.Count, player, tile.Id, x, y, w, h, def.Id, name ?? $"{def.Name}, {tile.Name}") { Patch = patch, Regional = regional };
        _sites.Add(site);
        _siteOf[player] = site;
        Player(player);
        if (player < _factions.Length)
            _factions[player] = null;
        return site;
    }

    /// <summary>
    /// Places neutral sites on a generated world (WM-018), after the starts are settled, so that every start has the same
    /// access to them (fair starts, OQ-22): a draft. Candidates are tiles neither a start nor beside one (and reachable).
    /// Each start drafts the same tiers of sites, one per round, the order of starts rotating each round from a seeded
    /// first; round k places the template ranked at the middle of tier k (the first rounds the weakest) on the unused
    /// candidate with room whose distance from the start (tile centres) is nearest the start's own k-th tier distance
    /// (the middle of its k-th slice of candidates, nearest first) and no nearer than its previous site. So each start
    /// gets the same count and templates, outward at comparable distances. Tiers per start: <paramref name="percent"/>
    /// of the tiles shared among the starts (at least one), fewer while the draft can't fill them. Planned read-only,
    /// then built. Each site records the start it was drafted for (<see cref="NeutralSite.Start"/>).
    /// </summary>
    void PlaceSites(int percent, ulong seed)
    {
        var templates = Content.SiteList;
        int n = _startTileIds.Count;
        if (percent <= 0 || templates.Count == 0 || n == 0)
            return;
        var steps = TileSteps([.. _startTileIds]);
        var near = new HashSet<int>(_startTileIds);
        foreach (int s in _startTileIds)
            near.UnionWith(_tiles[s].Neighbors);
        var candidates = _tiles.Where(t => !near.Contains(t.Id) && steps[t.Id] != int.MaxValue).ToList();
        if (candidates.Count < n)
            return;
        var dist = new long[n][];
        for (int i = 0; i < n; i++)
            dist[i] = [.. _tiles.Select(t => StartDist(t, i))];
        // Per start, its candidates' distances (cells), nearest first: tier k aims for the middle of its k-th slice.
        var sorted = Enumerable.Range(0, n).Select(i => candidates.Select(t => IntMath.ISqrt(dist[i][t.Id])).Order().ToArray()).ToArray();
        int first = new Rng(seed ^ 0x517E517E517E517EUL).Range(0, n);

        var spots = new Dictionary<(int, string), SiteSpot?>();
        SiteSpot? Spot(TileState t, SiteDef def) =>
            spots.TryGetValue((t.Id, def.Id), out var s) ? s : spots[(t.Id, def.Id)] = FindSiteSpot(t, def);

        int want = (_tiles.Count * percent + 50) / 100;
        for (int q = Math.Min(Math.Max(1, want / n), candidates.Count / n); q > 0; q--)
        {
            var plan = new List<(TileState Tile, SiteDef Def, SiteSpot Spot, int Start)>();
            var last = new long[n];
            for (int k = 0; k < q && plan.Count == k * n; k++)
            {
                var def = templates[(2 * k + 1) * templates.Count / (2 * q)];
                for (int j = 0; j < n; j++)
                {
                    int i = (first + k + j) % n;
                    long target = sorted[i][(2 * k + 1) * sorted[i].Length / (2 * q)];
                    var free = candidates.Where(t => !plan.Any(p => p.Tile == t))
                        .OrderBy(t => dist[i][t.Id] < last[i] ? 1 : 0)
                        .ThenBy(t => Math.Abs(IntMath.ISqrt(dist[i][t.Id]) - target)).ThenBy(t => t.Id);
                    var pick = free.Select(t => (Tile: t, Spot: Spot(t, def))).FirstOrDefault(c => c.Spot != null);
                    if (pick.Spot is not { } spot)
                        break;
                    plan.Add((pick.Tile, def, spot, i));
                    last[i] = dist[i][pick.Tile.Id];
                }
            }
            if (plan.Count < q * n)
                continue;
            foreach (var (tile, def, spot, start) in plan.OrderBy(p => p.Tile.Id))
                BuildSite(tile, def, spot).Start = start;
            return;
        }
    }

    /// <summary>Squared distance in cells from start <paramref name="start"/> (index into <see cref="StartTileIds"/>) to the tile, centre to centre.</summary>
    internal long StartDist(TileState t, int start)
    {
        var s = _tiles[_startTileIds[start]];
        return Sq(t.CenterX - s.CenterX) + Sq(t.CenterY - s.CenterY);
    }

    /// <summary>Where a site of a template goes on a tile: the layout's top-left and the patch it guards.</summary>
    sealed record SiteSpot(int X, int Y, int Patch);

    /// <summary>
    /// Where <paramref name="def"/> fits on the tile, read-only: beside its richest deposit patch that leaves room, the
    /// layout clear of the victory circle by <see cref="Balance.SiteCircleClearance"/> and off the port zones. Null if none.
    /// </summary>
    SiteSpot? FindSiteSpot(TileState tile, SiteDef def)
    {
        var (w, h) = def.Layout.Blueprint.Size(Content.Buildings);
        foreach (var patch in tile.Grid.Patches.All.Where(p => !p.Empty).OrderByDescending(p => p.Amount).ThenBy(p => p.Id))
        {
            int px = (patch.X0 + patch.X1) / 2, py = (patch.Y0 + patch.Y1) / 2;
            if (FreeSpot(tile, w, h, px - w / 2, py - h / 2) is { } spot && SiteSpotOk(tile, def, spot.X, spot.Y, w, h))
                return new SiteSpot(spot.X, spot.Y, patch.Id);
        }
        return null;
    }

    /// <summary>Builds a site from <paramref name="def"/> where <see cref="FindSiteSpot"/> finds room (tests). Null if none.</summary>
    internal NeutralSite? TryBuildSite(TileState tile, SiteDef def) => FindSiteSpot(tile, def) is { } spot ? BuildSite(tile, def, spot) : null;

    /// <summary>A generated site: built at the spot, its patch made richer by the template, its regional deposit laid.</summary>
    NeutralSite BuildSite(TileState tile, SiteDef def, SiteSpot spot)
    {
        var site = BuildSite(tile, def, spot.X, spot.Y, patch: spot.Patch);
        tile.Grid.Patches.Enrich(tile.Grid.Patches.ById(spot.Patch)!, def.Richness);
        LayRegional(tile, site, def);
        return site;
    }

    /// <summary>
    /// The rare treasure (WM-018, RS-003): <see cref="SiteDef.Regional"/> cells of a regional item's deposit (the first by
    /// id the tile has no patch of, else one by site id) in rings around the layout, nearest first, on free buildable
    /// cells off port zones and the victory circle: one patch at the template's and the world's richness.
    /// </summary>
    void LayRegional(TileState tile, NeutralSite site, SiteDef def)
    {
        var items = Content.Items.Values.Where(i => i.Regional && i.Deposit != null).OrderBy(i => i.Id, StringComparer.Ordinal).ToList();
        if (def.Regional <= 0 || items.Count == 0)
            return;
        var grid = tile.Grid;
        var item = items.FirstOrDefault(i => !grid.Patches.All.Any(p => p.Item == i.Id && !p.Empty)) ?? items[site.Id % items.Count];
        var cells = new List<(int X, int Y)>();
        for (int d = 1; d <= RegionalRings && cells.Count < def.Regional; d++)
        {
            int x0 = site.X - d, y0 = site.Y - d, x1 = site.X + site.Width - 1 + d, y1 = site.Y + site.Height - 1 + d;
            for (int y = y0; y <= y1 && cells.Count < def.Regional; y++)
            {
                // Top and bottom rows whole, the rows between only their two ends.
                for (int x = x0; x <= x1 && cells.Count < def.Regional; x += y == y0 || y == y1 ? 1 : x1 - x0)
                {
                    if (grid.Buildable(x, y) && grid.DepositAt(x, y) == null && grid.BuildingAt(x, y) == null
                        && grid.ZoneAt(x, y) < 0 && !tile.InCircle(x, y, 1, 1))
                        cells.Add((x, y));
                }
            }
        }
        if (cells.Count == 0)
            return;
        foreach (var (x, y) in cells)
            grid.SetDeposit(x, y, item.Id);
        long amount = Math.Max(1, (long)item.Deposit!.PerCell * cells.Count * (Setup?.RichnessPercent ?? 100) / 100 * def.Richness / 100);
        site.Regional = grid.Patches.Add(Content, item.Id, cells, amount).Id;
    }

    /// <summary>How many rings around a site's layout its regional deposit may spread over.</summary>
    const int RegionalRings = 6;

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
