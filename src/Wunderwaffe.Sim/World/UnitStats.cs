using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// A unit's figures, derived from its blocks (UD-002). The same formulas apply to a flying unit, a design on a frame and
/// a library blueprint, so the UI can show them before anything is built.
/// <list type="bullet">
/// <item>Mass: <see cref="Unit.FrameMass"/> per frame cell plus each block's total cost (at least 1).</item>
/// <item>Power: draw is the sum of <see cref="BuildingDef.Power"/>; supply is what batteries and generators give on board.</item>
/// <item>Cost: every block's cost plus the frame's (<see cref="ContentDb.Frame"/>) per frame cell.</item>
/// <item>Build time: <see cref="Balance.BuildTicksPerItem"/> per item of cost, block by block, frame included.</item>
/// <item>Upkeep: the sum of the blocks' <see cref="BuildingDef.Upkeep"/>, items a minute (UD-013).</item>
/// </list>
/// </summary>
/// <param name="Cells">Frame cells: the unit's size, compared with block caps (UD-006) and the Wunderwaffe threshold.</param>
/// <param name="Blocks">Buildings on the frame.</param>
/// <param name="Hp">Hit points left over all blocks; <paramref name="MaxHp"/> is the total when undamaged.</param>
/// <param name="BuildTicks">Ticks a factory spends placing everything, not counting waits for items.</param>
/// <param name="Dps">Damage per second of all weapons; 0 until combat adds weapons.</param>
public sealed record UnitStats(
    int Cells,
    int Blocks,
    int Mass,
    int Hp,
    int MaxHp,
    int PowerDraw,
    int PowerSupply,
    SortedDictionary<string, int> Cost,
    int BuildTicks,
    int Dps,
    SortedDictionary<string, int> Upkeep)
{
    /// <summary>Big enough to count as a Wunderwaffe (WW-001). Same rules otherwise.</summary>
    public bool IsWunderwaffe => Cells > Balance.WunderwaffeCells;

    /// <summary>
    /// A flying unit's build radius in cells (WM-015, OQ-58), from its live blocks (Hp above 0), so lost blocks shrink it:
    /// <see cref="Balance.VehicleRadiusBase"/> + <see cref="Balance.VehicleRadiusPerBlock"/> per block, capped.
    /// </summary>
    public static int BuildRadius(Unit unit) => BuildRadius(unit.Blocks.Count(b => b.Hp > 0));

    /// <inheritdoc cref="BuildRadius(Unit)"/>
    public static int BuildRadius(int liveBlocks) =>
        liveBlocks <= 0 ? 0 : Math.Min(Balance.VehicleRadiusMax, Balance.VehicleRadiusBase + Balance.VehicleRadiusPerBlock * liveBlocks);

    public static UnitStats Of(ContentDb content, Unit unit) =>
        Of(content, unit.Blocks.Select(b => (b.DefId, b.Hp)), unit.Cells.Count);

    /// <summary>A blueprint's design as it would be built: full hit points.</summary>
    public static UnitStats Of(ContentDb content, Blueprint bp) =>
        Of(content, bp.Blocks.Where(b => content.Buildings.ContainsKey(b.DefId)).Select(b => (b.DefId, content.Buildings[b.DefId].Hp)),
            bp.Frames.Length);

    public static UnitStats Of(ContentDb content, IEnumerable<(string DefId, int Hp)> blocks, int cells)
    {
        int count = 0, mass = cells * Unit.FrameMass, hp = 0, maxHp = 0, draw = 0, supply = 0, dps = 0;
        var frame = UnitAssembly.FrameCost(content);
        int ticks = cells * BuildTicksOf(frame);
        var cost = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var upkeep = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < cells; i++)
            Blueprints.Add(cost, frame);
        foreach (var (id, blockHp) in blocks)
        {
            var def = content.Buildings[id];
            count++;
            mass += Math.Max(1, def.Cost.Values.Sum());
            hp += blockHp;
            maxHp += def.Hp;
            draw += def.Power;
            supply += PowerNet.OnboardSupply(def);
            dps += WeaponDps(def);
            ticks += BuildTicksOf(def.Cost);
            Blueprints.Add(cost, def.Cost);
            if (def.Upkeep != null)
                Blueprints.Add(upkeep, def.Upkeep);
        }
        return new UnitStats(cells, count, mass, hp, maxHp, draw, supply, cost, ticks, dps, upkeep);
    }

    /// <summary>Ticks a factory spends placing a block with this cost once the items are in.</summary>
    public static int BuildTicksOf(IReadOnlyDictionary<string, int> cost) => Math.Max(1, cost.Values.Sum()) * Balance.BuildTicksPerItem;

    static int WeaponDps(BuildingDef def) => Combat.Dps(def);
}
