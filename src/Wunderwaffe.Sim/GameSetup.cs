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
public sealed record GameSetup(
    int Width, int Height, ulong Seed, int Players = 1,
    int AiPlayers = 0, int CaptureSeconds = 30, int VictoryPercent = 60, int RichnessPercent = 100)
{
    public const int MinSize = 3;
    public const int MaxSize = 15;
    public const int MaxPlayers = 8;
    public const int MinCaptureSeconds = 5, MaxCaptureSeconds = 600;
    public const int MinVictoryPercent = 10, MaxVictoryPercent = 100;
    public const int MinRichness = 25, MaxRichness = 400;

    /// <summary>True if <paramref name="player"/> is one of the AI seats.</summary>
    public bool IsAi(int player) => player >= Players - AiPlayers && player < Players;

    /// <summary>Why the setup can't start a match, or null if it can.</summary>
    public string? Problem() =>
        Width is < MinSize or > MaxSize || Height is < MinSize or > MaxSize ? $"map size {Width}x{Height}; {MinSize} to {MaxSize} allowed"
        : Players is < 1 or > MaxPlayers ? $"{Players} players; 1 to {MaxPlayers} allowed"
        : AiPlayers < 0 || AiPlayers > Players ? $"{AiPlayers} AI players of {Players}"
        : CaptureSeconds is < MinCaptureSeconds or > MaxCaptureSeconds ? $"capture time {CaptureSeconds} s; {MinCaptureSeconds} to {MaxCaptureSeconds} allowed"
        : VictoryPercent is < MinVictoryPercent or > MaxVictoryPercent ? $"victory share {VictoryPercent}%; {MinVictoryPercent} to {MaxVictoryPercent} allowed"
        : RichnessPercent is < MinRichness or > MaxRichness ? $"richness {RichnessPercent}%; {MinRichness} to {MaxRichness} allowed"
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
