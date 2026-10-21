using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim;

/// <summary>
/// Everything needed to build a match's starting state (MP-005). With the command log, it is all a save holds:
/// the same setup and commands replay to the same state (NF-002, NF-004). <see cref="Width"/> and <see cref="Height"/>
/// are the world's size in nominal tiles; the generated tiles are irregular, so their count is only about Width x Height.
/// <see cref="Players"/> players, ids 0 to Players - 1, each start on their own tile with a core.
/// </summary>
/// <param name="AiPlayers">How many of the players are game AI: always the last ids (MP-002). The host runs them.</param>
/// <param name="CaptureSeconds">How long a building must hold a victory circle to claim the tile (WM-006).</param>
/// <param name="VictoryPercent">Share of all tiles, in percent, that wins the match (MP-001).</param>
/// <param name="RichnessPercent">Deposit richness relative to normal (MP-005); 100 is the content as written.</param>
/// <param name="StartPrefabs">
/// Prefab ids stamped on each seat's starting tile (FB-021), indexed by player: the first nearest the victory circle
/// centre, the rest beside it (an AI head start, OQ-47). A missing or empty entry gets <see cref="DefaultStart"/>.
/// </param>
/// <param name="Scenario">
/// The scenario the world comes from (WM-017), or null to generate it from <see cref="Seed"/>. With a scenario,
/// <see cref="Width"/>, <see cref="Height"/>, <see cref="Seed"/> and <see cref="RichnessPercent"/> are unused and
/// <see cref="Players"/> is its seat count.
/// </param>
/// <param name="ScenarioHash">The scenario's <see cref="ScenarioDef.Hash"/> when the match began: a changed file won't replay.</param>
/// <param name="AiHeadStarts">
/// Head-start preset id per AI seat, in seat order (MP-008: the first AI seat first); null or "" for none.
/// </param>
/// <param name="Factions">
/// Faction id per seat (MP-009), <see cref="RandomFaction"/> for one picked from the seed, null or "" for the default.
/// A scenario seat with a faction of its own ignores it.
/// </param>
/// <param name="SitesPercent">
/// Neutral site density (WM-018, MP-005): percent of a generated world's tiles that get a site, 0 for none. Scenarios
/// carry their own sites; content without site templates places none.
/// </param>
public sealed record GameSetup(
    int Width, int Height, ulong Seed, int Players = 1,
    int AiPlayers = 0, int CaptureSeconds = Balance.CaptureSeconds, int VictoryPercent = 60, int RichnessPercent = 100,
    string[][]? StartPrefabs = null, string? Scenario = null, ulong ScenarioHash = 0, string?[]? AiHeadStarts = null,
    string?[]? Factions = null, int SitesPercent = GameSetup.DefaultSitesPercent)
{
    /// <summary>The lobby's Random faction choice: resolved from the seed and seat, so every machine picks the same.</summary>
    public const string RandomFaction = "random";

    /// <summary>
    /// The faction <paramref name="player"/> plays (MP-009): the scenario seat's own, else the setup's pick (Random
    /// resolved from <see cref="Seed"/>, <see cref="ScenarioHash"/> and the seat among the factions not hidden), else
    /// the default.
    /// </summary>
    public FactionDef FactionFor(int player, ContentDb content)
    {
        if (Scenario is { } sc && content.Scenarios.TryGetValue(sc, out var def) && player < def.Seats.Count
            && def.Seats[player].Faction is { Length: > 0 } fixedId && content.Factions.TryGetValue(fixedId, out var fixedFaction))
            return fixedFaction;
        string? id = Factions is { } a && player < a.Length ? a[player] : null;
        if (id == RandomFaction)
        {
            var pick = content.FactionList.Where(f => !f.Hidden).ToList();
            var rng = new Core.Rng(Seed ^ ScenarioHash ^ (0x9E3779B97F4A7C15UL * (ulong)(player + 1)));
            return pick.Count == 0 ? content.DefaultFaction : pick[rng.Range(0, pick.Count)];
        }
        return id is { Length: > 0 } && content.Factions.TryGetValue(id, out var f) ? f : content.DefaultFaction;
    }

    /// <summary>The prefabs <paramref name="player"/> starts with, the first in the victory circle: the setup's, else their faction's, else <see cref="DefaultStart"/>.</summary>
    public IReadOnlyList<string> PrefabsFor(int player, ContentDb content) =>
        StartPrefabs is { } s && player < s.Length && s[player] is { Length: > 0 } p ? p
        : FactionFor(player, content).StartPrefabs is { Length: > 0 } fp ? fp
        : [DefaultStart];

    /// <summary>The prefab every seat starts with unless the setup names others: the landed scout (WM-005).</summary>
    public const string DefaultStart = "scout";

    public const int MinSize = 3;
    public const int MaxSize = 7;
    public const int MaxPlayers = 8;
    public const int MinCaptureSeconds = 5, MaxCaptureSeconds = 600;
    public const int MinVictoryPercent = 10, MaxVictoryPercent = 100;
    public const int MinRichness = 25, MaxRichness = 400;
    public const int DefaultSitesPercent = 33, MaxSitesPercent = 50;

    /// <summary>True if <paramref name="player"/> is one of the AI seats.</summary>
    public bool IsAi(int player) => player >= Players - AiPlayers && player < Players;

    /// <summary>The head-start preset <paramref name="player"/> gets (MP-008), or null.</summary>
    public string? HeadStartFor(int player) =>
        IsAi(player) && AiHeadStarts is { } a && player - (Players - AiPlayers) is var i && i < a.Length && a[i] is { Length: > 0 } id ? id : null;

    /// <summary>A setup for a scenario: its seats are the players, the last <paramref name="aiPlayers"/> of them AI.</summary>
    public static GameSetup ForScenario(ScenarioDef scenario, int aiPlayers = 0) =>
        Default with { Seed = 0, Players = scenario.Seats.Count, AiPlayers = aiPlayers, Scenario = scenario.Id, ScenarioHash = scenario.Hash };

    /// <summary>Why the setup can't start a match, or null if it can.</summary>
    public string? Problem() =>
        Scenario == null && (Width is < MinSize or > MaxSize || Height is < MinSize or > MaxSize) ? $"map size {Width}x{Height}; {MinSize} to {MaxSize} allowed"
        : Players is < 1 or > MaxPlayers ? $"{Players} players; 1 to {MaxPlayers} allowed"
        : AiPlayers < 0 || AiPlayers > Players ? $"{AiPlayers} AI players of {Players}"
        : CaptureSeconds is < MinCaptureSeconds or > MaxCaptureSeconds ? $"capture time {CaptureSeconds} s; {MinCaptureSeconds} to {MaxCaptureSeconds} allowed"
        : VictoryPercent is < MinVictoryPercent or > MaxVictoryPercent ? $"victory share {VictoryPercent}%; {MinVictoryPercent} to {MaxVictoryPercent} allowed"
        : RichnessPercent is < MinRichness or > MaxRichness ? $"richness {RichnessPercent}%; {MinRichness} to {MaxRichness} allowed"
        : SitesPercent is < 0 or > MaxSitesPercent ? $"neutral sites {SitesPercent}%; 0 to {MaxSitesPercent} allowed"
        : StartPrefabs is { } s && s.Length > Players ? $"start prefabs for {s.Length} seats of {Players}"
        : AiHeadStarts is { } h && h.Length > AiPlayers ? $"head starts for {h.Length} AI seats of {AiPlayers}"
        : Factions is { } f && f.Length > Players ? $"factions for {f.Length} seats of {Players}"
        : null;

    /// <summary>
    /// Why the setup can't start a match with this content, or null: <see cref="Problem()"/>, an unknown prefab, head
    /// start or faction, a start prefab or head start using what the seat's faction can't build, or a scenario that is
    /// missing, changed since the setup was made, or has another seat count.
    /// </summary>
    public string? Problem(ContentDb content) =>
        Problem()
        ?? (Factions ?? []).Where(id => id is { Length: > 0 } && id != RandomFaction && !content.Factions.ContainsKey(id)).Select(id => $"unknown faction '{id}'")
            .Concat(Enumerable.Range(0, Players).SelectMany(p => PrefabsFor(p, content)).Where(id => !content.Prefabs.ContainsKey(id))
            .Select(id => $"unknown start prefab '{id}'"))
            .Concat((AiHeadStarts ?? []).Where(id => id is { Length: > 0 } && !content.HeadStarts.ContainsKey(id)).Select(id => $"unknown head start '{id}'"))
            .FirstOrDefault()
        ?? ScenarioProblem(content)
        ?? Enumerable.Range(0, Players).Select(p => FactionProblem(p, content)).FirstOrDefault(x => x != null);

    /// <summary>A start prefab or head-start prefab of the seat's that its faction can't build (MP-009), or null.</summary>
    string? FactionProblem(int player, ContentDb content)
    {
        var f = FactionFor(player, content);
        var prefabs = PrefabsFor(player, content)
            .Concat(HeadStartFor(player) is { } h ? content.HeadStarts[h].Prefabs ?? [] : []);
        foreach (var id in prefabs)
        {
            if (FactionDef.Misfit(content, f, content.Prefabs[id]) is { } bad)
                return $"seat {player}'s faction '{f.Id}' can't build '{bad}' of prefab '{id}'";
        }
        return null;
    }

    string? ScenarioProblem(ContentDb content) =>
        Scenario == null ? null
        : !content.Scenarios.TryGetValue(Scenario, out var s) ? $"unknown scenario '{Scenario}'"
        : s.Hash != ScenarioHash ? $"scenario '{Scenario}' has changed since this match was set up"
        : s.Seats.Count != Players ? $"scenario '{Scenario}' has {s.Seats.Count} seats, not {Players}"
        : null;

    public static readonly GameSetup Default = new(5, 5, 3037);

    /// <summary>A seed typed by the player: a number is used as is, any other text is hashed.</summary>
    public static ulong ParseSeed(string text)
    {
        text = text.Trim();
        if (ulong.TryParse(text, out ulong n))
            return n;
        ulong h = 14695981039346656037UL;
        foreach (char c in text)
        {
            h ^= c;
            h *= 1099511628211UL;
        }
        return h;
    }
}
