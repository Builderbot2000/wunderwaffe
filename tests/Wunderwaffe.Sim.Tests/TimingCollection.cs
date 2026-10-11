using System.Runtime.InteropServices;

namespace Wunderwaffe.Sim.Tests;

/// <summary>
/// Tests that measure wall time. They run on their own, after the parallel tests, so other tests (world generation
/// uses every core) don't skew the numbers. Each calls <see cref="Timing.PreferFastCores"/> before it measures.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingCollection
{
    public const string Name = "Timing";
}

/// <summary>Keeps wall-time budgets about the code, not about where Windows happens to run the test host.</summary>
static class Timing
{
    /// <summary>
    /// Opts the test process and the calling thread out of Windows power throttling (EcoQoS). On a hybrid CPU (P and E
    /// cores) Windows otherwise moves a background console process such as the test host onto efficiency cores for
    /// seconds at a time, which made the same ticks take 2 to 2.5 times as long at random points of a run (measured:
    /// 0.87 ms/tick on a P core, 2.1 to 2.5 on an E core, with identical work and allocations). With the opt-out the
    /// stress tests read the same alone and in the full suite. No effect elsewhere.
    /// </summary>
    public static void PreferFastCores()
    {
        if (!OperatingSystem.IsWindows())
            return;
        // PROCESS_POWER_THROTTLING_STATE / THREAD_POWER_THROTTLING_STATE: version 1, control EXECUTION_SPEED, state off.
        var state = new ThrottlingState(1, 1, 0);
        SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state, Marshal.SizeOf<ThrottlingState>());
        SetThreadInformation(GetCurrentThread(), ThreadPowerThrottling, ref state, Marshal.SizeOf<ThrottlingState>());
    }

    const int ProcessPowerThrottling = 4, ThreadPowerThrottling = 3;

    [StructLayout(LayoutKind.Sequential)]
    record struct ThrottlingState(uint Version, uint ControlMask, uint StateMask);

    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll")]
    static extern bool SetProcessInformation(IntPtr process, int infoClass, ref ThrottlingState info, int size);

    [DllImport("kernel32.dll")]
    static extern bool SetThreadInformation(IntPtr thread, int infoClass, ref ThrottlingState info, int size);
}
