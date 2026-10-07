namespace Wunderwaffe.Sim.Core;

/// <summary>
/// SplitMix64. Used instead of System.Random, whose sequence isn't guaranteed stable across .NET versions,
/// so the same seed builds the same world everywhere (WM-009, NF-004).
/// </summary>
public sealed class Rng(ulong seed)
{
    ulong _state = seed;

    public ulong Next()
    {
        ulong z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform-ish int in [min, maxExclusive).</summary>
    public int Range(int min, int maxExclusive) => min + (int)(Next() % (ulong)(maxExclusive - min));
}
