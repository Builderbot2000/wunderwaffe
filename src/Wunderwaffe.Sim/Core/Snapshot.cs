using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Core;

/// <summary>
/// The whole changing state of a match at the start of a tick (NF-008), so a load can start here and replay only the
/// commands after it. What never changes after generation (terrain, tile shapes, zones, deposits, names, circles) is
/// rebuilt from the <see cref="GameSetup"/> instead, and derived caches (power networks, boosts, nav regions, paths'
/// region tables) rebuild themselves. Anything new that is sim state must be captured and restored here as well as
/// hashed in <see cref="StateHash"/>; <c>SnapshotTests</c> check the two agree.
/// </summary>
public sealed record Snapshot(long Tick, int NextUnitId, List<TileSnap> Tiles, List<PlayerSnap> Players, List<UnitSnap> Units,
    List<AbstractSnap> Abstract, SquadsSnap Squads, int NextProjectileId, List<ProjectileSnap> Projectiles, List<int> Interrupted)
{
    public static Snapshot Capture(Simulation sim) => new(
        sim.Tick,
        sim.NextUnitId,
        sim.Tiles.Select(t => TileSnap.Of(sim, t)).ToList(),
        sim.Players.Select(PlayerSnap.Of).ToList(),
        sim.Units.Select(UnitSnap.Of).ToList(),
        sim.Abstract.Select(kv => new AbstractSnap(kv.Key, kv.Value.Waypoints.Select(p => new LongPoint(p.X, p.Y)).ToList(), kv.Value.Next)).ToList(),
        SquadsSnap.Of(sim.Squads),
        sim.NextProjectileId,
        sim.Projectiles.Select(ProjectileSnap.Of).ToList(),
        [.. sim.InterruptedUnits.Keys]);

    /// <summary>
    /// A match in the snapshot's state: the world rebuilt from <paramref name="setup"/>, then everything that changes
    /// put back. <paramref name="log"/> is the command log up to the snapshot, kept so later saves stay complete.
    /// </summary>
    public Simulation Restore(ContentDb content, GameSetup setup, IEnumerable<LoggedCommand> log)
    {
        var sim = Simulation.NewGame(content, setup);
        foreach (var t in Tiles)
            t.Restore(sim, sim.Tiles[t.Id]);
        foreach (var p in Players)
            p.Restore(sim.Player(p.Id));
        sim.RestoreUnits(Units.Select(u => u.Restore(sim)));
        foreach (var a in Abstract)
            sim.AbstractNodes[a.UnitId] = new AbstractNode(a.Waypoints.Select(p => (p.X, p.Y)).ToArray(), sim.UnitById(a.UnitId)!.Route) { Next = a.Next };
        Squads.Restore(sim.Squads);
        foreach (int id in Interrupted)
            sim.InterruptedUnits[id] = sim.UnitById(id)!.Order;
        sim.RestoreProjectiles(Projectiles.Select(p => p.Restore()), NextProjectileId);
        sim.RestoreClock(Tick, NextUnitId, log);
        sim.LatestSnapshot = this;
        return sim;
    }
}

public readonly record struct Point(int X, int Y);

public readonly record struct LongPoint(long X, long Y);

public sealed record AccSnap(FlowKind Kind, int PortId, string Item, long Acc);

/// <summary>One series of <see cref="ProductionStats"/>: key (item, or partner tile id), ring of buckets, total.</summary>
public sealed record SeriesSnap(string Key, long[] Ring, long Total);

public sealed record StatsSnap(long Bucket, List<SeriesSnap> Made, List<SeriesSnap> Used, List<SeriesSnap> Sent);

public sealed record ClaimSnap(int Player, int Ticks);

