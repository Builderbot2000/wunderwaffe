using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.World;

/// <summary>
/// What a design did on the test range (UD-008).
/// </summary>
/// <param name="Result">Whether it ran; anything but <see cref="TestRangeResult.Ok"/> leaves the figures at 0.</param>
/// <param name="Seconds">How long the trial lasted: <see cref="TestRange.Seconds"/>, or less if it ended early.</param>
/// <param name="DamageDealt">Hit points the targets lost.</param>
/// <param name="MeasuredDps">Damage dealt per second over the trial: range, travel and ammo included, unlike <paramref name="StatDps"/>.</param>
/// <param name="StatDps">The design's weapons' paper DPS (UD-002), for comparison.</param>
/// <param name="TargetsDestroyed">Targets destroyed, of <paramref name="Targets"/>.</param>
/// <param name="Survived">Still flying at the end.</param>
/// <param name="SecondsAlive">How long it lasted under the turret's fire.</param>
/// <param name="HpLeft">Hit points left over its blocks, of <paramref name="MaxHp"/>.</param>
/// <param name="BlocksLost">Blocks shot off.</param>
public sealed record TestRangeReport(
    TestRangeResult Result, int Seconds, long DamageDealt, int MeasuredDps, int StatDps, int TargetsDestroyed, int Targets,
    bool Survived, int SecondsAlive, int HpLeft, int MaxHp, int BlocksLost);

public enum TestRangeResult { Ok, NotAUnit, Invalid }

/// <summary>
/// UD-008 test range: a design is spawned for free in a sandbox (its own throwaway <see cref="Simulation"/>, so the
/// match is never touched and nothing is paid), facing a block of dummy walls behind a gun turret that shoots back. It
/// attack-moves at them for <see cref="Seconds"/> and the report says how much damage it dealt and how it held up.
/// Deterministic: the same design always gets the same report. Its weapons' ammo rides in its cargo.
/// </summary>
public static class TestRange
{
    /// <summary>Trial length in seconds.</summary>
    public const int Seconds = 60;

    /// <summary>The sandbox: one square tile, the design on the west, the targets on the east.</summary>
    public const int Size = 64;

    /// <summary>Ammo per weapon ammo item the design carries, and shells in the turret's core.</summary>
    public const int Ammo = 2000;

    const int Me = 0, Range = 1;

    /// <summary>Checks that a blueprint is a unit design the range can spawn: one core and a frame, blocks known.</summary>
    public static TestRangeResult Check(ContentDb content, Blueprint design)
    {
        if (Prefabs.Problems(content, "design", design, null).Count > 0)
            return TestRangeResult.Invalid;
        return design.Frames.Length > 0 && design.Blocks.Count(b => content.Buildings[b.DefId].Kind == BuildingKind.Core) == 1
            ? TestRangeResult.Ok : TestRangeResult.NotAUnit;
    }

    /// <summary>Runs the trial. Costs a few thousand ticks of a tiny world: fine on a button press, not every frame.</summary>
    public static TestRangeReport Run(ContentDb content, Blueprint design)
    {
        var stats = UnitStats.Of(content, design);
        var check = Check(content, design);
        if (check != TestRangeResult.Ok)
            return new(check, 0, 0, 0, stats.Dps, 0, 0, false, 0, 0, stats.MaxHp, 0);

        var sim = new Simulation(content);
        var tile = sim.AddTile(0, 0, Range, Size);
        Blueprints.Stamp(sim, tile, Range, Targets(content), Size - 16, Size / 2 - 6);

        var ammo = design.Blocks.Select(b => content.Buildings[b.DefId].Weapon?.Ammo).OfType<string>().Distinct()
            .ToDictionary(a => a, _ => Ammo);
        var (w, h) = design.Size(content.Buildings);
        var unit = sim.SpawnPrefab(tile, Me, new PrefabDef("test_range", "Design", design.Blocks, design.Frames,
            new SortedDictionary<string, int>(ammo, StringComparer.Ordinal)), Size / 2 - w - 6, Size / 2 - h / 2);
        unit.Stance = Stance.Aggressive;
        int id = unit.Id;
        sim.Enqueue(new Core.AttackMoveCommand(Me, [id], tile.Id, Size - 12, Size / 2));

        var targets = tile.Grid.Buildings.Where(b => b.Owner == Range).Select(b => b.Id).ToList();
        long fullHp = TargetHp(tile, targets);
        int alive = 0, ticks = Seconds * Simulation.TicksPerSecond;
        for (int t = 0; t < ticks && sim.UnitById(id) != null && tile.Grid.Buildings.Any(b => b.Owner == Range); t++, alive++)
            sim.Step();

        int left = targets.Count(i => tile.Grid.BuildingById(i) != null);
        long dealt = fullHp - TargetHp(tile, targets);
        int seconds = Math.Max(1, alive / Simulation.TicksPerSecond);
        var survivor = sim.UnitById(id);
        return new(TestRangeResult.Ok, seconds, dealt, (int)(dealt / seconds), stats.Dps, targets.Count - left, targets.Count,
            survivor != null, survivor != null ? seconds : alive / Simulation.TicksPerSecond,
            survivor?.Blocks.Sum(b => b.Hp) ?? 0, stats.MaxHp, survivor?.Blocks.Count(b => b.Hp <= 0) ?? design.Blocks.Length);
    }

    /// <summary>Hit points the targets still standing have.</summary>
    static long TargetHp(TileState tile, List<int> ids) => ids.Sum(i => (long)(tile.Grid.BuildingById(i)?.Hp ?? 0));

    /// <summary>
    /// The targets: the first core (holding shells), a gun turret in front of it and a 4x4 block of walls before that,
    /// all from the content by kind so a renamed or modded content still gets a range.
    /// </summary>
    static PrefabDef Targets(ContentDb content)
    {
        var core = content.DefaultCore!;
        var turret = content.BuildingList.FirstOrDefault(b => b.Kind == BuildingKind.Weapon && !b.Neutral && b.Weapon?.Ammo is { } a && content.Items.ContainsKey(a));
        var wall = content.BuildingList.FirstOrDefault(b => b.Kind == BuildingKind.Wall && !b.Neutral && b.Width == 1 && b.Height == 1);
        var blocks = new List<BlueprintBlock> { new(core.Id, 8, 4, 0) };
        if (turret != null)
            blocks.Add(new(turret.Id, 6, 5, 0));
        for (int y = 0; wall != null && y < 4; y++)
            for (int x = 0; x < 4; x++)
                blocks.Add(new(wall.Id, x, 4 + y, 0));
        var items = turret?.Weapon?.Ammo is { } shell ? new SortedDictionary<string, int>(StringComparer.Ordinal) { [shell] = Ammo } : null;
        return new PrefabDef("test_range_targets", "Targets", [.. blocks], [], items);
    }
}
