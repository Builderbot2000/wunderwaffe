using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim.Api;

/// <summary>A building a plan would place, in command order.</summary>
public sealed record PlannedBuilding(string BuildingId, int X, int Y, int Rotation, string? RecipeId);

/// <summary>A machine of a production line: what it is and, for crafters with several recipes, which one.</summary>
public sealed record Stage(string BuildingId, string? RecipeId = null);

/// <summary>
/// Read-only layout searches for drivers that can't see the screen: the AI and external tools (NF-009). They only
/// propose; placing still goes through <see cref="Core.PlaceBuildingCommand"/> and <see cref="Placement.Check"/>.
/// Every placement they return passes <see cref="Placement.Check"/> on the current state except for cost, and keeps
/// items where they belong: nothing it adds would push items into, or take items from, a building of another line
/// (machines dump into every neighbour that accepts, so a careless neighbour steals or jams). Deterministic: candidates
/// are tried nearest first, ties by row then column.
/// </summary>
public static class Planner
{
    /// <summary>Drill positions tried by <see cref="PlanLine"/> before it gives up.</summary>
    const int DrillTries = 24;

    /// <summary>
    /// A legal spot for one building: on a deposit of <paramref name="onDeposit"/> (extractors), touching building
    /// <paramref name="touching"/> (which may feed it), inside the I/O zone facing tile <paramref name="zone"/> (ports),
    /// nearest (<paramref name="nearX"/>, <paramref name="nearY"/>) or the tile's core. Null if there is none.
    /// </summary>
    public static Spot? FindSpot(Simulation sim, TileState tile, int player, string buildingId, string? recipeId = null,
        string? onDeposit = null, int touching = -1, int nearX = -1, int nearY = -1, int zone = -1)
    {
        if (!sim.Content.Buildings.TryGetValue(buildingId, out var def))
            return null;
        var layout = new Layout(sim, tile);
        Node? feeder = touching >= 0 ? layout.Real(touching) : null;
        if (touching >= 0 && feeder == null)
            return null;
        if (nearX < 0)
            (nearX, nearY) = layout.CoreCentre();
        var spot = layout.Candidates(def, onDeposit, feeder, nearX, nearY)
            .Select(c => layout.TryNode(player, def, c.X, c.Y, recipeId, onDeposit, feeder, null))
            .FirstOrDefault(n => n != null && (zone < 0 || PortRules.ZoneOf(tile.Grid, n.X, n.Y, n.W, n.H) == zone));
        return spot == null ? null : new Spot(spot.X, spot.Y, 0, spot.Recipe?.Id);
    }

    /// <summary>
    /// A belt from building <paramref name="from"/> to building <paramref name="to"/>: the shortest run of free cells
    /// that starts beside <paramref name="from"/>, ends facing <paramref name="to"/> and passes nothing else that would
    /// push items onto it. Empty if they already touch; null if there is no way.
    /// </summary>
    public static IReadOnlyList<BeltStep>? RouteBelt(Simulation sim, TileState tile, int from, int to)
    {
        var layout = new Layout(sim, tile);
        return layout.Real(from) is { } a && layout.Real(to) is { } b ? layout.Route(a, b) : null;
    }

    /// <summary>
    /// A whole production line, placed together: a drill on <paramref name="depositItem"/> (or the existing building
    /// <paramref name="source"/>), then each of <paramref name="stages"/> touching the one before, then a belt into
    /// building <paramref name="sink"/>. Null if no layout fits. The caller places them in order in one tick.
    /// </summary>
    public static IReadOnlyList<PlannedBuilding>? PlanLine(Simulation sim, TileState tile, int player, string? depositItem, int source,
        IReadOnlyList<Stage> stages, int sink)
    {
        var layout = new Layout(sim, tile);
        if (layout.Real(sink) is not { } target)
            return null;
        var (sx, sy) = (target.X + target.W / 2, target.Y + target.H / 2);

        if (depositItem == null)
        {
            if (layout.Real(source) is not { } start)
                return null;
            return layout.Extend(player, start, stages, target, []);
        }

        var drill = sim.Content.Buildings.Values.FirstOrDefault(d =>
            sim.Content.RecipesByBuilding.GetValueOrDefault(d.Id)?.Any(r => r.Inputs.Count == 0 && r.Outputs.ContainsKey(depositItem)) == true);
        if (drill == null)
            return null;
        int tries = 0;
        foreach (var (x, y) in layout.Candidates(drill, depositItem, null, sx, sy))
        {
            var node = layout.TryNode(player, drill, x, y, null, depositItem, null, stages.Count == 0 ? target : null);
            if (node == null)
                continue;
            layout.Push(node);
            var plan = layout.Extend(player, node, stages, target, [new PlannedBuilding(drill.Id, x, y, 0, null)]);
            layout.Pop();
            if (plan != null)
                return plan;
            if (++tries >= DrillTries)
                break;
        }
        return null;
    }

