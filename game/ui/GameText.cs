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
        return r.Inputs.Count == 0 ? $"Mines {Items(r.Outputs)} every {time}" : $"{Items(r.Inputs)} → {Items(r.Outputs)} in {time}";
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

    /// <summary>The buildings a tech node unlocks, by name.</summary>
    public static string UnlockNames(ContentDb content, TechDef tech) =>
        string.Join(", ", tech.Unlocks.Where(content.Buildings.ContainsKey).Select(id => content.Buildings[id].Name));

    public static string TerrainName(ContentDb content, string terrainId) =>
        content.TerrainList.FirstOrDefault(t => t.Id == terrainId)?.Name ?? terrainId;

    public static string Plural(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";
}
