using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Content;

/// <summary>
/// A prefab blueprint (FB-021): a blueprint shipped as content in <c>game/data/prefabs/*.json</c>, one per file. The
/// library offers it like the player's own, and match setup stamps prefabs onto each start (<see cref="GameSetup.StartPrefabs"/>).
/// <paramref name="Items"/> is what its first core starts with: the starting items come from the prefab, not code.
/// The same shape, without the id, is the text code a blueprint exports to (UD-009, <see cref="Prefabs.ToCode"/>).
/// </summary>
public sealed record PrefabDef(
    string Id, string Name, BlueprintBlock[] Blocks, BlueprintCell[] Frames,
    SortedDictionary<string, int>? Items = null, string[]? Tags = null)
{
    public Blueprint Blueprint => new(Blocks, Frames);

    /// <summary>
    /// Tagged <see cref="FlyingTag"/>: a start spawns it as a hovering unit beside the seat's first core instead of
    /// stamping it landed (WM-005's small starting vehicle).
    /// </summary>
    public bool Flying => Tags?.Contains(FlyingTag) == true;

    public const string FlyingTag = "flying";
}

/// <summary>Prefab validation and blueprint text codes (FB-021, UD-009).</summary>
public static class Prefabs
{
    /// <summary>Starts every blueprint code, with the format version.</summary>
    public const string CodePrefix = "WW1:";

    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    internal static PrefabDef Parse(string json) =>
        JsonSerializer.Deserialize<PrefabDef>(json, Options) ?? throw new JsonException("expected a prefab object");

    /// <summary>
    /// What is wrong with a blueprint as content, each line prefixed with <paramref name="owner"/>: unknown block ids or
    /// recipes, rotations outside 0..3, overlapping blocks, repeated or negative frame cells, a grid not starting at
    /// (0, 0), a unit block off its frame, starting items without a core or of unknown items.
    /// </summary>
    public static List<string> Problems(ContentDb content, string owner, Blueprint bp, IReadOnlyDictionary<string, int>? items)
    {
        var errors = new List<string>();
        if (bp.Blocks is null || bp.Frames is null || bp.Blocks.Length + bp.Frames.Length == 0)
        {
            errors.Add($"{owner}: no blocks or frame");
            return errors;
        }
        var taken = new HashSet<(int, int)>();
        foreach (var b in bp.Blocks)
        {
            if (!content.Buildings.TryGetValue(b.DefId ?? "", out var def))
            {
                errors.Add($"{owner}: unknown building '{b.DefId}'");
                continue;
            }
            if (b.Rotation is < 0 or > 3)
                errors.Add($"{owner}: '{b.DefId}' at ({b.X}, {b.Y}) has rotation {b.Rotation}; 0 to 3 allowed");
            if (b.RecipeId is { } r && (!content.Recipes.TryGetValue(r, out var recipe) || recipe.Building != b.DefId))
                errors.Add($"{owner}: '{b.DefId}' at ({b.X}, {b.Y}) can't run recipe '{r}'");
            var (w, h) = def.Footprint(b.Rotation & 3);
            for (int y = b.Y; y < b.Y + h; y++)
            {
                for (int x = b.X; x < b.X + w; x++)
                {
                    if (!taken.Add((x, y)))
                        errors.Add($"{owner}: '{b.DefId}' at ({b.X}, {b.Y}) overlaps another block at ({x}, {y})");
                }
            }
        }
        var frames = new HashSet<(int, int)>();
        foreach (var f in bp.Frames)
        {
            if (!frames.Add((f.X, f.Y)))
                errors.Add($"{owner}: frame cell ({f.X}, {f.Y}) repeated");
        }
        var cells = bp.Cells(content.Buildings).ToList();
        if (cells.Count > 0 && (cells.Min(c => c.X) != 0 || cells.Min(c => c.Y) != 0))
            errors.Add($"{owner}: the grid must start at (0, 0) top-left");
        var unframed = taken.Where(c => !frames.Contains(c)).Order().ToList();
        if (bp.IsUnit(content.Buildings) && unframed.Count > 0)
            errors.Add($"{owner}: a unit's blocks must stand on its frame; ({unframed[0].Item1}, {unframed[0].Item2}) has none");
        if (items is { Count: > 0 })
        {
            if (!bp.Blocks.Any(b => content.Buildings.TryGetValue(b.DefId ?? "", out var d) && d.Kind == BuildingKind.Core))
                errors.Add($"{owner}: starting items need a core to hold them");
            foreach (var (id, n) in items)
            {
                if (!content.Items.ContainsKey(id))
                    errors.Add($"{owner}: unknown item '{id}'");
                if (n <= 0)
                    errors.Add($"{owner}: item '{id}' count must be > 0");
            }
        }
        return errors;
    }

    internal static void Validate(ContentDb content, PrefabDef p, List<string> errors)
    {
        string owner = $"prefab '{p.Id}'";
        if (string.IsNullOrWhiteSpace(p.Id))
            errors.Add("prefab with no id");
        if (string.IsNullOrWhiteSpace(p.Name))
            errors.Add($"{owner}: no name");
        errors.AddRange(Problems(content, owner, p.Blueprint, p.Items));
        if (p.Flying && (p.Frames.Length == 0 || p.Blocks.Count(b => content.Buildings.TryGetValue(b.DefId ?? "", out var d) && d.Kind == BuildingKind.Core) != 1))
            errors.Add($"{owner}: a flying prefab is a unit: a frame and exactly one core");
    }

    /// <summary>
    /// A blueprint as a text code to paste to another player (UD-009): <see cref="CodePrefix"/> then the prefab JSON,
    /// deflated and base64url-encoded. <see cref="FromCode"/> turns it back into the same blueprint.
    /// </summary>
    public static string ToCode(string name, Blueprint bp, IEnumerable<string>? tags = null)
    {
        var def = new PrefabDef("", name, bp.Blocks, bp.Frames, null, tags?.ToArray());
        var json = JsonSerializer.SerializeToUtf8Bytes(def, Options);
        using var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.SmallestSize))
            deflate.Write(json);
        return CodePrefix + Convert.ToBase64String(buffer.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// Reads a code from <see cref="ToCode"/>, validated like a prefab against <paramref name="content"/>. Null with
    /// <paramref name="error"/> set if it is malformed or names blocks this game doesn't have.
    /// </summary>
    public static PrefabDef? FromCode(ContentDb content, string code, out string? error)
    {
        error = null;
        code = code.Trim();
        if (!code.StartsWith(CodePrefix, StringComparison.Ordinal))
        {
            error = "not a blueprint code";
            return null;
        }
        PrefabDef def;
        try
        {
            string b64 = code[CodePrefix.Length..].Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            using var deflate = new DeflateStream(new MemoryStream(Convert.FromBase64String(b64)), CompressionMode.Decompress);
            using var json = new MemoryStream();
            deflate.CopyTo(json);
            def = Parse(Encoding.UTF8.GetString(json.ToArray()));
        }
        catch (Exception e) when (e is FormatException or InvalidDataException or JsonException)
        {
            error = "the code is damaged";
            return null;
        }
        var problems = Problems(content, "blueprint", def.Blueprint, null);
        if (problems.Count > 0)
        {
            error = problems[0];
            return null;
        }
        return def;
    }
}
