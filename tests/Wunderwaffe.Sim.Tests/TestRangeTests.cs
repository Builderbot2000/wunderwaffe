using Wunderwaffe.Sim.Api;
using Wunderwaffe.Sim.World;
using Xunit.Abstractions;
using static Wunderwaffe.Sim.Tests.Harness;

namespace Wunderwaffe.Sim.Tests;

/// <summary>UD-008: a design tried in a sandbox against dummy targets, free and apart from the match.</summary>
public class TestRangeTests(ITestOutputHelper output)
{
    /// <summary>A thruster, the 2x2 core, a gun and a second thruster on a 4x2 frame.</summary>
    static readonly Blueprint Gunship = new(
        [new("thruster", 0, 0, 0), new("core", 1, 0, 0), new("gun_turret", 3, 0, 0), new("thruster", 3, 1, 0)],
        [.. Enumerable.Range(0, 8).Select(i => new BlueprintCell(i % 4, i / 4))]);

    [Fact]
    public void AGunshipDealsDamageAndAnUnarmedScoutDealsNone()
    {
        var sim = Simulation.NewGame(TestContent.Load(), GameSetup.Default);
        var q = new GameQuery(sim, Me);
        ulong before = sim.StateHash();

        var gun = q.TestDesign(Gunship);
        output.WriteLine($"{gun}");
        Assert.Equal(TestRangeResult.Ok, gun.Result);
        Assert.True(gun.DamageDealt > 0 && gun.MeasuredDps > 0, $"dealt {gun.DamageDealt}");
        Assert.True(gun.StatDps > 0);
        Assert.True(gun.Targets > 0);

        var scout = q.TestDesign(q.Prefab("scout")!.Blueprint);
        output.WriteLine($"{scout}");
        Assert.Equal((TestRangeResult.Ok, 0L, 0), (scout.Result, scout.DamageDealt, scout.StatDps));
        Assert.True(scout.HpLeft < scout.MaxHp || !scout.Survived, "the turret should shoot back");

        // The match is untouched and nothing was paid.
        Assert.Equal(before, sim.StateHash());
    }

    [Fact]
    public void TheSameDesignGetsTheSameReport() =>
        Assert.Equal(TestRange.Run(TestContent.Load(), Gunship), TestRange.Run(TestContent.Load(), Gunship));

    [Fact]
    public void OnlyUnitDesignsAreTried()
    {
        var content = TestContent.Load();
        Assert.Equal(TestRangeResult.NotAUnit, TestRange.Check(content, new Blueprint([new("wall", 0, 0, 0)], [])));
        Assert.Equal(TestRangeResult.Invalid, TestRange.Check(content, new Blueprint([new("no_such_block", 0, 0, 0)], [new(0, 0)])));
        Assert.Equal(TestRangeResult.NotAUnit, TestRange.Run(content, new Blueprint([new("wall", 0, 0, 0)], [])).Result);
    }
}
