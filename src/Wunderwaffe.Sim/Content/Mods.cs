using System.Text.Json;
using System.Text.Json.Nodes;

namespace Wunderwaffe.Sim.Content;

/// <summary>
/// Every content text <see cref="ContentDb.FromJson"/> reads, by the file names in <c>game/data</c> (NF-003): the list
/// files by name (<see cref="ListFiles"/>), prefabs and scenarios one text per file. Null for a file not given.
/// </summary>
public sealed record ContentSources(
    IReadOnlyDictionary<string, string> Lists, IReadOnlyList<string> Prefabs, IReadOnlyList<string> Scenarios)
{
    /// <summary>The list files, in the order <see cref="Load"/> passes them to <see cref="ContentDb.FromJson"/>.</summary>
    public static readonly string[] ListFiles =
        ["items.json", "recipes.json", "buildings.json", "goals.json", "terrain.json", "layers.json", "head_starts.json", "factions.json", "sites.json"];

    string? List(string name) => Lists.TryGetValue(name, out var text) ? text : null;

    /// <summary>Builds and validates the content (throws <see cref="ContentException"/>).</summary>
    public ContentDb Load() => ContentDb.FromJson(List("items.json") ?? "[]", List("recipes.json") ?? "[]", List("buildings.json") ?? "[]",
        List("goals.json"), List("terrain.json"), List("layers.json"), Prefabs, List("head_starts.json"), Scenarios,
        List("factions.json"), List("sites.json"));
}

/// <summary>
/// A content mod (NF-006): a folder holding <c>mod.json</c> (<see cref="Id"/>, <see cref="Name"/>) and any of the
/// <see cref="ContentSources.ListFiles"/>, <c>prefabs/</c> and <c>scenarios/</c>, read by the game into
/// <see cref="Lists"/> (file name → text), <see cref="Prefabs"/> and <see cref="Scenarios"/>. Sprites in its
/// <c>sprites/</c> folder are the game's business.
/// </summary>
public sealed record ModDef(string Id, string Name, IReadOnlyDictionary<string, string> Lists,
    IReadOnlyList<string> Prefabs, IReadOnlyList<string> Scenarios)
{
    /// <summary>
    /// Reads a mod's <c>mod.json</c> text (an object with <c>id</c> and optional <c>name</c>) with its files. Throws
    /// <see cref="ContentException"/> without an id or with a list file <see cref="Mods"/> doesn't know.
    /// </summary>
    public static ModDef Parse(string manifest, IReadOnlyDictionary<string, string> lists, IReadOnlyList<string> prefabs, IReadOnlyList<string> scenarios)
    {
        var node = JsonNode.Parse(manifest, documentOptions: Mods.Read) as JsonObject;
        string id = node?["id"]?.GetValue<string>() ?? throw new ContentException(["mod.json: no id"]);
        if (lists.Keys.FirstOrDefault(k => !ContentSources.ListFiles.Contains(k)) is { } unknown)
            throw new ContentException([$"mod '{id}': unknown content file '{unknown}'"]);
        return new ModDef(id, node["name"]?.GetValue<string>() ?? id, lists, prefabs, scenarios);
    }
}

/// <summary>
/// NF-006 content modding: a mod's list entries replace the base entries with the same <c>id</c> where they stand and
/// the rest are added after them, so a mod can add a block or retune one without copying whole files. Mod prefabs and
/// scenarios join the base ones (a prefab with a taken id replaces it). Mods apply in the order given, so a later mod
/// wins. Content is validated once, after every mod, by <see cref="ContentSources.Load"/>.
/// </summary>
public static class Mods
{
    internal static readonly JsonDocumentOptions Read = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>The base content with each mod applied in order.</summary>
    public static ContentSources Apply(ContentSources content, IEnumerable<ModDef> mods)
    {
        var lists = new Dictionary<string, string>(content.Lists, StringComparer.Ordinal);
        var prefabs = content.Prefabs.ToList();
        var scenarios = content.Scenarios.ToList();
        foreach (var mod in mods)
        {
            foreach (var (name, text) in mod.Lists)
                lists[name] = lists.TryGetValue(name, out var baseText) ? Merge(baseText, text, $"mod '{mod.Id}' {name}") : text;
            foreach (var text in mod.Prefabs)
            {
                string id = IdOf(JsonNode.Parse(text, documentOptions: Read), $"mod '{mod.Id}' prefab");
                prefabs.RemoveAll(p => IdOf(JsonNode.Parse(p, documentOptions: Read), "prefab") == id);
                prefabs.Add(text);
            }
            scenarios.AddRange(mod.Scenarios);
        }
        return new ContentSources(lists, prefabs, scenarios);
    }

    /// <summary>A JSON list with <paramref name="mod"/>'s entries merged into <paramref name="baseList"/> by id.</summary>
    public static string Merge(string baseList, string mod, string at)
    {
        var merged = JsonNode.Parse(baseList, documentOptions: Read) as JsonArray ?? throw new ContentException([$"{at}: the base file is not a list"]);
        var add = JsonNode.Parse(mod, documentOptions: Read) as JsonArray ?? throw new ContentException([$"{at}: expected a list"]);
        foreach (var entry in add.ToList())
        {
            add.Remove(entry);
            string id = IdOf(entry, at);
            int i = merged.ToList().FindIndex(e => e?["id"]?.GetValue<string>() == id);
            if (i < 0)
                merged.Add(entry);
            else
                merged[i] = entry;
        }
        return merged.ToJsonString();
    }

    static string IdOf(JsonNode? entry, string at) =>
        entry is JsonObject o && o["id"]?.GetValue<string>() is { Length: > 0 } id ? id : throw new ContentException([$"{at}: an entry with no id"]);
}