    /// <summary>
    /// Top-left of a <paramref name="width"/> x <paramref name="height"/> block of free, buildable, unframed cells with a
    /// clear cell of margin all round (so a unit built on it can lift off), nearest the tile's core. Null if none.
    /// </summary>
    public static Spot? FindFrame(Simulation sim, TileState tile, int width, int height)
    {
        var layout = new Layout(sim, tile);
        var (cx, cy) = layout.CoreCentre();
        var g = tile.Grid;
        foreach (var (x, y) in Nearest(g.Width, g.Height, cx - width / 2, cy - height / 2))
        {
            if (!g.InBounds(x, y, width, height) || !g.Buildable(x, y, width, height))
                continue;
            bool clear = true;
            for (int yy = y - 1; yy <= y + height && clear; yy++)
            {
                for (int xx = x - 1; xx <= x + width && clear; xx++)
                    clear = g.InRect(xx, yy) && g.BuildingAt(xx, yy) == null && !g.HasFrame(xx, yy);
            }
            if (clear)
                return new Spot(x, y, 0, null);
        }
        return null;
    }

    /// <summary>
    /// Where building <paramref name="id"/>'s output ends up: itself if nothing takes it, the building it feeds
    /// directly, or the building at the end of the belt it feeds. Lets drivers tell what a line is for.
    /// </summary>
    public static PlacedBuilding? OutletOf(TileState tile, PlacedBuilding b)
    {
        var g = tile.Grid;
        foreach (var (x, y) in Perimeter(b.X, b.Y, b.Width, b.Height))
        {
            if (g.BuildingAt(x, y) is not { } next || next == b)
                continue;
            if (next.Kind == BuildingKind.Conveyor)
            {
                var d = Directions.FromRotation(next.Rotation);
                if (g.BuildingAt(next.X + d.Dx(), next.Y + d.Dy()) == b)
                    continue;
                return EndOfBelt(g, next);
            }
            if (next.Kind is BuildingKind.Core or BuildingKind.ExportPort
                || (next.Machine?.Recipe is { } r && b.Machine?.Recipe is { } mine && mine.Outputs.Keys.Any(r.Inputs.ContainsKey)))
                return next;
        }
        return null;
    }

    static PlacedBuilding? EndOfBelt(TileGrid g, PlacedBuilding belt)
    {
        for (int steps = 0; steps < g.Width * g.Height; steps++)
        {
            var d = Directions.FromRotation(belt.Rotation);
            if (g.BuildingAt(belt.X + d.Dx(), belt.Y + d.Dy()) is not { } next)
                return null;
            if (next.Kind != BuildingKind.Conveyor)
                return next;
            belt = next;
        }
        return null;
    }

    static IEnumerable<(int X, int Y)> Perimeter(int x, int y, int w, int h)
    {
        for (int i = 0; i < w; i++)
            yield return (x + i, y - 1);
        for (int i = 0; i < h; i++)
            yield return (x + w, y + i);
        for (int i = w - 1; i >= 0; i--)
            yield return (x + i, y + h);
        for (int i = h - 1; i >= 0; i--)
            yield return (x - 1, y + i);
    }

    /// <summary>Every cell of a w x h box, nearest (cx, cy) first, ties by row then column.</summary>
    static IEnumerable<(int X, int Y)> Nearest(int w, int h, int cx, int cy)
    {
        var all = new List<(int X, int Y)>(w * h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
                all.Add((x, y));
        }
        return all.OrderBy(p => (long)(p.X - cx) * (p.X - cx) + (long)(p.Y - cy) * (p.Y - cy)).ThenBy(p => p.Y).ThenBy(p => p.X);
    }

    /// <summary>A building, real or only planned, as the item-flow rules see it.</summary>
    sealed class Node(int x, int y, int w, int h, BuildingDef def, RecipeDef? recipe, PlacedBuilding? real)
    {
        public int X { get; } = x;
        public int Y { get; } = y;
        public int W { get; } = w;
        public int H { get; } = h;
        public BuildingDef Def { get; } = def;
        public RecipeDef? Recipe { get; } = recipe;
        public PlacedBuilding? Real { get; } = real;

        /// <summary>Takes items pushed at it: what TileEntities.TryInsert accepts.</summary>
        public bool Accepts(string? item) => Def.Kind switch
        {
            BuildingKind.Core or BuildingKind.Conveyor or BuildingKind.ExportPort => true,
            BuildingKind.Machine => Recipe != null && (item == null ? Recipe.Inputs.Count > 0 : Recipe.Inputs.ContainsKey(item)),
            _ => false,
        };

