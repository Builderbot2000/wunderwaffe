using System.Text.Json;
using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.Core;

public sealed class SaveException(string message) : Exception(message);

/// <summary>
/// A saved match (NF-002): its setup and every command applied, which replay to the same state (NF-004).
/// <see cref="Hash"/> is the state hash at save time; a replay that ends elsewhere was saved with different rules
/// or content.
/// </summary>
/// <param name="Label">Free text for the load menu, such as progress at save time.</param>
public sealed record SaveFile(int Version, GameSetup Setup, long Tick, ulong Hash, string Label, List<LoggedCommand> Commands)
{
    public const int CurrentVersion = 1;

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static SaveFile From(Simulation sim, string label = "")
    {
        if (sim.Setup is not { } setup)
            throw new SaveException("only matches started with Simulation.NewGame can be saved");
        return new SaveFile(CurrentVersion, setup, sim.Tick, sim.StateHash(), label, [.. sim.CommandLog]);
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static SaveFile FromJson(string json)
    {
        SaveFile? save;
        try
        {
            save = JsonSerializer.Deserialize<SaveFile>(json, Options);
        }
        catch (JsonException e)
        {
            throw new SaveException($"not a save file: {e.Message}");
        }
        if (save?.Setup == null || save.Commands == null)
            throw new SaveException("not a save file");
        if (save.Version != CurrentVersion)
            throw new SaveException($"save version {save.Version}, this build reads {CurrentVersion}");
        return save;
    }
}

/// <summary>
/// Rebuilds a saved match by replaying its commands from its setup. <see cref="Advance"/> runs a slice at a time
/// so a loading screen can show progress.
/// </summary>
public sealed class Replay
{
    readonly SaveFile _save;
    int _next;

    public Replay(ContentDb content, SaveFile save, int player = 0)
    {
        _save = save;
        Sim = Simulation.NewGame(content, save.Setup, player);
    }

    public Simulation Sim { get; }
    public long TotalTicks => _save.Tick;
    public bool Done => Sim.Tick >= _save.Tick;

    /// <summary>True once done if the replay reached exactly the saved state.</summary>
    public bool Matches => Done && Sim.StateHash() == _save.Hash;

    /// <summary>Runs up to <paramref name="maxTicks"/> more ticks, feeding each saved command in on its tick.</summary>
    public void Advance(int maxTicks)
    {
        var log = _save.Commands;
        for (int i = 0; i < maxTicks && !Done; i++)
        {
            while (_next < log.Count && log[_next].Tick == Sim.Tick)
                Sim.Enqueue(log[_next++].Command);
            Sim.Step();
        }
    }

    /// <summary>Replays the whole save. Throws if it doesn't reach the saved state.</summary>
    public static Simulation Load(ContentDb content, SaveFile save)
    {
        var replay = new Replay(content, save);
        replay.Advance(int.MaxValue);
        if (!replay.Matches)
            throw new SaveException("replay did not reach the saved state");
        return replay.Sim;
    }
}
