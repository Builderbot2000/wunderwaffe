using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim.Api;

/// <summary>
/// The shared command interface as text (NF-009): one JSON request per line in, one JSON response per line out, so a
/// program in any language can play through the same commands and queries as the GUI and the AI. The CLI
/// (<c>src/Wunderwaffe.Cli</c>) wires it to stdin and stdout.
/// <para>Requests, by <c>op</c>:</para>
/// <list type="bullet">
/// <item><c>{"op":"new","setup":{"Width":5,"Height":5,"Seed":1},"player":0}</c>: starts a match; this driver plays
/// <c>player</c>.</item>
/// <item><c>{"op":"query","name":"Tile","args":{"tileId":3}}</c>: calls that <see cref="GameQuery"/> method or
/// property (names and arguments case-insensitive); <c>{"op":"queries"}</c> lists them.</item>
/// <item><c>{"op":"command","command":{"type":"place",...}}</c>: enqueues a command (save-file JSON, same tags) for
/// the next tick; it must be for this driver's player.</item>
/// <item><c>{"op":"step","ticks":60}</c>: runs the sim on.</item>
/// </list>
/// Responses are <c>{"ok":true,"result":...}</c> or <c>{"ok":false,"error":"..."}</c>. Enums travel as names.
/// </summary>
public sealed class JsonDriver(ContentDb content)
{
    /// <summary>Most ticks one step request may run.</summary>
    public const int MaxStep = 60 * 60 * Simulation.TicksPerSecond;

    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    Simulation? _sim;
    int _player;

    /// <summary>The match being played, once a <c>new</c> request started one.</summary>
    public Simulation? Sim => _sim;

    /// <summary>Handles one request line and returns the response line.</summary>
    public string Handle(string line)
    {
        try
        {
            var request = JsonNode.Parse(line)?.AsObject() ?? throw new DriverException("empty request");
            object? result = (string?)request["op"] switch
            {
                "new" => New(request),
                "query" => Query(request),
                "queries" => Queries(),
                "command" => Command(request),
                "step" => Step(request),
                var op => throw new DriverException($"unknown op '{op}'"),
            };
            return JsonSerializer.Serialize(new { ok = true, result }, Options);
        }
        catch (Exception e) when (e is DriverException or JsonException or InvalidOperationException or FormatException
                                       or ArgumentException or TargetInvocationException)
        {
            string error = (e as TargetInvocationException)?.InnerException?.Message ?? e.Message;
            return JsonSerializer.Serialize(new { ok = false, error }, Options);
        }
    }

    /// <summary>Reads requests from <paramref name="input"/> until it ends, answering each on <paramref name="output"/>.</summary>
    public void Run(TextReader input, TextWriter output)
    {
        while (input.ReadLine() is { } line)
        {
            if (line.Trim().Length == 0)
                continue;
            output.WriteLine(Handle(line));
            output.Flush();
        }
    }

    Simulation Need() => _sim ?? throw new DriverException("no match: send {\"op\":\"new\"} first");

    object New(JsonObject request)
    {
        var setup = request["setup"]?.Deserialize<GameSetup>(Options) ?? GameSetup.Default;
        if (setup.Problem() is { } problem)
            throw new DriverException(problem);
        int player = request["player"]?.GetValue<int>() ?? 0;
        if (player < 0 || player >= setup.Players)
            throw new DriverException($"player {player} isn't in a {setup.Players}-player match");
        _sim = Simulation.NewGame(content, setup);
        _player = player;
        return new GameQuery(_sim, _player).Match();
    }

    object? Command(JsonObject request)
    {
        var sim = Need();
        var command = request["command"].Deserialize<Command>(Options) ?? throw new DriverException("no command");
        if (command.Player != _player)
            throw new DriverException($"this driver plays player {_player}, not {command.Player}");
        sim.Enqueue(command);
        return null;
    }

    object Step(JsonObject request)
    {
        var sim = Need();
        int ticks = request["ticks"]?.GetValue<int>() ?? 1;
        if (ticks < 0 || ticks > MaxStep)
            throw new DriverException($"ticks must be 0 to {MaxStep}");
        for (int i = 0; i < ticks; i++)
            sim.Step();
        return new { sim.Tick };
    }

    static readonly MemberInfo[] Members =
    [
        .. typeof(GameQuery).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName),
        .. typeof(GameQuery).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(p => p.Name != "Content"),
    ];

    static object Queries() => Members.Select(m => m is MethodInfo mi
            ? $"{mi.Name}({string.Join(", ", mi.GetParameters().Select(p => p.Name + (p.HasDefaultValue ? "?" : "")))})"
            : m.Name)
        .Order(StringComparer.Ordinal).ToList();

    object? Query(JsonObject request)
    {
        var q = new GameQuery(Need(), _player);
        string name = (string?)request["name"] ?? throw new DriverException("query needs a name");
        var args = request["args"]?.AsObject() ?? [];
        var found = Members.Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (found.Count == 0)
            throw new DriverException($"unknown query '{name}'; see {{\"op\":\"queries\"}}");
        if (found[0] is PropertyInfo prop)
            return prop.GetValue(q);

        var given = args.ToDictionary(kv => kv.Key.ToLowerInvariant(), kv => kv.Value);
        foreach (var method in found.Cast<MethodInfo>())
        {
            var ps = method.GetParameters();
            if (given.Keys.Any(k => ps.All(p => p.Name!.ToLowerInvariant() != k)))
                continue;
            if (ps.Any(p => !p.HasDefaultValue && !given.ContainsKey(p.Name!.ToLowerInvariant())))
                continue;
            var values = ps.Select(p => given.TryGetValue(p.Name!.ToLowerInvariant(), out var v)
                ? v.Deserialize(p.ParameterType, Options)
                : p.DefaultValue).ToArray();
            return method.Invoke(q, values);
        }
        var shapes = found.Cast<MethodInfo>().Select(m => $"{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.Name))})");
        throw new DriverException($"arguments don't fit {string.Join(" or ", shapes)}");
    }
}

/// <summary>A request the driver can't carry out; its message goes back to the caller.</summary>
public sealed class DriverException(string message) : Exception(message);