        /// <summary>Pushes items into every neighbour (belts only push ahead, so they don't count).</summary>
        public bool Dumps => Def.Kind == BuildingKind.ImportPort || (Def.Kind == BuildingKind.Machine && Recipe != null);

        /// <summary>True if this pushes something <paramref name="other"/> would take.</summary>
        public bool Feeds(Node other) => Dumps && (Recipe == null ? other.Accepts(null) : Recipe.Outputs.Keys.Any(other.Accepts));
    }

    /// <summary>The tile's buildings plus a stack of planned ones not placed yet.</summary>
    sealed class Layout(Simulation sim, TileState tile)
    {
        readonly TileGrid _g = tile.Grid;
        readonly List<Node> _planned = [];
        readonly Dictionary<int, Node> _real = [];

        public Node? Real(int id)
        {
            if (_real.TryGetValue(id, out var n))
                return n;
            var b = _g.Buildings.FirstOrDefault(b => b.Id == id);
            return b == null ? null : Wrap(b);
        }

        Node Wrap(PlacedBuilding b)
        {
            if (!_real.TryGetValue(b.Id, out var n))
            {
                n = new Node(b.X, b.Y, b.Width, b.Height, sim.Content.Buildings[b.DefId], b.Machine?.Recipe, b);
                _real[b.Id] = n;
            }
            return n;
        }

        public void Push(Node n) => _planned.Add(n);

        public void Pop() => _planned.RemoveAt(_planned.Count - 1);

        public (int X, int Y) CoreCentre()
        {
            var core = _g.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Core);
            return core == null ? (_g.Width / 2, _g.Height / 2) : (core.X + core.Width / 2, core.Y + core.Height / 2);
        }

        Node? At(int x, int y)
        {
            foreach (var n in _planned)
            {
                if (x >= n.X && x < n.X + n.W && y >= n.Y && y < n.Y + n.H)
                    return n;
            }
            return _g.BuildingAt(x, y) is { } b ? Wrap(b) : null;
        }

        bool Free(int x, int y) => _g.InBounds(x, y) && _g.Buildable(x, y) && !_g.HasFrame(x, y) && At(x, y) == null;

        /// <summary>Top-left positions worth trying for <paramref name="def"/>, best first.</summary>
        public IEnumerable<(int X, int Y)> Candidates(BuildingDef def, string? onDeposit, Node? touching, int nearX, int nearY)
        {
            int w = def.Width, h = def.Height;
            IEnumerable<(int X, int Y)> all;
            if (touching != null)
            {
                var list = new List<(int, int)>();
                for (int y = touching.Y - h; y <= touching.Y + touching.H; y++)
                {
                    for (int x = touching.X - w; x <= touching.X + touching.W; x++)
                    {
                        bool side = x + w == touching.X || x == touching.X + touching.W || y + h == touching.Y || y == touching.Y + touching.H;
                        bool corner = (x + w == touching.X || x == touching.X + touching.W) && (y + h == touching.Y || y == touching.Y + touching.H);
                        if (side && !corner)
                            list.Add((x, y));
                    }
                }
                all = list;
            }
            else if (onDeposit != null)
            {
                var set = new SortedSet<(int, int)>();
                for (int y = 0; y < _g.Height; y++)
                {
                    for (int x = 0; x < _g.Width; x++)
                    {
                        if (_g.InBounds(x, y) && _g.DepositAt(x, y) == onDeposit)
                        {
                            for (int dy = 0; dy < h; dy++)
                                for (int dx = 0; dx < w; dx++)
                                    set.Add((x - dx, y - dy));
                        }
                    }
                }
                all = set;
            }
            else
            {
                return Nearest(_g.Width, _g.Height, nearX - w / 2, nearY - h / 2);
            }
            return all.OrderBy(p => Dist(p.X + w / 2, p.Y + h / 2, nearX, nearY)).ThenBy(p => p.Y).ThenBy(p => p.X);
        }

        static long Dist(int ax, int ay, int bx, int by) => (long)(ax - bx) * (ax - bx) + (long)(ay - by) * (ay - by);

        /// <summary>
        /// The building as a node if it may go at (x, y): legal now (cost aside), clear of planned buildings, and trading
        /// items only with <paramref name="feeder"/>, <paramref name="target"/> and cores.
        /// </summary>
        public Node? TryNode(int player, BuildingDef def, int x, int y, string? recipeId, string? onDeposit, Node? feeder, Node? target)
        {
            var r = Placement.Check(sim, tile, player, def.Id, x, y, 0, recipeId, out var plan);
            if (r is not (PlaceResult.Ok or PlaceResult.CannotAfford))
                return null;
            var recipe = plan.RecipeId == null ? null : sim.Content.Recipes[plan.RecipeId];
            if (onDeposit != null && recipe?.Outputs.ContainsKey(onDeposit) != true)
                return null;
            for (int cy = y; cy < y + plan.Height; cy++)
            {
                for (int cx = x; cx < x + plan.Width; cx++)
                {
                    if (At(cx, cy) != null)
                        return null;
                }
            }
            var node = new Node(x, y, plan.Width, plan.Height, def, recipe, null);
            foreach (var (px, py) in Perimeter(x, y, plan.Width, plan.Height))
            {
                if (At(px, py) is not { } other || other == feeder || other == target || other.Def.Kind == BuildingKind.Core)
                    continue;
                if (node.Feeds(other) || other.Feeds(node))
                    return null;
            }
            return node;
        }