public sealed record TileSnap(int Id, int Owner, int Revision, int NextBuildingId, int NextWreckId,
    List<int> Frames, List<int> Platforms, List<BuildingSnap> Buildings, List<Wreck> Wrecks, List<ClaimSnap> Claims, StatsSnap Stats,
    bool Full, List<AccSnap> Rates, List<int> Cables)
{
    internal static TileSnap Of(Simulation sim, TileState t)
    {
        var g = t.Grid;
        var frames = new List<int>();
        var platforms = new List<int>();
        for (int i = 0; (g.FrameCount > 0 || g.PlatformCount > 0) && i < g.Width * g.Height; i++)
        {
            if (g.HasFrame(i % g.Width, i / g.Width))
                frames.Add(i);
            if (g.HasPlatform(i % g.Width, i / g.Width))
                platforms.Add(i);
        }
        var cables = new List<int>();
        for (int i = 0; g.CableCount > 0 && i < g.Width * g.Height; i++)
        {
            if (g.HasCable(i % g.Width, i / g.Width))
                cables.Add(i);
        }
        var rs = sim.RateState(t.Id);
        var acc = rs.Acc.Select((a, i) => (a, i)).Where(e => e.a != 0)
            .Select(e => new AccSnap(rs.AccKeys[e.i].Kind, rs.AccKeys[e.i].PortId, rs.AccKeys[e.i].Item, e.a)).ToList();
        return new TileSnap(t.Id, t.Owner, g.Revision, g.NextBuildingId, g.NextWreckId, frames, platforms,
            g.Buildings.Select(BuildingSnap.Of).ToList(), [.. g.Wrecks],
            t.Claims.Select(kv => new ClaimSnap(kv.Key, kv.Value)).ToList(), t.Stats.Snap(), rs.Full, acc, cables);
    }

    internal void Restore(Simulation sim, TileState t)
    {
        t.Owner = Owner;
        t.Claims.Clear();
        foreach (var c in Claims)
            t.Claims[c.Player] = c.Ticks;
        t.Stats.Restore(Stats);

        var g = t.Grid;
        g.ClearForRestore();
        foreach (int i in Frames)
            g.SetFrame(i % g.Width, i / g.Width, true);
        foreach (int i in Platforms)
            g.SetPlatform(i % g.Width, i / g.Width, true);
        foreach (int i in Cables)
            g.SetCable(i % g.Width, i / g.Width, true);
        foreach (var b in Buildings)
            b.Restore(sim.Content, g);
        foreach (var w in Wrecks)
            g.RestoreWreck(w);
        g.RestoreIds(NextBuildingId, NextWreckId, Revision);

        var rs = sim.RateState(t.Id);
        rs.Full = Full;
        rs.Rates = null;
        rs.AccKeys = Rates.Select(a => new Flow(a.Kind, a.PortId, a.Item, 0)).ToList();
        rs.Acc = Rates.Select(a => a.Acc).ToArray();
    }
}

public sealed record BeltSnap(string?[] Items, int[] Positions, int Count);

public sealed record MachineSnap(bool Working, int Progress, int IdleTicks, int DumpWait, int DumpCursor,
    Dictionary<string, int> Input, Dictionary<string, int> Output, int OutputCount, int OutputCredit);

public sealed record ExportSnap(int Cursor, List<Shipment> InFlight);

public sealed record CombatSnap(int Ammo, int Cooldown, int Charge, WeaponStatus Status, Target Target, int RepairDebt)
{
    internal static CombatSnap? Of(CombatState? c) => c == null ? null : new(c.Ammo, c.Cooldown, c.Charge, c.Status, c.Target, c.RepairDebt);

    internal void Restore(CombatState c) =>
        (c.Ammo, c.Cooldown, c.Charge, c.Status, c.Target, c.RepairDebt) = (Ammo, Cooldown, Charge, Status, Target, RepairDebt);
}

/// <summary>A unit factory's state; its steps are rebuilt from the projection (<c>UnitFactory.StepsOf</c>).</summary>
public sealed record FactorySnap(Blueprint? Projected, int OX, int OY, int Next, bool Working, int Progress,
    Dictionary<string, int> Buffer, FactoryStatus Status, AssembleResult LiftOff, int Built, FactoryMode Mode = FactoryMode.Continuous);

public sealed record StorageSnap(int DumpWait, int DumpCursor, Dictionary<string, int> Output, int OutputCount);

public sealed record RelaySnap(int Cursor, List<List<RelayItem>> Lanes);

