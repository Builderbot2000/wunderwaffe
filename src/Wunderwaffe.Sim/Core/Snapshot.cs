using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Core;

/// <summary>
/// The whole changing state of a match at the start of a tick (NF-008), so a load can start here and replay only the
/// commands after it. What never changes after generation (terrain, tile shapes, zones, deposits, names) is rebuilt from
/// the <see cref="GameSetup"/> instead. Anything new that is sim state must be captured and restored here as well as
/// hashed in <see cref="StateHash"/>; <c>SnapshotTests</c> check the two agree.
/// </summary>
public sealed record Snapshot(long Tick, int NextUnitId, List<TileSnap> Tiles, List<PlayerSnap> Players, List<UnitSnap> Units, List<AbstractSnap> Abstract)
{
    public static Snapshot Capture(Simulation sim) => new(
        sim.Tick,
        sim.NextUnitId,
        sim.Tiles.Select(t => TileSnap.Of(sim, t)).ToList(),
        sim.Players.Select(PlayerSnap.Of).ToList(),
        sim.Units.Select(UnitSnap.Of).ToList(),
        sim.Abstract.Select(kv => new AbstractSnap(kv.Key, kv.Value.Waypoints.Select(p => new LongPoint(p.X, p.Y)).ToList(), kv.Value.Next)).ToList());

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
        sim.RestoreClock(Tick, NextUnitId, log);
        sim.LatestSnapshot = this;
        return sim;
    }
}

public readonly record struct Point(int X, int Y);

public readonly record struct LongPoint(long X, long Y);

public sealed record AccSnap(FlowKind Kind, int PortId, string Item, long Acc);

public sealed record TileSnap(int Id, int Owner, int Revision, int NextBuildingId, Dictionary<string, long> Inventory, List<int> Frames,
    List<BuildingSnap> Buildings, bool Full, List<AccSnap> Rates)
{
    internal static TileSnap Of(Simulation sim, TileState t)
    {
        var g = t.Grid;
        var frames = new List<int>();
        for (int i = 0; g.FrameCount > 0 && i < g.Width * g.Height; i++)
        {
            if (g.HasFrame(i % g.Width, i / g.Width))
                frames.Add(i);
        }
        var rs = sim.RateState(t.Id);
        var acc = rs.Acc.Select((a, i) => (a, i)).Where(e => e.a != 0)
            .Select(e => new AccSnap(rs.AccKeys[e.i].Kind, rs.AccKeys[e.i].PortId, rs.AccKeys[e.i].Item, e.a)).ToList();
        return new TileSnap(t.Id, t.Owner, g.Revision, g.NextBuildingId, t.Inventory.Entries.ToDictionary(kv => kv.Key, kv => kv.Value),
            frames, g.Buildings.Select(BuildingSnap.Of).ToList(), rs.Full, acc);
    }

    internal void Restore(Simulation sim, TileState t)
    {
        t.Owner = Owner;
        foreach (var (id, n) in t.Inventory.Entries.ToList())
            t.Inventory.Add(id, -n);
        foreach (var (id, n) in Inventory)
            t.Inventory.Add(id, n);
        var g = t.Grid;
        g.ClearForRestore();
        foreach (int i in Frames)
            g.SetFrame(i % g.Width, i / g.Width, true);
        foreach (var b in Buildings)
            b.Restore(sim.Content, g);
        g.RestoreIds(NextBuildingId, Revision);

        var rs = sim.RateState(t.Id);
        rs.Full = Full;
        rs.Rates = null;
        rs.AccKeys = Rates.Select(a => new Flow(a.Kind, a.PortId, a.Item, 0)).ToList();
        rs.Acc = Rates.Select(a => a.Acc).ToArray();
    }
}

public sealed record BeltSnap(string?[] Items, int[] Positions, int Count);

public sealed record MachineSnap(bool Working, int Progress, int IdleTicks, int DumpWait, int DumpCursor,
    Dictionary<string, int> Input, Dictionary<string, int> Output, int OutputCount);

public sealed record ExportSnap(int Cursor, List<Shipment> InFlight);

public sealed record BuildingSnap(int Id, string DefId, int Owner, int Hp, int X, int Y, int W, int H, int Rotation, string? RecipeId,
    BeltSnap? Belt, MachineSnap? Machine, ExportSnap? Export)
{
    internal static BuildingSnap Of(PlacedBuilding b) => new(b.Id, b.DefId, b.Owner, b.Hp, b.X, b.Y, b.Width, b.Height, b.Rotation, b.RecipeId,
        b.Belt is { } belt ? new BeltSnap([.. belt.Items], [.. belt.Positions], belt.Count) : null,
        b.Machine is { } m
            ? new MachineSnap(m.Working, m.Progress, m.IdleTicks, m.DumpWait, m.DumpCursor, new(m.Input), new(m.Output), m.OutputCount)
            : null,
        b.Export is { } e ? new ExportSnap(e.Cursor, [.. e.InFlight]) : null);

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
            (m.Working, m.Progress, m.IdleTicks, m.DumpWait, m.DumpCursor, m.OutputCount) =
                (ms.Working, ms.Progress, ms.IdleTicks, ms.DumpWait, ms.DumpCursor, ms.OutputCount);
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

public sealed record UnitSnap(int Id, int Owner, int TileId, int X, int Y, int VX, int VY, int Angle, int AngVel,
    List<UnitBlock> Blocks, List<CellSnap> Cells, int CoreGX, int CoreGY,
    int[]? Route, int RouteIndex, int DestTileId, int DestX, int DestY,
    List<Point>? Path, int PathIndex, PathKeySnap? PathKey, PortalSnap? Portal)
{
    internal static UnitSnap Of(Unit u) => new(u.Id, u.Owner, u.TileId, u.X, u.Y, u.VX, u.VY, u.Angle, u.AngVel,
        [.. u.Blocks], u.Cells.Select(c => new CellSnap(c.GX, c.GY, c.Mass)).ToList(), u.CoreGX, u.CoreGY,
        u.Route == null ? null : [.. u.Route], u.RouteIndex, u.DestTileId, u.DestX, u.DestY,
        u.Path?.Select(p => new Point(p.X, p.Y)).ToList(), u.PathIndex,
        u.PathKey is { } k ? new PathKeySnap(k.Tile, k.Next, k.Revision, k.GoalX, k.GoalY) : null,
        u.Portal is { } p ? new PortalSnap(p.Tile, p.Next, p.X, p.Y) : null);

    internal Unit Restore(Simulation sim)
    {
        var blocks = Blocks.Select(b => b with { }).ToList();
        var u = new Unit(Id, Owner, TileId, 0, 0, blocks, Cells.Select(c => (c.GX, c.GY, c.Mass)).ToList(), sim.Content.Buildings, CoreGX, CoreGY)
        {
            X = X, Y = Y, VX = VX, VY = VY, Angle = Angle, AngVel = AngVel,
            Route = Route == null ? null : [.. Route], RouteIndex = RouteIndex,
            DestTileId = DestTileId, DestX = DestX, DestY = DestY,
            Path = Path?.Select(p => (p.X, p.Y)).ToArray(), PathIndex = PathIndex,
            PathKey = PathKey is { } k ? (k.Tile, k.Next, k.Revision, k.GoalX, k.GoalY) : null,
            Portal = Portal is { } p ? (p.Tile, p.Next, p.X, p.Y) : null,
        };
        return u;
    }
}

public sealed record AbstractSnap(int UnitId, List<LongPoint> Waypoints, int Next);
