namespace Wunderwaffe.Sim;

/// <summary>
/// Everything needed to build a match's starting state (MP-005). With the command log, it is all a save holds:
/// the same setup and commands replay to the same state (NF-002, NF-004).
/// </summary>
public sealed record GameSetup(int Width, int Height, ulong Seed)
{
    public const int MinSize = 3;
    public const int MaxSize = 15;

    public static readonly GameSetup Default = new(5, 5, 3037);

    /// <summary>The local player's first tile: the middle of the map.</summary>
    public (int X, int Y) StartTile => (Width / 2, Height / 2);

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
