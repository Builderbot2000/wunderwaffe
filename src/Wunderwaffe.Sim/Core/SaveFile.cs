using System.Text.Json;
using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.Core;

public sealed class SaveException(string message) : Exception(message);

/// <summary>
/// A saved match (NF-002): its setup and every command applied, which replay to the same state (NF-004), plus the
/// match's latest periodic snapshot (NF-008) so a load only replays the commands after it.
/// <see cref="Hash"/> is the state hash at save time; a replay that ends elsewhere was saved with different rules
/// or content.
/// </summary>
/// <param name="Label">Free text for the load menu, such as progress at save time.</param>
/// <param name="Snapshot">State at a tick no later than <paramref name="Tick"/>, or null for a full replay.</param>
public sealed record SaveFile(int Version, GameSetup Setup, long Tick, ulong Hash, string Label, List<LoggedCommand> Commands, Snapshot? Snapshot = null)
{
    /// <summary>
    /// 9: building needs a build radius (WM-015), so old command logs replay differently. 8: the setup names a scenario
    /// (with its hash) or a seed, and AI head starts (WM-017, MP-008). No compatibility
    /// with older saves (OQ-34).
    /// </summary>
    public const int CurrentVersion = 10;

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static SaveFile From(Simulation sim, string label = "")
    {
        if (sim.Setup is not { } setup)
            throw new SaveException("only matches started with Simulation.NewGame can be saved");
        return new SaveFile(CurrentVersion, setup, sim.Tick, sim.StateHash(), label, [.. sim.CommandLog], sim.LatestSnapshot);
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
/// Rebuilds a saved match: from its snapshot, replaying only the commands after it (NF-008), or from its setup,
/// replaying everything. <see cref="Advance"/> runs a slice at a time so a loading screen can show progress. If a
/// snapshot load ends away from the saved hash (state the snapshot doesn't capture), it starts again as a full replay.
/// </summary>
public sealed class Replay
{
    readonly ContentDb _content;
    readonly SaveFile _save;
    int _next;

    /// <param name="useSnapshot">False forces a full replay from the setup, ignoring the save's snapshot.</param>
    public Replay(ContentDb content, SaveFile save, bool useSnapshot = true)
    {
        // A missing or edited scenario, or content without the save's prefabs, can't rebuild the match.
        if (save.Setup.Problem(content) is { } problem)
            throw new SaveException($"can't rebuild this match: {problem}");
        _content = content;
        _save = save;
        if (useSnapshot && save.Snapshot is { } snap && snap.Tick <= save.Tick)
        {
            FromSnapshot = true;
            _next = save.Commands.FindIndex(c => c.Tick >= snap.Tick);
            if (_next < 0)
                _next = save.Commands.Count;
            Sim = snap.Restore(content, save.Setup, save.Commands.Take(_next));
            FallBackIfDiverged();
        }
        else
        {
            Sim = Simulation.NewGame(content, save.Setup);
        }
    }

    public Simulation Sim { get; private set; }
    public long TotalTicks => _save.Tick;
    public bool Done => Sim.Tick >= _save.Tick;

    /// <summary>Started from the save's snapshot and still on it (not fallen back to a full replay).</summary>
    public bool FromSnapshot { get; private set; }

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
        FallBackIfDiverged();
    }

    void FallBackIfDiverged()
    {
        if (Done && FromSnapshot && !Matches)
        {
            FromSnapshot = false;
            _next = 0;
            Sim = Simulation.NewGame(_content, _save.Setup);
        }
    }

    /// <summary>Loads the whole save. Throws if it doesn't reach the saved state.</summary>
    public static Simulation Load(ContentDb content, SaveFile save, bool useSnapshot = true)
    {
        var replay = new Replay(content, save, useSnapshot);
        while (!replay.Done)
            replay.Advance(int.MaxValue);
        if (!replay.Matches)
            throw new SaveException("replay did not reach the saved state");
        return replay.Sim;
    }
}
