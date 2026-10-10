namespace Wunderwaffe.Sim.Core;

/// <summary>Integer helpers shared by the deterministic sim (NF-004).</summary>
public static class IntMath
{
    /// <summary>Division rounding toward negative infinity, for coordinates that can be negative.</summary>
    public static long FloorDiv(long a, long b) => a >= 0 ? a / b : -((-a + b - 1) / b);

    public static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

    /// <summary>Floor of the square root, by Newton's method.</summary>
    public static long ISqrt(long n)
    {
        if (n < 2)
            return n;
        long x = n, y = (x + 1) / 2;
        while (y < x)
        {
            x = y;
            y = (x + n / x) / 2;
        }
        return x;
    }
}
