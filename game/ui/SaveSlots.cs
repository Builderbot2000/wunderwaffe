using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace Wunderwaffe.Game.Screens;

/// <summary>One save on disk, for the load menu.</summary>
public sealed record SaveInfo(string Path, string Name, DateTime SavedAt, long Tick, string Label);

/// <summary>
/// The saves folder (<c>user://saves</c>). Each save is one JSON file named by the time it was made, so listing
/// newest first is a sort on the name.
/// </summary>
public static class SaveSlots
{
    public const string Dir = "user://saves";

    public static string NewPath() => $"{Dir}/{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.json";

    public static void EnsureDir() => DirAccess.MakeDirRecursiveAbsolute(Dir);

    /// <summary>Saves newest first. Files that can't be read are listed with an empty label rather than hidden.</summary>
    public static List<SaveInfo> List()
    {
        var list = new List<SaveInfo>();
        using var dir = DirAccess.Open(Dir);
        if (dir == null)
            return list;
        foreach (var file in dir.GetFiles().Where(f => f.EndsWith(".json")))
        {
            string path = $"{Dir}/{file}";
            var when = DateTime.UnixEpoch.AddSeconds(FileAccess.GetModifiedTime(path)).ToLocalTime();
            long tick = 0;
            string label = "";
            try
            {
                // The header fields come before the command log; JsonDocument still parses it all, which is fine
                // for the handful of saves a player keeps.
                using var doc = JsonDocument.Parse(FileAccess.GetFileAsString(path));
                if (doc.RootElement.TryGetProperty("Tick", out var t))
                    tick = t.GetInt64();
                if (doc.RootElement.TryGetProperty("Label", out var l))
                    label = l.GetString() ?? "";
            }
            catch (JsonException)
            {
                label = "(unreadable)";
            }
            list.Add(new SaveInfo(path, file[..^5].Replace('_', ' '), when, tick, label));
        }
        return list.OrderByDescending(s => s.SavedAt).ToList();
    }

    public static SaveInfo? Newest() => List().FirstOrDefault();

    public static void Delete(string path) => DirAccess.RemoveAbsolute(path);
}
