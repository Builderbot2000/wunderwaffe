using Wunderwaffe.Sim.Core;

namespace Wunderwaffe.Sim.World.Gen;

/// <summary>
/// Integer value noise: random values on a lattice, blended with a smoothstep. Integer so the same seed gives the same
/// world on every machine (WM-009, NF-004).
/// </summary>
internal static class Noise
{
    public const int Max = 65535;
    const int Frac = 1024;

    static int Lattice(int x, int y, ulong seed)
    {
        ulong z = seed ^ ((ulong)(uint)x * 0x9E3779B97F4A7C15UL) ^ ((ulong)(uint)y * 0xC2B2AE3D27D4EB4FUL);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return (int)((z ^ (z >> 31)) >> 48);
    }

    static int Smooth(int t) => (int)((long)t * t * (3 * Frac - 2 * t) / ((long)Frac * Frac));

    /// <summary>Noise in [0, <see cref="Max"/>] with lattice points <paramref name="period"/> cells apart.</summary>
    public static int Value(int x, int y, int period, ulong seed)
    {
        int ix = IntMath.FloorDiv(x, period), iy = IntMath.FloorDiv(y, period);
        int fx = Smooth((x - ix * period) * Frac / period), fy = Smooth((y - iy * period) * Frac / period);
        long a = Lattice(ix, iy, seed), b = Lattice(ix + 1, iy, seed);
        long c = Lattice(ix, iy + 1, seed), d = Lattice(ix + 1, iy + 1, seed);
        long top = a + (b - a) * fx / Frac, bottom = c + (d - c) * fx / Frac;
        return (int)(top + (bottom - top) * fy / Frac);
    }

    /// <summary>Octaves of <see cref="Value"/>, each half the period and half the weight of the last. Same range.</summary>
    public static int Fractal(int x, int y, int period, int octaves, ulong seed)
    {
        long sum = 0, weight = 0;
        for (int o = 0; o < octaves; o++)
        {
            int w = 1 << (octaves - o);
            sum += (long)Value(x, y, Math.Max(2, period), seed + (ulong)o * 0x632BE59BD9B4E019UL) * w;
            weight += w;
            period /= 2;
        }
        return (int)(sum / weight);
    }
}