public sealed record BuildingSnap(int Id, string DefId, int Owner, int Hp, int X, int Y, int W, int H, int Rotation, string? RecipeId,
    BeltSnap? Belt, MachineSnap? Machine, ExportSnap? Export, CombatSnap? Combat = null, FactorySnap? Factory = null,
    StorageSnap? Storage = null, long? BatteryCharge = null, RelaySnap? Relay = null, string? SorterFilter = null, bool SorterFlip = false,
    Dictionary<string, long>? Stock = null)
{
    internal static BuildingSnap Of(PlacedBuilding b) => new(b.Id, b.DefId, b.Owner, b.Hp, b.X, b.Y, b.Width, b.Height, b.Rotation, b.RecipeId,
        b.Belt is { } belt ? new BeltSnap([.. belt.Items], [.. belt.Positions], belt.Count) : null,
        b.Machine is { } m
            ? new MachineSnap(m.Working, m.Progress, m.IdleTicks, m.DumpWait, m.DumpCursor, new(m.Input), new(m.Output), m.OutputCount, m.OutputCredit)
            : null,
        b.Export is { } e ? new ExportSnap(e.Cursor, [.. e.InFlight]) : null,
        CombatSnap.Of(b.Combat),
        b.Factory is { } f ? new FactorySnap(f.Projected, f.OX, f.OY, f.Next, f.Working, f.Progress, new(f.Buffer), f.Status, f.LiftOff, f.Built, f.Mode) : null,
        b.Storage is { } s ? new StorageSnap(s.DumpWait, s.DumpCursor, new(s.Output), s.OutputCount) : null,
        b.Battery?.Charge,
        b.Relay is { } r ? new RelaySnap(r.Cursor, r.Lanes.Select(l => l.ToList()).ToList()) : null,
        b.Sorter?.Filter,
        b.Sorter?.Flip ?? false,
        b.Stock?.Entries.ToDictionary(kv => kv.Key, kv => kv.Value));

    internal void Restore(ContentDb content, TileGrid g)
    {
        var recipe = RecipeId == null ? null : content.Recipes[RecipeId];
        var b = g.Restore(Id, content.Buildings[DefId], Owner, X, Y, W, H, Rotation, recipe, Hp);
        if (Belt is { } belt && b.Belt is { } bs)
        {
            belt.Items.CopyTo(bs.Items, 0);
            belt.Positions.CopyTo(bs.Positions, 0);
            bs.Count = belt.Count;
        }
        if (Machine is { } ms && b.Machine is { } m)
        {
            (m.Working, m.Progress, m.IdleTicks, m.DumpWait, m.DumpCursor, m.OutputCount, m.OutputCredit) =
                (ms.Working, ms.Progress, ms.IdleTicks, ms.DumpWait, ms.DumpCursor, ms.OutputCount, ms.OutputCredit);
            foreach (var (id, n) in ms.Input)
                m.Input[id] = n;
            foreach (var (id, n) in ms.Output)
                m.Output[id] = n;
        }
        if (Export is { } es && b.Export is { } e)
        {
            e.Cursor = es.Cursor;
            foreach (var s in es.InFlight)
                e.InFlight.Enqueue(s);
        }
        if (Combat is { } cs && b.Combat is { } c)
            cs.Restore(c);
        foreach (var (id, n) in Stock ?? [])
            b.Stock!.Add(id, n);
        if (Factory is { } fs && b.Factory is { } f)
        {
            (f.Projected, f.OX, f.OY, f.Next, f.Working, f.Progress, f.Status, f.LiftOff, f.Built) =
                (fs.Projected, fs.OX, fs.OY, fs.Next, fs.Working, fs.Progress, fs.Status, fs.LiftOff, fs.Built);
            f.Mode = fs.Mode;
            f.Steps = fs.Projected is { } bp ? UnitFactory.StepsOf(content, bp, fs.OX, fs.OY) : [];
            foreach (var (id, n) in fs.Buffer)
                f.Buffer[id] = n;
        }
        if (Storage is { } ss && b.Storage is { } st)
        {
            (st.DumpWait, st.DumpCursor, st.OutputCount) = (ss.DumpWait, ss.DumpCursor, ss.OutputCount);
            foreach (var (id, n) in ss.Output)
                st.Output[id] = n;
        }
        if (BatteryCharge is { } charge && b.Battery is { } bat)
            bat.Charge = charge;
        if (Relay is { } rs && b.Relay is { } r)
        {
            r.Cursor = rs.Cursor;
            for (int i = 0; i < rs.Lanes.Count; i++)
                r.Lanes[i].AddRange(rs.Lanes[i]);
        }
        if (b.Sorter is { } sorter)
            (sorter.Filter, sorter.Flip) = (SorterFilter, SorterFlip);
    }
}

