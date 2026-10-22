using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Wunderwaffe.Game.Render;

/// <summary>
/// Sprite lookup by content id: <c>res://assets/sprites/{id}.png</c>. Content without a sprite returns null,
/// and callers fall back to the <see cref="Palette"/> colour. Sprites face east at rotation 0. Mods' sprite folders
/// (NF-006, <see cref="AddDir"/>) come first, later mods before earlier ones, so a mod can draw its own or replace one.
/// </summary>
public static class Sprites
{
    const string Dir = "res://assets/sprites/";

    public const string Ground = "ground";

    static readonly Dictionary<string, Texture2D?> Cache = [];
    static readonly List<string> ModDirs = [];

    /// <summary>Adds a mod's sprite folder (a user:// path of <c>{id}.png</c> files) ahead of the shipped sprites.</summary>
    public static void AddDir(string dir)
    {
        ModDirs.Insert(0, dir);
        Cache.Clear();
    }

    public static Texture2D? Get(string id)
    {
        if (!Cache.TryGetValue(id, out var tex))
        {
            string? mod = ModDirs.Select(d => $"{d}/{id}.png").FirstOrDefault(FileAccess.FileExists);
            string path = $"{Dir}{id}.png";
            tex = mod != null ? ImageTexture.CreateFromImage(Image.LoadFromFile(mod))
                : ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
            Cache[id] = tex;
        }
        return tex;
    }
}
