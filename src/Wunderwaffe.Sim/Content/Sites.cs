using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Content;

/// <summary>
/// A neutral site template (WM-010, WM-018) from <c>game/data/sites.json</c>, weakest first. World generation stamps
/// <paramref name="Layout"/> for a neutral player of <paramref name="Faction"/> (a hidden faction) beside a deposit
/// patch it multiplies by <paramref name="Richness"/> percent, its first core holding the layout's items, and spawns
/// <paramref name="Guards"/> units of <paramref name="Guard"/> on defensive stance.
/// </summary>
public sealed record SiteDef(string Id, string Name, string Faction, PrefabDef Layout, PrefabDef? Guard = null, int Guards = 0,
    int Richness = 100)
{
    /// <summary>
    /// How hard the site is to take, from content alone (MP-007): every weapon's DPS (layout and guards) times every
    /// block's HP, over 1000.
    /// </summary>
    public long Strength(ContentDb content)
    {
        var layout = UnitStats.Of(content, Layout.Blueprint);
        var guard = Guard is { } g && Guards > 0 ? UnitStats.Of(content, g.Blueprint) : null;
        long dps = layout.Dps + (guard?.Dps ?? 0) * (long)Guards;
        long hp = layout.MaxHp + (guard?.MaxHp ?? 0) * (long)Guards;
        return dps * hp / 1000;
    }

    /// <summary>What the site holds, from content alone: item tiers over its buildings' costs and its stock, guards included.</summary>
    public long Value(ContentDb content)
    {
        long Of(PrefabDef p) =>
            p.Blocks.Sum(b => content.Buildings[b.DefId].Cost.Sum(c => (long)content.Items[c.Key].Tier * c.Value))
            + (p.Items ?? []).Sum(i => (long)content.Items[i.Key].Tier * i.Value);
        return Of(Layout) + (Guard is { } g ? Of(g) * Guards : 0);
    }

    public const int MaxGuards = 16, MinRichness = 100, MaxRichness = 1000;

    /// <summary>
    /// Content validation of the site list (WM-018): names, a known hidden faction that can build every block, a layout
    /// with a core, a guard with exactly one core when there are guards, richness in range, and strength, value and
    /// richness rising from one template to the next.
    /// </summary>
    internal static void Validate(ContentDb content, IReadOnlyList<SiteDef> list, List<string> errors)
    {
        SiteDef? prev = null;
        foreach (var s in list)
        {
            string at = $"site '{s.Id}'";
            int before = errors.Count;
            if (string.IsNullOrWhiteSpace(s.Name))
                errors.Add($"{at}: no name");
            if (!content.Factions.TryGetValue(s.Faction ?? "", out var f) || !f.Hidden)
                errors.Add($"{at}: faction '{s.Faction}' must be a hidden faction (the neutral guardians)");
            if (s.Layout is null)
            {
                errors.Add($"{at}: no layout");
                continue;
            }
            errors.AddRange(Prefabs.Problems(content, $"{at} layout", s.Layout.Blueprint, s.Layout.Items));
            if (s.Guards is < 0 or > MaxGuards || (s.Guards > 0) != (s.Guard != null))
                errors.Add($"{at}: give a guard with 1 to {MaxGuards} guards, or neither");
            if (s.Guard is { } g)
                errors.AddRange(Prefabs.Problems(content, $"{at} guard", g.Blueprint, g.Items));
            if (s.Richness is < MinRichness or > MaxRichness)
                errors.Add($"{at}: richness {s.Richness}; {MinRichness} to {MaxRichness} allowed");
            if (errors.Count > before)
                continue;

            if (!s.Layout.Blocks.Any(b => content.Buildings[b.DefId].Kind == BuildingKind.Core))
                errors.Add($"{at}: the layout needs a core");
            if (s.Guard is { } guard && guard.Blocks.Count(b => content.Buildings[b.DefId].Kind == BuildingKind.Core) != 1)
                errors.Add($"{at}: a guard unit has exactly one core");
            if (s.Guard is { Frames.Length: 0 })
                errors.Add($"{at}: a guard unit needs frame cells");
            foreach (var b in s.Layout.Blocks.Concat(s.Guard?.Blocks ?? []))
            {
                if (f is { } faction && !faction.Has(b.DefId))
                    errors.Add($"{at}: faction '{faction.Id}' can't build '{b.DefId}'");
            }
            if (errors.Count > before)
                continue;
            if (prev != null && (s.Strength(content) <= prev.Strength(content) || s.Value(content) <= prev.Value(content) || s.Richness < prev.Richness))
                errors.Add($"{at}: strength, value and richness must rise from '{prev.Id}' (weakest first)");
            prev = s;
        }
    }
}
