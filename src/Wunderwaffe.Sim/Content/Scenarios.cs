using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Content;

/// <summary>
/// A scenario (WM-017, OQ-51): a whole map plus what stands on it at the start, as a content file in
/// <c>game/data/scenarios/*.json</c> (or saved from a match with <see cref="Simulation.ToScenario"/>). A match whose
/// <see cref="GameSetup.Scenario"/> names it is built from this instead of generated from the seed. Everything is in
/// world or tile-local cells; cell maps are <see cref="CellGrid"/>s (one string per row) so the file can be edited.
/// </summary>
/// <param name="Width">World size in cells.</param>
/// <param name="Terrain">Terrain per world cell; the legend holds terrain ids.</param>
/// <param name="TileMap">Tile id per world cell, -1 for none (no legend). Ids run 0 to <c>Tiles.Count - 1</c>.</param>
/// <param name="Elevation">
/// Elevation per world cell (0-1000, only the world map's shading uses it), little-endian 16-bit, deflated and base64:
/// null for flat land and low water.
/// </param>
/// <param name="Tiles">Per tile id: name, victory circle, port zones and cell layers.</param>
/// <param name="Seats">Per player: what they start with (MP-008: an AI seat takes over its own).</param>
/// <param name="Sites">Neutral sites (WM-010), each its own neutral player after the seats; null for none.</param>
public sealed record ScenarioDef(
    string Id, string Name, int Width, int Height, CellGrid Terrain, CellGrid TileMap, List<ScenarioTile> Tiles, List<ScenarioSeat> Seats,
    string? Description = null, string? Elevation = null, List<ScenarioSite>? Sites = null)
{
    /// <summary>Hash of the scenario's content (<see cref="Scenarios.Hash"/>): a save records it so it replays the same map.</summary>
    [JsonIgnore]
    public ulong Hash { get; internal set; }
}

/// <summary>
/// One tile of a scenario. <paramref name="CircleX"/>, <paramref name="CircleY"/>: victory circle centre in tile-local
/// cells (WM-014). <paramref name="Zones"/>: neighbour tile id per cell of the tile's bounding box, -1 for none (null:
/// computed from the borders, as generation does; drop it after changing a tile's shape). <paramref name="Layers"/>:
/// each non-empty cell layer by layer id (FB-022: deposits, platforms, frames, cables), over the bounding box, its legend
/// holding item or building ids with "" for empty. <paramref name="Patches"/>: the deposit patches (RS-004: item, cells
/// when found, starting and remaining amount), with <paramref name="PatchMap"/> giving each box cell's patch, 1-based
/// (0 for none); null finds them from the deposits as a generated map does, at normal richness. Drop both after editing
/// the deposits' shapes.
/// </summary>
public sealed record ScenarioTile(string Name, int CircleX, int CircleY, CellGrid? Zones = null, SortedDictionary<string, CellGrid>? Layers = null,
    List<Core.PatchRecord>? Patches = null, CellGrid? PatchMap = null);

/// <summary>
/// A seat's starting state: <paramref name="Tiles"/> it owns (the first is its start tile), its buildings and its units.
/// A seat with no buildings or units gets the setup's start prefabs on its start tile, as a generated map does (FB-021).
/// <paramref name="Faction"/> fixes the seat's faction (MP-009, e.g. a campaign mission); null lets the lobby pick.
/// </summary>
public sealed record ScenarioSeat(List<int> Tiles, List<ScenarioBuilding>? Buildings = null, List<ScenarioUnit>? Units = null,
    string? Faction = null);

/// <summary>
/// A neutral site (WM-010) built from site template <paramref name="Template"/> (its guardian faction), its layout's box
/// at tile-local (X, Y) on <paramref name="Tile"/>: the site's buildings (a core at least) and guard units, placed as a
/// seat's are. Its guards defend where they stand. <paramref name="Patch"/>: the tile's deposit patch it guards (1-based), or 0.
/// </summary>
public sealed record ScenarioSite(string Template, int Tile, int X, int Y, string? Name = null, List<ScenarioBuilding>? Buildings = null,
    List<ScenarioUnit>? Units = null, int Patch = 0);

/// <summary>
/// A building at tile-local (X, Y), its footprint's top-left. <paramref name="Hp"/> null is full; <paramref name="Stock"/> is
/// a core's items. Placed by the placement rules (bounds, overlap, terrain, port zones, recipes), but free.
/// </summary>
public sealed record ScenarioBuilding(int Tile, string DefId, int X, int Y, int Rotation = 0, string? RecipeId = null, int? Hp = null,
    SortedDictionary<string, long>? Stock = null);

/// <summary>
/// A unit at rest, heading 0, its core's top-left at tile-local (X, Y). Blocks and frame cells are relative to that
/// cell (the core block is at (0, 0)). <paramref name="Cargo"/> is its core's stock (UD-010).
/// </summary>
public sealed record ScenarioUnit(int Tile, int X, int Y, BlueprintBlock[] Blocks, BlueprintCell[] Frames, SortedDictionary<string, long>? Cargo = null);

/// <summary>
/// A grid of cell values, one string per row of <c>v</c> or <c>v*count</c> runs separated by commas (<c>"0*120,3*8,0*128"</c>).
/// With a <see cref="Legend"/>, values index it (content ids); without, they are plain integers.
/// </summary>
public sealed record CellGrid(string[] Rows, string[]? Legend = null)
{
    /// <summary>A w x h grid of <paramref name="value"/>(x, y), run-length coded per row.</summary>
    internal static CellGrid Encode(int w, int h, Func<int, int, int> value, string[]? legend = null)
    {
        var rows = new string[h];
        var sb = new StringBuilder();
        for (int y = 0; y < h; y++)
        {
            sb.Clear();
            for (int x = 0; x < w;)
            {
                int v = value(x, y), n = 1;
                while (x + n < w && value(x + n, y) == v)
                    n++;
                if (sb.Length > 0)
                    sb.Append(',');
                sb.Append(v);
                if (n > 1)
                    sb.Append('*').Append(n);
                x += n;
            }
            rows[y] = sb.ToString();
        }
        return new CellGrid(rows, legend);
    }

