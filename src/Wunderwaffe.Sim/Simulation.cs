using System.Text;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Sim;

/// <summary>
/// The authoritative game state, advanced in fixed ticks. Integer-only so every machine reaches the
/// same state from the same commands (NF-004). Godot reads from this; it never writes except via commands.
/// </summary>
public sealed class Simulation(ContentDb content)
{
    public const int TicksPerSecond = 60;

    /// <summary>Local map size in cells. Placeholder until OQ-2 (tile size) is decided.</summary>
    public const int DefaultTileSize = 96;

    /// <summary>Share of a building's cost returned on removal (FB-002).</summary>
    public const int RefundPercent = 100;

    public const string CoreBuildingId = "core";

    /// <summary>What a new player's core starts with (WM-005).</summary>
    public static readonly IReadOnlyDictionary<string, int> StartingItems = new SortedDictionary<string, int>(StringComparer.Ordinal)
    {
        ["copper_ore"] = 300,
        ["iron_ore"] = 150,
    };

    readonly List<TileState> _tiles = [];
    readonly List<Command> _pending = [];

    public ContentDb Content { get; } = content;
    public long Tick { get; private set; }
    public IReadOnlyList<TileState> Tiles => _tiles;

    public TileState AddTile(int x, int y, int owner = TileState.Unowned, int size = DefaultTileSize)
    {
        var tile = new TileState(_tiles.Count, x, y, size, size) { Owner = owner };
        _tiles.Add(tile);
        return tile;
    }

    /// <summary>
    /// Gives a player their first tile: seeded deposits, a core in the middle and starting items (WM-005).
    /// </summary>
    public TileState AddStartingTile(int x, int y, int owner, ulong seed)
    {
        if (!Content.Buildings.TryGetValue(CoreBuildingId, out var core))
            throw new InvalidOperationException($"content has no '{CoreBuildingId}' building");

        var tile = AddTile(x, y, owner);
        TileGenerator.GenerateDeposits(tile.Grid, Content, new Rng(seed));
        int cx = (tile.Grid.Width - core.Width) / 2;
        int cy = (tile.Grid.Height - core.Height) / 2;
        tile.Grid.Add(core.Id, cx, cy, core.Width, core.Height, 0, null);
        foreach (var (id, n) in StartingItems)
            tile.Inventory.Add(id, n);
        return tile;
    }

    public TileState? TryGetTile(int id) => id >= 0 && id < _tiles.Count ? _tiles[id] : null;

    /// <summary>Queues a command to apply at the start of the next <see cref="Step"/>.</summary>
    public void Enqueue(Command command) => _pending.Add(command);

    public void Step()
    {
        foreach (var command in _pending)
            command.Apply(this);
        _pending.Clear();

        foreach (var tile in _tiles)
        {
            // TODO(FB-013): full-mode tiles need the entity simulation (belts, units, combat).
            // Until it exists, they run the same rate step as background tiles.
            StepRate(tile);
        }

        Tick++;
    }

    internal PlaceResult TryPlace(TileState tile, int player, string buildingId, int x, int y, int rotation, string? recipeId)
    {
        var result = Placement.Check(Content, tile, player, buildingId, x, y, rotation, recipeId, out var plan);
        if (result != PlaceResult.Ok)
            return result;

        tile.Inventory.TryTake(plan.Def.Cost);
        var b = tile.Grid.Add(plan.Def.Id, plan.X, plan.Y, plan.Width, plan.Height, plan.Rotation, plan.RecipeId);
        if (b.RecipeId != null)
            tile.AddMachines(b.RecipeId, 1);
        return PlaceResult.Ok;
    }

    internal bool TryRemove(TileState tile, int player, int x, int y)
    {
        if (tile.Owner != player || tile.Grid.BuildingAt(x, y) is not { } b || b.DefId == CoreBuildingId)
            return false;

        tile.Grid.Remove(b);
        if (b.RecipeId != null)
            tile.AddMachines(b.RecipeId, -1);
        foreach (var (id, n) in Content.Buildings[b.DefId].Cost)
            tile.Inventory.Add(id, (long)n * RefundPercent / 100);
        return true;
    }

    void StepRate(TileState tile)
    {
        foreach (var line in tile.Lines.Values)
        {
            RecipeDef recipe = Content.Recipes[line.RecipeId];
            line.Progress += line.Machines;
            while (line.Progress >= recipe.Ticks)
            {
                if (!tile.Inventory.TryTake(recipe.Inputs))
                {
                    // Starved: hold one finished batch of work, don't bank more (FB-014).
                    line.Progress = recipe.Ticks;
                    break;
                }
                foreach (var (id, n) in recipe.Outputs)
                    tile.Inventory.Add(id, n);
                line.Progress -= recipe.Ticks;
            }
        }
    }

    /// <summary>FNV-1a hash of the full state, for desync detection and determinism tests.</summary>
    public ulong StateHash()
    {
        ulong h = 14695981039346656037UL;
        void Mix(long v)
        {
            for (int i = 0; i < 8; i++)
            {
                h ^= (byte)(v >> (i * 8));
                h *= 1099511628211UL;
            }
        }
        void MixStr(string? s)
        {
            if (s == null)
            {
                Mix(-1);
                return;
            }
            foreach (byte b in Encoding.UTF8.GetBytes(s))
            {
                h ^= b;
                h *= 1099511628211UL;
            }
            Mix(s.Length);
        }

        Mix(Tick);
        foreach (var t in _tiles)
        {
            Mix(t.Id);
            Mix(t.Owner);
            Mix((long)t.Mode);
            foreach (var (id, n) in t.Inventory.Entries)
            {
                MixStr(id);
                Mix(n);
            }
            foreach (var b in t.Grid.Buildings)
            {
                Mix(b.Id);
                MixStr(b.DefId);
                Mix(b.X);
                Mix(b.Y);
                Mix(b.Rotation);
                MixStr(b.RecipeId);
            }
            foreach (var line in t.Lines.Values)
            {
                MixStr(line.RecipeId);
                Mix(line.Machines);
                Mix(line.Progress);
            }
        }
        return h;
    }
}
