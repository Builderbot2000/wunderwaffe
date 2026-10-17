using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Screens;

/// <summary>One saved blueprint. The blocks can't be edited: record it again to change a design (FB-009, UD-001).</summary>
public sealed class BlueprintEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public Blueprint Blueprint { get; set; } = new([], []);
    public DateTime Created { get; set; }

    /// <summary>Shipped with the content (FB-021): placeable, projectable and duplicable, never renamed, retagged or deleted.</summary>
    [JsonIgnore] public bool IsPrefab { get; init; }
}

/// <summary>
/// The player's blueprint library (FB-009): <c>user://blueprints.json</c>, shared by every match and kept outside the
/// sim. Entries can be renamed, duplicated, tagged and deleted. Every change saves at once. The content's prefabs
/// (FB-021) are listed after the player's own, read-only.
/// </summary>
public static class BlueprintLibrary
{
    public const string Path = "user://blueprints.json";

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    static List<BlueprintEntry>? _entries;
    static List<BlueprintEntry>? _prefabs;

    /// <summary>Bumped on every change, so panels know when to rebuild.</summary>
    public static int Revision { get; private set; }

    /// <summary>The player's own, newest first.</summary>
    static List<BlueprintEntry> Own => _entries ??= Load();

    /// <summary>The player's own, newest first, then the prefabs by id.</summary>
    public static IReadOnlyList<BlueprintEntry> Entries => [.. Own, .. Prefabs];

    static List<BlueprintEntry> Prefabs => _prefabs ??= [.. SimHost.Instance.Content.PrefabList.Select(p => new BlueprintEntry
    {
        Id = "prefab:" + p.Id,
        Name = p.Name,
        Tags = [.. p.Tags ?? []],
        Blueprint = p.Blueprint,
        IsPrefab = true,
    })];

    public static BlueprintEntry? Get(string id) => Entries.FirstOrDefault(e => e.Id == id);

    static List<BlueprintEntry> Load()
    {
        if (!FileAccess.FileExists(Path))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<BlueprintEntry>>(FileAccess.GetFileAsString(Path), Options) ?? [];
        }
        catch (JsonException e)
        {
            GD.PrintErr($"blueprint library unreadable, starting empty: {e.Message}");
            return [];
        }
    }

    static void Save()
    {
        using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        file.StoreString(JsonSerializer.Serialize(_entries, Options));
        Revision++;
    }

    public static BlueprintEntry Add(string name, Blueprint blueprint, IEnumerable<string>? tags = null)
    {
        var entry = new BlueprintEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Tags = tags?.ToList() ?? [],
            Blueprint = blueprint,
            Created = DateTime.Now,
        };
        Own.Insert(0, entry);
        Save();
        return entry;
    }

    public static void Rename(string id, string name)
    {
        if (Get(id) is { IsPrefab: false } e && name.Trim() is { Length: > 0 } trimmed)
        {
            e.Name = trimmed;
            Save();
        }
    }

    /// <summary>Tags from comma-separated text; blanks and repeats dropped.</summary>
    public static void SetTags(string id, string text)
    {
        if (Get(id) is not { IsPrefab: false } e)
            return;
        e.Tags = [.. text.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).Distinct()];
        Save();
    }

    public static BlueprintEntry? Duplicate(string id) =>
        Get(id) is { } e ? Add($"{e.Name} copy", e.Blueprint, e.Tags) : null;

    public static void Delete(string id)
    {
        if (Own.RemoveAll(e => e.Id == id) > 0)
            Save();
    }

    /// <summary>The entry as a text code another player can import (UD-009).</summary>
    public static string Export(BlueprintEntry e) => Wunderwaffe.Sim.Content.Prefabs.ToCode(e.Name, e.Blueprint, e.Tags);

    /// <summary>Adds the blueprint in a code from <see cref="Export"/>; null with the reason if it can't be read.</summary>
    public static BlueprintEntry? Import(string code, out string? error) =>
        Wunderwaffe.Sim.Content.Prefabs.FromCode(SimHost.Instance.Content, code, out error) is { } p ? Add(p.Name, p.Blueprint, p.Tags) : null;

    /// <summary>"Unit 3" for the next unnamed unit, and so on.</summary>
    public static string NextName(string kind) =>
        $"{kind} {Entries.Count(e => e.Name.StartsWith(kind + " ")) + 1}";
}
