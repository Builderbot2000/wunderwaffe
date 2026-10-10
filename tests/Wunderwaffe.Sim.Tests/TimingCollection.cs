namespace Wunderwaffe.Sim.Tests;

/// <summary>
/// Tests that measure wall time. They run on their own, after the parallel tests, so other tests (world generation
/// uses every core) don't skew the numbers.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingCollection
{
    public const string Name = "Timing";
}
