using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim.World.Gen;

/// <summary>
/// Names tiles in one made-up language per world: the seed picks a small set of sounds and an ending or two, and every
/// name is built only from those, so all names on a map sound alike while two worlds sound different. Names are unique
/// within a world and carry no number or terrain; those are stats on the tile.
/// </summary>
public sealed class TileNames
{
    static readonly string[] AllOnsets =
        ["b", "d", "f", "g", "h", "k", "l", "m", "n", "p", "r", "s", "t", "v", "z", "th", "sh", "br", "dr", "gr", "kr", "tr", "st", "vr"];
    static readonly string[] AllVowels = ["a", "e", "i", "o", "u", "ae", "ei", "au", "y"];
    /// <summary>Codas that may close a syllable before another consonant without an ugly cluster.</summary>
    static readonly string[] AllMedialCodas = ["n", "l", "r", "s", "m"];
    static readonly string[] AllFinals = ["n", "r", "l", "s", "th", "nd", "rn", "st", "x", "m"];
    static readonly string[] AllEndings = ["ia", "or", "en", "ar", "heim", "ach", "un", "is", "ora", "ad", "el", "os"];

    const int MinLength = 4, MaxLength = 11;

    readonly Rng _rng;
    readonly string[] _onsets, _vowels, _medials, _finals, _endings;
    readonly int _finalPercent, _endingPercent, _threeSyllablePercent;
    readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);

    public TileNames(ulong seed)
    {
        _rng = new Rng(seed ^ 0x6A09E667F3BCC909UL);
        _onsets = Pick(AllOnsets, 7, 10);
        _vowels = Pick(AllVowels, 3, 5);
        _medials = Pick(AllMedialCodas, 1, 3);
        _finals = Pick(AllFinals, 2, 4);
        _endings = Pick(AllEndings, 1, 3);
        _finalPercent = _rng.Range(20, 60);
        _endingPercent = _rng.Range(15, 40);
        _threeSyllablePercent = _rng.Range(15, 40);
    }

    /// <summary>A name not handed out before by this instance.</summary>
    public string Next()
    {
        for (int attempt = 0; ; attempt++)
        {
            bool crowded = attempt >= 200;
            string name = Build(crowded ? 1 : 0);
            if ((crowded || name.Length is >= MinLength and <= MaxLength) && _used.Add(name))
                return char.ToUpperInvariant(name[0]) + name[1..];
        }
    }

    /// <summary>One candidate; <paramref name="extra"/> syllables widen the space once short names run out.</summary>
    string Build(int extra)
    {
        int syllables = (_rng.Range(0, 100) < _threeSyllablePercent ? 3 : 2) + extra;
        bool ending = _rng.Range(0, 100) < _endingPercent;
        if (ending)
            syllables--;

        var sb = new System.Text.StringBuilder();
        string? prev = null;
        for (int i = 0; i < syllables; i++)
        {
            // A name may open on a bare vowel; later syllables always have an onset so vowels never collide.
            if (i > 0 || _rng.Range(0, 100) >= 20)
                sb.Append(Onset(sb));
            string vowel = Of(_vowels);
            if (vowel == prev)
                vowel = Of(_vowels);
            sb.Append(vowel);
            prev = vowel;
            bool last = i == syllables - 1;
            if (!last && _rng.Range(0, 100) < 25)
                sb.Append(Of(_medials));
            else if (last && !ending && _rng.Range(0, 100) < _finalPercent)
                sb.Append(Of(_finals));
        }
        if (ending)
        {
            string e = Of(_endings);
            // Endings starting with a vowel need a consonant before them.
            if ("aeiouy".Contains(e[0]) && "aeiouy".Contains(sb[^1]))
                sb.Append(Onset(sb));
            sb.Append(e);
        }
        return sb.ToString();
    }

    string Of(string[] set) => set[_rng.Range(0, set.Length)];

    /// <summary>An onset that doesn't double the letter before it ("mm", "rr"), where the language allows.</summary>
    string Onset(System.Text.StringBuilder sb)
    {
        string onset = Of(_onsets);
        for (int i = 0; i < 4 && sb.Length > 0 && onset[0] == sb[^1]; i++)
            onset = Of(_onsets);
        return onset;
    }

    /// <summary>Between <paramref name="min"/> and <paramref name="max"/> distinct entries of <paramref name="all"/>, in order.</summary>
    string[] Pick(string[] all, int min, int max)
    {
        var pool = all.ToList();
        int n = Math.Min(pool.Count, _rng.Range(min, max + 1));
        var chosen = new List<string>();
        for (int i = 0; i < n; i++)
        {
            int j = _rng.Range(0, pool.Count);
            chosen.Add(pool[j]);
            pool.RemoveAt(j);
        }
        return [.. chosen];
    }
}
