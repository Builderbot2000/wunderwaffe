using System.Collections.Generic;
using Godot;

namespace Wunderwaffe.Game.Render;

/// <summary>
/// Sprite lookup by content id: <c>res://assets/sprites/{id}.png</c>. Content without a sprite returns null,
/// and callers fall back to the <see cref="Palette"/> colour. Sprites face east at rotation 0.
/// </summary>
public static class Sprites
{
    const string Dir = "res://assets/sprites/";

    public const string Ground = "ground";
    public const string Frame = "frame";

    static readonly Dictionary<string, Texture2D?> Cache = [];

    public static Texture2D? Get(string id)
    {
        if (!Cache.TryGetValue(id, out var tex))
        {
            string path = $"{Dir}{id}.png";
            tex = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
            Cache[id] = tex;
        }
        return tex;
    }
}
