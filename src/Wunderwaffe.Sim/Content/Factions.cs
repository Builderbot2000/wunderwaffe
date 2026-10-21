using System.Text.Json.Serialization;

namespace Wunderwaffe.Sim.Content;

/// <summary>
/// A faction rule (MP-010) from the fixed list the sim knows, with its parameters. It covers the buildings it names and
/// every building of <see cref="Kind"/>.
/// </summary>
/// <param name="Type">One of <see cref="Types"/>.</param>
/// <param name="Stat">Buff only: one of <see cref="BuffStats"/>, the machine and factory stats boosters raise too.</param>
/// <param name="Percent">Buff only: added to the stat, like a booster's percent (20 = 120% speed); never 0.</param>
public sealed record FactionRule(
    string Type,
    string[]? Buildings = null,
    [property: JsonConverter(typeof(SnakeCaseEnumConverter))] BuildingKind? Kind = null,
    string? Stat = null,
    int Percent = 0)
{
    /// <summary>The seat may build a covered building only once it has owned one it didn't build (OQ-56).</summary>
    public const string CaptureToBuild = "capture_to_build";

    /// <summary>A percentage change to one stat of the covered buildings the seat owns.</summary>
    public const string Buff = "buff";

    public static readonly string[] Types = [CaptureToBuild, Buff];

    /// <summary>Stats a buff may change: the <see cref="World.BoostStats"/> percent stats (machine speed and power draw, factory build speed).</summary>
    public static readonly string[] BuffStats = ["speed", "power", "build_speed"];

    public const int MinPercent = -90, MaxPercent = 400;

    public bool Covers(BuildingDef def) => (Buildings?.Contains(def.Id) ?? false) || Kind == def.Kind;
}