        /// <summary>Places <paramref name="stages"/> after <paramref name="start"/>, then a belt to the target.</summary>
        public List<PlannedBuilding>? Extend(int player, Node start, IReadOnlyList<Stage> stages, Node target, List<PlannedBuilding> plan)
        {
            var prev = start;
            int pushed = 0;
            try
            {
                for (int i = 0; i < stages.Count; i++)
                {
                    var stage = stages[i];
                    if (!sim.Content.Buildings.TryGetValue(stage.BuildingId, out var def))
                        return null;
                    var (tx, ty) = (target.X + target.W / 2, target.Y + target.H / 2);
                    Node? node = null;
                    foreach (var (x, y) in Candidates(def, null, prev, tx, ty))
                    {
                        node = TryNode(player, def, x, y, stage.RecipeId, null, prev, i == stages.Count - 1 ? target : null);
                        if (node != null)
                            break;
                    }
                    if (node == null)
                        return null;
                    Push(node);
                    pushed++;
                    plan.Add(new PlannedBuilding(def.Id, node.X, node.Y, 0, stage.RecipeId));
                    prev = node;
                }
                if (Route(prev, target) is not { } belt)
                    return null;
                plan.AddRange(belt.Select(s => new PlannedBuilding("conveyor", s.X, s.Y, s.Rotation, null)));
                return plan;
            }
            finally
            {
                for (int i = 0; i < pushed; i++)
                    Pop();
            }
        }

        static bool Touches(Node n, int x, int y) =>
            (x >= n.X && x < n.X + n.W && (y == n.Y - 1 || y == n.Y + n.H)) || (y >= n.Y && y < n.Y + n.H && (x == n.X - 1 || x == n.X + n.W));

        /// <summary>A cell a belt of the from-to line may use: free, and no other building beside it pushes items.</summary>
        bool BeltCell(int x, int y, Node from, Node to)
        {
            if (!Free(x, y))
                return false;
            foreach (var d in Directions.All)
            {
                if (At(x + d.Dx(), y + d.Dy()) is { } n && n != from && n != to && n.Dumps)
                    return false;
            }
            return true;
        }

        public List<BeltStep>? Route(Node from, Node to)
        {
            if (Perimeter(from.X, from.Y, from.W, from.H).Any(p => At(p.X, p.Y) == to))
                return [];
            int w = _g.Width, h = _g.Height;
            var prev = new int[w * h];
            Array.Fill(prev, -2);
            var queue = new Queue<int>();
            foreach (var (x, y) in Perimeter(from.X, from.Y, from.W, from.H))
            {
                if (_g.InRect(x, y) && prev[y * w + x] == -2 && BeltCell(x, y, from, to))
                {
                    prev[y * w + x] = -1;
                    queue.Enqueue(y * w + x);
                }
            }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % w, y = i / w;
                if (Touches(to, x, y))
                    return Steps(prev, i, to);
                foreach (var d in Directions.All)
                {
                    int nx = x + d.Dx(), ny = y + d.Dy();
                    if (!_g.InRect(nx, ny) || prev[ny * w + nx] != -2 || !BeltCell(nx, ny, from, to))
                        continue;
                    prev[ny * w + nx] = i;
                    queue.Enqueue(ny * w + nx);
                }
            }
            return null;
        }

        List<BeltStep> Steps(int[] prev, int end, Node to)
        {
            int w = _g.Width;
            var cells = new List<(int X, int Y)>();
            for (int i = end; i >= 0; i = prev[i])
                cells.Add((i % w, i / w));
            cells.Reverse();
            var steps = new List<BeltStep>(cells.Count);
            for (int k = 0; k < cells.Count; k++)
            {
                var (x, y) = cells[k];
                Direction dir;
                if (k + 1 < cells.Count)
                    dir = Directions.All.First(d => x + d.Dx() == cells[k + 1].X && y + d.Dy() == cells[k + 1].Y);
                else
                    dir = Directions.All.First(d => At(x + d.Dx(), y + d.Dy()) == to);
                steps.Add(new BeltStep(x, y, Rotation(dir)));
            }
            return steps;
        }

        static int Rotation(Direction d) => d switch { Direction.East => 0, Direction.South => 1, Direction.West => 2, _ => 3 };
    }
}
