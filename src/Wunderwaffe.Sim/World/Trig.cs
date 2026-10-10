namespace Wunderwaffe.Sim.World;

/// <summary>
/// Integer trigonometry for unit physics, so rotation is bit-identical on every machine (NF-004). Angles are
/// fractions of a turn in 1/<see cref="Turn"/>; 0 is east and positive turns clockwise on screen (y down), the same
/// way building rotation counts. The table is built once with CORDIC, which uses only shifts and adds.
/// </summary>
public static class Trig
{
    public const int Turn = 65536;
    public const int Quarter = Turn / 4;

    /// <summary>Sin and cos are returned scaled by this.</summary>
    public const int One = 4096;

    // CORDIC constants in 2^30 fixed point: atan(2^-i), the gain of the iterations, and pi/2 and 2*pi.
    const int Iterations = 24;
    static readonly long[] Atan =
    [
        843314857, 497837829, 263043837, 133525159, 67021687, 33543516, 16775851, 8388437,
        4194283, 2097149, 1048576, 524288, 262144, 131072, 65536, 32768,
        16384, 8192, 4096, 2048, 1024, 512, 256, 128,
    ];
    const long Gain = 652032874;
    const long HalfPi = 1686629713;
    const long TwoPi = 6746518852;

    // Table entries per turn. Indexing uses the top bits of the angle.
    const int TableBits = 10;
    const int TableSize = 1 << TableBits;
    const int TableShift = 16 - TableBits;

    static readonly int[] SinTable = new int[TableSize];
    static readonly int[] CosTable = new int[TableSize];

    static Trig()
    {
        int quarter = TableSize / 4;
        for (int i = 0; i < TableSize; i++)
        {
            int rem = i % quarter;
            var (c, s) = Rotate(rem * HalfPi / quarter);
            (CosTable[i], SinTable[i]) = (i / quarter) switch
            {
                0 => (c, s),
                1 => (-s, c),
                2 => (-c, -s),
                _ => (s, -c),
            };
        }
    }

    public static int Sin(int angle) => SinTable[(angle & (Turn - 1)) >> TableShift];

    public static int Cos(int angle) => CosTable[(angle & (Turn - 1)) >> TableShift];

    /// <summary>Angle of the vector (<paramref name="x"/>, <paramref name="y"/>), in 0..<see cref="Turn"/>.</summary>
    public static int Atan2(long y, long x)
    {
        if (x == 0 && y == 0)
            return 0;
        long half = 0;
        if (x < 0)
        {
            (x, y, half) = (-x, -y, Turn / 2);
        }

        // Scale up so the shifts below keep their precision.
        x <<= 10;
        y <<= 10;
        long z = 0;
        for (int i = 0; i < Iterations; i++)
        {
            long nx, ny;
            if (y >= 0)
            {
                (nx, ny) = (x + (y >> i), y - (x >> i));
                z += Atan[i];
            }
            else
            {
                (nx, ny) = (x - (y >> i), y + (x >> i));
                z -= Atan[i];
            }
            (x, y) = (nx, ny);
        }
        return (int)((z * Turn / TwoPi + half) & (Turn - 1));
    }

    /// <summary>Signed difference <c>to - from</c> wrapped into -Turn/2 .. Turn/2.</summary>
    public static int Diff(int to, int from) => ((to - from + Turn / 2) & (Turn - 1)) - Turn / 2;

    /// <summary>Cos and sin of <paramref name="theta"/> (0 to pi/2, 2^30 fixed point), scaled by <see cref="One"/>.</summary>
    static (int Cos, int Sin) Rotate(long theta)
    {
        long x = Gain, y = 0, z = theta;
        for (int i = 0; i < Iterations; i++)
        {
            long nx, ny;
            if (z >= 0)
            {
                (nx, ny) = (x - (y >> i), y + (x >> i));
                z -= Atan[i];
            }
            else
            {
                (nx, ny) = (x + (y >> i), y - (x >> i));
                z += Atan[i];
            }
            (x, y) = (nx, ny);
        }
        const int Shift = 30 - 12;
        return ((int)((x + (1L << (Shift - 1))) >> Shift), (int)((y + (1L << (Shift - 1))) >> Shift));
    }

    /// <summary>Rotates a vector by <paramref name="angle"/>.</summary>
    public static (long X, long Y) Rotate(long x, long y, int angle)
    {
        long c = Cos(angle), s = Sin(angle);
        return ((x * c - y * s) / One, (x * s + y * c) / One);
    }
}