/// <summary>
/// A faction template (MP-009, OQ-55): which parts of the shared catalogue (buildings.json, the hidden master tech
/// tree) its seats can build, which of its own variants replace shared entries, and the rules it plays by (MP-010).
/// The first faction of factions.json is the default.
/// </summary>
/// <param name="Buildings">Allow list of catalogue ids; null for the whole catalogue less every faction's variants.</param>
/// <param name="Except">Deny list taken out of the whole catalogue (not with <paramref name="Buildings"/>).</param>
/// <param name="Variants">Base id → the faction's own building that replaces it: same kind, footprint and layer.</param>
/// <param name="StartPrefabs">Prefabs its seats start with when the setup names none (FB-021); null for the default.</param>
/// <param name="Hidden">Not offered in the lobby nor picked by Random (a neutral guardian template, WM-010).</param>
public sealed record FactionDef(
    string Id, string Name, string? Description = null, string[]? Buildings = null, string[]? Except = null,
    SortedDictionary<string, string>? Variants = null, FactionRule[]? Rules = null, string[]? StartPrefabs = null,
    bool Hidden = false)
{
    SortedSet<string> _subset = new(StringComparer.Ordinal);
    Dictionary<string, string> _baseOf = [];

    /// <summary>The catalogue ids its seats can build (capture-gated ones included), variants in, replaced bases out.</summary>
    [JsonIgnore]
    public IReadOnlySet<string> Subset => _subset;

    public bool Has(string defId) => _subset.Contains(defId);

    /// <summary>
    /// What a seat of this faction builds for catalogue entry <paramref name="defId"/> (a blueprint block, a prefab
    /// block): its own variant of a base, the base of another faction's variant it lacks, else the id itself.
    /// </summary>
    public string Map(string defId) =>
        Variants?.GetValueOrDefault(defId) is { } v ? v
        : !Has(defId) && _baseOf.GetValueOrDefault(defId) is { } b && Has(Map(b)) ? Map(b)
        : defId;

    /// <summary>Whether a capture-to-build rule covers the building.</summary>
    public bool CaptureGated(BuildingDef def) =>
        Rules?.Any(r => r.Type == FactionRule.CaptureToBuild && r.Covers(def)) ?? false;

    /// <summary>The summed buff percent to <paramref name="stat"/> for a building of this faction's.</summary>
    public int BuffPercent(BuildingDef def, string stat) =>
        Rules?.Where(r => r.Type == FactionRule.Buff && r.Stat == stat && r.Covers(def)).Sum(r => r.Percent) ?? 0;

    /// <summary>Works out <see cref="Subset"/> from the catalogue and every faction's variants (variant → base).</summary>
    internal void Resolve(ContentDb content, IReadOnlyDictionary<string, string> baseOf)
    {
        _baseOf = new(baseOf);
        var all = content.BuildingList.Select(b => b.Id);
        _subset = new SortedSet<string>(Buildings ?? all.Where(id => !baseOf.ContainsKey(id)), StringComparer.Ordinal);
        _subset.ExceptWith(Except ?? []);
        foreach (var (b, v) in Variants ?? [])
        {
            _subset.Remove(b);
            _subset.Add(v);
        }
    }

    /// <summary>
    /// The items the subset's recipes make, back to extraction (RS-002): the hidden tree's resource gating. A building
    /// is reachable once every item of its cost is in here.
    /// </summary>
    public SortedSet<string> Producible(ContentDb content)
    {
        var made = new SortedSet<string>(StringComparer.Ordinal);
        var recipes = content.Recipes.Values.Where(r => Has(r.Building)).OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
        for (bool grew = true; grew;)
        {
            grew = false;
            foreach (var r in recipes)
            {
                if (r.Inputs.Keys.All(made.Contains) && r.Outputs.Keys.Any(o => !made.Contains(o)))
                {
                    made.UnionWith(r.Outputs.Keys);
                    grew = true;
                }
            }
        }
        return made;
    }

    /// <summary>The default faction when content has no factions file: the whole catalogue, no rules.</summary>
    internal static FactionDef Everything(ContentDb content)
    {
        var f = new FactionDef("default", "Default");
        f.Resolve(content, new Dictionary<string, string>());
        return f;
    }

    /// <summary>Content validation of every faction (MP-009): ids, variants, rules, the start's needs, resource gating.</summary>
    internal static void Validate(ContentDb content, IReadOnlyList<FactionDef> list, List<string> errors)
    {
        var defs = content.Buildings;
        foreach (var f in list)
        {
            string at = $"faction '{f.Id}'";
            if (string.IsNullOrWhiteSpace(f.Name))
                errors.Add($"{at}: no name");
            if (f.Buildings != null && f.Except != null)
                errors.Add($"{at}: give buildings or except, not both");
            foreach (var id in (f.Buildings ?? []).Concat(f.Except ?? []).Concat(f.Rules?.SelectMany(r => r.Buildings ?? []) ?? []))
            {
                if (!defs.ContainsKey(id))
                    errors.Add($"{at}: unknown building '{id}'");
            }
            foreach (var (b, v) in f.Variants ?? [])
            {
                if (!defs.TryGetValue(b, out var bd) || !defs.TryGetValue(v, out var vd) || b == v)
                {
                    errors.Add($"{at}: variant '{v}' of '{b}': both must be other catalogue buildings");
                    continue;
                }
                if (bd.Kind != vd.Kind || bd.Width != vd.Width || bd.Height != vd.Height || bd.Layer != vd.Layer)
                    errors.Add($"{at}: variant '{v}' must have the kind, footprint and layer of '{b}'");
            }
            foreach (var r in f.Rules ?? [])
            {
                if (!FactionRule.Types.Contains(r.Type))
                    errors.Add($"{at}: unknown rule '{r.Type}'; one of {string.Join(", ", FactionRule.Types)}");
                if (r.Buildings is not { Length: > 0 } && r.Kind == null)
                    errors.Add($"{at}: rule '{r.Type}' names no buildings or kind");
                if (r.Type == FactionRule.Buff && (!FactionRule.BuffStats.Contains(r.Stat)
                    || r.Percent is 0 or < FactionRule.MinPercent or > FactionRule.MaxPercent))
                    errors.Add($"{at}: buff needs a stat ({string.Join(", ", FactionRule.BuffStats)}) and a percent of {FactionRule.MinPercent} to {FactionRule.MaxPercent}, not 0");
            }
            if (errors.Count > 0)
                continue;

            // The start needs a core, a conveyor and an extractor it can build without capturing anything.
            bool Can(BuildingDef d) => f.Has(d.Id) && !f.CaptureGated(d);
            var mine = content.BuildingList.Where(Can).ToList();
            if (!mine.Any(d => d.Kind == BuildingKind.Core))
                errors.Add($"{at}: no core it can build");
            if (!mine.Any(d => d.Kind == BuildingKind.Conveyor))
                errors.Add($"{at}: no conveyor it can build");
            if (!mine.Any(d => content.RecipesByBuilding.GetValueOrDefault(d.Id)?.Any(r => r.Inputs.Count == 0) ?? false))
                errors.Add($"{at}: no extractor it can build");

            // Resource gating: every entry's cost comes from the subset's own recipes, back to extraction.
            var made = f.Producible(content);
            foreach (var d in content.BuildingList.Where(d => f.Has(d.Id)))
            {
                foreach (var item in d.Cost.Keys.Where(i => !made.Contains(i)))
                    errors.Add($"{at}: '{d.Id}' costs '{item}', which nothing in the faction makes (its prerequisites are missing)");
            }

            foreach (var p in f.StartPrefabs ?? [])
            {
                if (!content.Prefabs.TryGetValue(p, out var prefab))
                    errors.Add($"{at}: unknown start prefab '{p}'");
                else if (Misfit(content, f, prefab) is { } bad)
                    errors.Add($"{at}: start prefab '{p}' uses '{bad}', which the faction can't build");
            }
        }
    }

    /// <summary>The first block of <paramref name="prefab"/> a seat of the faction couldn't build at the start, or null.</summary>
    public static string? Misfit(ContentDb content, FactionDef f, PrefabDef prefab) =>
        (prefab.Blocks ?? []).Select(b => f.Map(b.DefId))
            .Concat(prefab.Frames is { Length: > 0 } && content.Frame is { } frame ? [frame.Id] : [])
            .FirstOrDefault(id => !f.Has(id) || (content.Buildings.TryGetValue(id, out var d) && f.CaptureGated(d)));
}