    /// <summary>The values row by row, each in [min, max] (or within the legend); null with errors added if malformed.</summary>
    internal int[]? Decode(int w, int h, int min, int max, string what, List<string> errors)
    {
        if (Legend != null)
            (min, max) = (0, Legend.Length - 1);
        if (Rows is null || Rows.Length != h)
        {
            errors.Add($"{what}: {Rows?.Length ?? 0} rows, expected {h}");
            return null;
        }
        var cells = new int[w * h];
        for (int y = 0; y < h; y++)
        {
            int x = 0;
            foreach (var token in Rows[y].Split(',', StringSplitOptions.TrimEntries))
            {
                int star = token.IndexOf('*');
                if (!int.TryParse(star < 0 ? token : token[..star], out int v) || v < min || v > max)
                {
                    errors.Add($"{what}: row {y} has value '{token}'; {min} to {max} allowed");
                    return null;
                }
                int n = 1;
                if (star >= 0 && (!int.TryParse(token[(star + 1)..], out n) || n < 1))
                {
                    errors.Add($"{what}: row {y} has a bad run '{token}'");
                    return null;
                }
                if (x + n > w)
                    break;
                Array.Fill(cells, v, y * w + x, n);
                x += n;
            }
            if (x != w)
            {
                errors.Add($"{what}: row {y} is not {w} cells long");
                return null;
            }
        }
        return cells;
    }
}

/// <summary>
/// A generic head start (MP-008, OQ-47) from <c>head_starts.json</c>: prefabs stamped beside a seat's first start prefab
/// (or its first core, on a scenario), and items added to that core. The host picks one per AI seat
/// (<see cref="GameSetup.AiHeadStarts"/>).
/// </summary>
public sealed record HeadStartDef(string Id, string Name, string[]? Prefabs = null, SortedDictionary<string, int>? Items = null, string? Description = null);

/// <summary>Scenario files: parsing, writing, hashing and the checks that don't need a world (WM-017).</summary>
public static class Scenarios
{
    static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public static ScenarioDef Parse(string json) =>
        JsonSerializer.Deserialize<ScenarioDef>(json, ReadOptions) ?? throw new JsonException("expected a scenario object");

    /// <summary>The scenario as an indented file, one cell row per line.</summary>
    public static string ToJson(ScenarioDef def) => JsonSerializer.Serialize(def, WriteOptions);

    /// <summary>FNV-1a of the scenario written back out, so formatting and comments in the file don't count.</summary>
    public static ulong Hash(ScenarioDef def)
    {
        ulong h = 14695981039346656037UL;
        foreach (byte b in JsonSerializer.SerializeToUtf8Bytes(def, WriteOptions))
        {
            h ^= b;
            h *= 1099511628211UL;
        }
        return h;
    }

    internal static short[] DecodeElevation(string text, int cells)
    {
        using var deflate = new DeflateStream(new MemoryStream(Convert.FromBase64String(text)), CompressionMode.Decompress);
        using var raw = new MemoryStream();
        deflate.CopyTo(raw);
        var bytes = raw.ToArray();
        if (bytes.Length != cells * 2)
            throw new InvalidDataException($"elevation has {bytes.Length / 2} cells, expected {cells}");
        var e = new short[cells];
        for (int i = 0; i < cells; i++)
            e[i] = (short)(bytes[2 * i] | bytes[2 * i + 1] << 8);
        return e;
    }

    internal static string EncodeElevation(Func<int, int> at, int cells)
    {
        var bytes = new byte[cells * 2];
        for (int i = 0; i < cells; i++)
        {
            int v = at(i);
            (bytes[2 * i], bytes[2 * i + 1]) = ((byte)v, (byte)(v >> 8));
        }
        using var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.SmallestSize))
            deflate.Write(bytes);
        return Convert.ToBase64String(buffer.ToArray());
    }

    /// <summary>
    /// Everything wrong with a scenario: the file's own checks here, then a trial build of its world
    /// (<see cref="Simulation.LoadScenario"/>), which checks cell maps, layers and every seat's assets against the rules.
    /// </summary>
    internal static void Validate(ContentDb content, ScenarioDef s, List<string> errors)
    {
        string at = $"scenario '{s.Id}'";
        int before = errors.Count;
        if (string.IsNullOrWhiteSpace(s.Id))
            errors.Add("scenario with no id");
        if (string.IsNullOrWhiteSpace(s.Name))
            errors.Add($"{at}: no name");
        if (s.Width <= 0 || s.Height <= 0)
            errors.Add($"{at}: size {s.Width}x{s.Height}");
        if (s.Terrain is null || s.TileMap is null || s.Tiles is not { Count: > 0 } || s.Seats is null)
            errors.Add($"{at}: needs terrain, tileMap, tiles and seats");
        else if (s.Seats.Count is < 1 or > GameSetup.MaxPlayers)
            errors.Add($"{at}: {s.Seats.Count} seats; 1 to {GameSetup.MaxPlayers} allowed");
        foreach (var f in s.Seats?.Select(x => x?.Faction).Where(f => f is { Length: > 0 } && !content.Factions.ContainsKey(f)) ?? [])
            errors.Add($"{at}: unknown faction '{f}'");
        if (errors.Count > before)
            return;
        try
        {
            new Simulation(content).LoadScenario(s);
        }
        catch (ContentException e)
        {
            errors.AddRange(e.Errors);
        }
    }
}
