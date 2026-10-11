using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
}

/// <summary>
/// The player's blueprint library (FB-009): <c>user://blueprints.json</c>, shared by every match and kept outside the
/// sim. Entries can be renamed, duplicated, tagged and deleted. Every change saves at once.
/// </summary>
public static class BlueprintLibrary
{
    public const string Path = "user://blueprints.json";

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    static List<BlueprintEntry>? _entries;

    /// <summary>Bumped on every change, so panels know when to rebuild.</summary>
    public static int Revision { get; private set; }

    /// <summary>Newest first.</summary>
    public static IReadOnlyList<BlueprintEntry> Entries => _entries ??= Load();

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
        _ = Entries;
        _entries!.Insert(0, entry);
        Save();
        return entry;
    }

    public static void Rename(string id, string name)
    {
        if (Get(id) is { } e && name.Trim() is { Length: > 0 } trimmed)
        {
            e.Name = trimmed;
            Save();
        }
    }

    /// <summary>Tags from comma-separated text; blanks and repeats dropped.</summary>
    public static void SetTags(string id, string text)
    {
        if (Get(id) is not { } e)
            return;
        e.Tags = [.. text.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).Distinct()];
        Save();
    }

    public static BlueprintEntry? Duplicate(string id) =>
        Get(id) is { } e ? Add($"{e.Name} copy", e.Blueprint, e.Tags) : null;

    public static void Delete(string id)
    {
        _ = Entries;
        if (_entries!.RemoveAll(e => e.Id == id) > 0)
            Save();
    }

    /// <summary>"Unit 3" for the next unnamed unit, and so on.</summary>
    public static string NextName(string kind) =>
        $"{kind} {Entries.Count(e => e.Name.StartsWith(kind + " ")) + 1}";
}
