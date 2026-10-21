namespace Wunderwaffe.Sim.Ai.Modules;

/// <summary>
/// One concern of the AI (MP-006): looks at the think's <see cref="AiContext"/> and offers scored
/// <see cref="Proposal"/>s, or none. Modules keep no state, so they can be switched off or re-weighted per seat
/// (<see cref="AiConfig"/>) and one with nothing to offer never holds the others back.
/// </summary>
public interface IAiModule
{
    /// <summary>Stable name: the key for <see cref="AiConfig.Weights"/> and the debug log.</summary>
    string Name { get; }

    IEnumerable<Proposal> Propose(AiContext c);
}

/// <summary>The standard module list, in tie-break order.</summary>
public static class AiModules
{
    public static IReadOnlyList<IAiModule> Default() =>
        [new EconomyModule(), new ExpansionModule(), new ProductionModule(), new DefenceModule(), new AttackModule(), new ClaimModule()];
}
