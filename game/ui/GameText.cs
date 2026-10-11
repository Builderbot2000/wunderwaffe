using System.Collections.Generic;
using System.Linq;
using Wunderwaffe.Sim;
using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Game.Screens;

/// <summary>Player-facing wording for content: names, costs, recipes, conditions. Pure functions of the content.</summary>
public static class GameText
{
    public static string ItemName(ContentDb content, string id) => content.Items.TryGetValue(id, out var i) ? i.Name : id;

    /// <summary>"10 Copper Ore, 5 Iron Ore", or "free".</summary>
    public static string Cost(ContentDb content, IEnumerable<KeyValuePair<string, int>> items) =>
        items.Any() ? string.Join(", ", items.Select(kv => $"{kv.Value} {ItemName(content, kv.Key)}")) : "free";

    /// <summary>"2 Iron Ore → 1 Iron Plate in 2 s", or "Mines 1 Coal every 1 s" for extraction.</summary>
    public static string Recipe(ContentDb content, RecipeDef r)
    {
        string Items(Dictionary<string, int> items) => string.Join(" + ", items.Select(kv => $"{kv.Value} {ItemName(content, kv.Key)}"));
        string time = $"{r.Ticks / (double)Simulation.TicksPerSecond:0.#} s";
        return r.Inputs.Count == 0 ? $"Mines {Items(r.Outputs)} every {time}"
            : r.Outputs.Count == 0 ? $"Burns {Items(r.Inputs)} every {time}"
            : $"{Items(r.Inputs)} → {Items(r.Outputs)} in {time}";
    }

    /// <summary>Booster stats as "speed +25%, radius +4": the non-zero ones, in <see cref="BoosterDef.Stats"/> order.</summary>
    public static string Effects(IReadOnlyDictionary<string, int> effects) => string.Join(", ",
        BoosterDef.Stats.Where(s => effects.GetValueOrDefault(s) != 0).Select(s => Effect(s, effects[s])));

    static string Effect(string stat, int n) => stat switch
    {
        "precision" => "builds tier-3+ and weapon blocks",
        "cooling" => "cooled",
        "radius" => $"radius {n:+0;-0}",
        "block_cap" => $"block cap {n:+0;-0}",
        "input_sides" => $"input sides {n:+0;-0}",
        _ => $"{stat.Replace('_', ' ')} {n:+0;-0}%",
    };

    /// <summary>A booster's stacking and shape rules in a sentence (FB-016).</summary>
    public static string BoosterRules(BoosterDef b)
    {
        var rules = new List<string>
        {
            b.Chain ? "counts in a straight line running outward from its host, end to end" : "must touch its host; boosts every host it touches",
        };
        if (b.MaxHosts > 0)
            rules.Add($"serves at most {b.MaxHosts} hosts");
        if (b.MaxPerHost > 0)
            rules.Add($"a host counts at most {b.MaxPerHost}");
        if (b.HalfWithout is { } need)
            rules.Add($"half effect unless {Effect(need, 1)}");
        string text = string.Join("; ", rules);
        return char.ToUpperInvariant(text[0]) + text[1..] + ".";
    }

    /// <summary>What a condition's amount counts: "Iron Plate", "tiles owned", ...</summary>
    public static string ConditionUnit(ContentDb content, UnlockCondition c) => c.Type switch
    {
        "produce" => ItemName(content, c.Item!),
        "claim" => "tiles owned",
        "ship" => "items shipped",
        "launch" => c.Amount == 1 ? "unit launched" : "units launched",
        _ => c.Type,
    };

    public static string TerrainName(ContentDb content, string terrainId) =>
        content.TerrainList.FirstOrDefault(t => t.Id == terrainId)?.Name ?? terrainId;

    public static string Plural(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";
}
