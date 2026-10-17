using Wunderwaffe.Sim.Api;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Ai;

/// <summary>A unit design the AI builds: a named blueprint (frame cells plus blocks, one of them a core).</summary>
public sealed record AiDesign(string Name, Blueprint Blueprint);

/// <summary>
/// Where the AI's designs come from (MP-006 initialisation). The seam for the prefab library (FB-021): once
/// <see cref="GameQuery"/> lists prefabs, <see cref="Pick"/> chooses among them; until then it returns
/// <see cref="Scout"/>.
/// </summary>
public static class AiDesigns
{
    /// <summary>
    /// The starting unit's layout on a 4 x 2 frame: a thruster, a core and a thruster along the top, facing east.
    /// </summary>
    public static readonly AiDesign Scout = new("scout", new Blueprint(
        [new BlueprintBlock("thruster", 0, 0, 0), new BlueprintBlock("core", 1, 0, 0), new BlueprintBlock("thruster", 3, 0, 0)],
        [.. Enumerable.Range(0, 8).Select(i => new BlueprintCell(i % 4, i / 4))]));

    /// <summary>The unit design for a seat, chosen from what the match offers.</summary>
    public static AiDesign Pick(GameQuery q) => Scout;
}