public sealed record PlayerSnap(int Id, Dictionary<string, long> Produced, long Shipped, long Launched, List<long> GoalTicks, int ViewedTile)
{
    internal static PlayerSnap Of(PlayerState p) =>
        new(p.Id, p.Produced.ToDictionary(kv => kv.Key, kv => kv.Value), p.Shipped, p.Launched, [.. p.GoalTicks], p.ViewedTile);

    internal void Restore(PlayerState p)
    {
        foreach (var (id, n) in Produced)
        {
            for (long left = n - p.ProducedOf(id); left > 0; left -= int.MaxValue)
                p.AddProduced(new Dictionary<string, int> { [id] = (int)Math.Min(left, int.MaxValue) });
        }
        p.Shipped = Shipped;
        p.Launched = Launched;
        foreach (long t in GoalTicks.Skip(p.GoalsDone))
            p.CompleteGoal(t);
        p.ViewedTile = ViewedTile;
    }
}

public sealed record CellSnap(int GX, int GY, int Mass);

public sealed record PathKeySnap(int Tile, int Next, long Revision, int GoalX, int GoalY);

public sealed record PortalSnap(int Tile, int Next, int X, int Y);

/// <summary>A unit: body, motion, route and path, movement's order state (UC-001..006), combat per block, cargo, upkeep.</summary>
public sealed record UnitSnap(int Id, int Owner, int TileId, int X, int Y, int VX, int VY, int Angle, int AngVel,
    List<UnitBlock> Blocks, List<CellSnap> Cells, int CoreGX, int CoreGY,
    int[]? Route, int[]? RouteRegions, int RouteIndex, int DestTileId, int DestX, int DestY,
    List<Point>? Path, int PathIndex, PathKeySnap? PathKey, PortalSnap? Portal,
    UnitOrder? Order, int SlotX, int SlotY, bool PatrolReturning, bool Chasing, Stance Stance, int AnchorX, int AnchorY,
    int SquadId, List<CombatSnap?> BlockCombat, Dictionary<string, long> Cargo, long UpkeepDue, bool Unsupplied, TargetMode Targeting)
{
    internal static UnitSnap Of(Unit u) => new(u.Id, u.Owner, u.TileId, u.X, u.Y, u.VX, u.VY, u.Angle, u.AngVel,
        u.Blocks.Select(b => b with { }).ToList(), u.Cells.Select(c => new CellSnap(c.GX, c.GY, c.Mass)).ToList(), u.CoreGX, u.CoreGY,
        u.Route == null ? null : [.. u.Route], u.RouteRegions == null ? null : [.. u.RouteRegions], u.RouteIndex, u.DestTileId, u.DestX, u.DestY,
        u.Path?.Select(p => new Point(p.X, p.Y)).ToList(), u.PathIndex,
        u.PathKey is { } k ? new PathKeySnap(k.Tile, k.Next, k.Revision, k.GoalX, k.GoalY) : null,
        u.Portal is { } p ? new PortalSnap(p.Tile, p.Next, p.X, p.Y) : null,
        u.Order, u.SlotX, u.SlotY, u.PatrolReturning, u.Chasing, u.Stance, u.AnchorX, u.AnchorY, u.SquadId,
        u.BlockCombat.Select(CombatSnap.Of).ToList(), u.Cargo.Entries.ToDictionary(kv => kv.Key, kv => kv.Value), u.UpkeepDue, u.Unsupplied, u.Targeting);

    internal Unit Restore(Simulation sim)
    {
        var blocks = Blocks.Select(b => b with { }).ToList();
        var u = new Unit(Id, Owner, TileId, 0, 0, blocks, Cells.Select(c => (c.GX, c.GY, c.Mass)).ToList(), sim.Content.Buildings, CoreGX, CoreGY)
        {
            X = X, Y = Y, VX = VX, VY = VY, Angle = Angle, AngVel = AngVel,
            Route = Route == null ? null : [.. Route], RouteRegions = RouteRegions == null ? null : [.. RouteRegions], RouteIndex = RouteIndex,
            DestTileId = DestTileId, DestX = DestX, DestY = DestY,
            Path = Path?.Select(p => (p.X, p.Y)).ToArray(), PathIndex = PathIndex,
            PathKey = PathKey is { } k ? (k.Tile, k.Next, k.Revision, k.GoalX, k.GoalY) : null,
            Portal = Portal is { } p ? (p.Tile, p.Next, p.X, p.Y) : null,
            Order = Order, SlotX = SlotX, SlotY = SlotY, PatrolReturning = PatrolReturning, Chasing = Chasing, Stance = Stance,
            AnchorX = AnchorX, AnchorY = AnchorY, SquadId = SquadId,
            UpkeepDue = UpkeepDue, Unsupplied = Unsupplied, Targeting = Targeting,
        };
        for (int i = 0; i < BlockCombat.Count; i++)
        {
            if (BlockCombat[i] is { } cs && u.BlockCombat[i] is { } c)
                cs.Restore(c);
        }
        foreach (var (id, n) in Cargo)
            u.Cargo.Add(id, n);
        // Hit points came back with the blocks; derived stats follow from the live ones.
        u.RecomputeStats(sim.Content.Buildings);
        return u;
    }
}

public sealed record AbstractSnap(int UnitId, List<LongPoint> Waypoints, int Next);

public sealed record SquadSnap(int Id, int Owner, string Name, List<int> Members, Formation Formation, List<Point> Custom, Stance Stance,
    UnitOrder? Order, int TargetId, TargetMode Targeting, int FocusId);

public sealed record SquadsSnap(int NextId, List<SquadSnap> Squads, List<Rally> Rallies)
{
    internal static SquadsSnap Of(Squads squads) => new(squads.NextId,
        squads.All.Select(s => new SquadSnap(s.Id, s.Owner, s.Name, [.. s.Members], s.Formation,
            s.Custom.Select(c => new Point(c.X, c.Y)).ToList(), s.Stance, s.Order, s.TargetId, s.Targeting, s.FocusId)).ToList(),
        [.. squads.Rallies]);

    /// <summary>Fills an empty registry.</summary>
    internal void Restore(Squads into)
    {
        foreach (var s in Squads)
        {
            var squad = into.AddRestored(s.Id, s.Owner, s.Name);
            squad.MemberList.AddRange(s.Members);
            (squad.Formation, squad.Stance, squad.Order, squad.TargetId) = (s.Formation, s.Stance, s.Order, s.TargetId);
            squad.Custom = s.Custom.Select(c => (c.X, c.Y)).ToArray();
            (squad.Targeting, squad.FocusId) = (s.Targeting, s.FocusId);
        }
        foreach (var r in Rallies)
            into.SetRally(r);
        into.NextId = NextId;
    }
}

public sealed record ProjectileSnap(int Id, int Owner, string WeaponId, long X, long Y, long FromX, long FromY, long VX, long VY,
    int Damage, string Type, bool Beam, int Life, int Shooter, bool Done)
{
    internal static ProjectileSnap Of(Projectile p) =>
        new(p.Id, p.Owner, p.WeaponId, p.X, p.Y, p.FromX, p.FromY, p.VX, p.VY, p.Damage, p.Type, p.Beam, p.Life, p.Shooter, p.Done);

    internal Projectile Restore() => new()
    {
        Id = Id, Owner = Owner, WeaponId = WeaponId, X = X, Y = Y, FromX = FromX, FromY = FromY, VX = VX, VY = VY,
        Damage = Damage, Type = Type, Beam = Beam, Life = Life, Shooter = Shooter, Done = Done,
    };
}
