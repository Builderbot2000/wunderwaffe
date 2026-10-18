using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Sim.World;

public enum PlaceResult
{
    Ok,
    NoSuchBuilding,
    OutOfBounds,
    Blocked,
    NeedsDeposit,
    InvalidRecipe,
    CannotAfford,

    /// <summary>Ports only fit wholly inside one I/O zone (see <see cref="PortRules"/>).</summary>
    NotInPortZone,

    /// <summary>Some cell of the footprint is terrain buildings can't stand on, such as water or peaks (FB-002).</summary>
    BadTerrain,

    /// <summary>A wreck lies on the footprint; deconstruct it first (CB-006).</summary>
    Wreck,

    /// <summary>The player has no building or unit on the tile and doesn't own it (WM-015).</summary>
    NoPresence,

    /// <summary>The footprint touches an enemy-owned victory circle while a building not of the player's stands in it (WM-015).</summary>
    EnemyCircle,
}

/// <summary>A placement that passed every check, ready to apply. The player's cores on the tile pay (<see cref="Funds"/>).</summary>
public readonly record struct PlacementPlan(BuildingDef Def, int X, int Y, int Width, int Height, int Rotation, string? RecipeId);

/// <summary>
/// Building placement rules (FB-002, FB-003). Read-only, so the UI can use the same check to preview
/// placement as the command uses to apply it.
/// </summary>
public static class Placement
{
    public static PlaceResult Check(
        Simulation sim, TileState tile, int player, string buildingId, int x, int y, int rotation,
        string? recipeId, out PlacementPlan plan, bool ignoreCost = false)
    {
        plan = default;
        var content = sim.Content;
        if (!Territory.HasPresence(sim, player, tile))
            return PlaceResult.NoPresence;
        if (!content.Buildings.TryGetValue(buildingId, out var def))
            return PlaceResult.NoSuchBuilding;
        rotation &= 3;
        var (w, h) = def.Footprint(rotation);

        TileGrid grid = tile.Grid;
        if (!grid.InBounds(x, y, w, h))
            return PlaceResult.OutOfBounds;
        // Layer buildings (frame, cables, platforms) follow their layer's stacking rules (FB-022).
        int layer = content.Layers.Of(def).Layer;
        if (layer >= 0)
        {
            if (Fits(content, grid, layer, x, y) is var fit and not PlaceResult.Ok)
                return fit;
        }
        else if (!grid.IsFree(x, y, w, h) || BlockedByLayer(grid, x, y, w, h))
        {
            return PlaceResult.Blocked;
        }
        else if (!grid.Buildable(x, y, w, h) && !(def.OnWater && StandsOn(content, grid, x, y, w, h)))
        {
            return PlaceResult.BadTerrain;
        }
        if (!grid.WreckFree(x, y, w, h))
            return PlaceResult.Wreck;
        if (Territory.EnemyCircleBlocks(tile, player, x, y, w, h))
            return PlaceResult.EnemyCircle;

        if (def.Kind is BuildingKind.ExportPort or BuildingKind.ImportPort && PortRules.ZoneOf(grid, x, y, w, h) < 0)
            return PlaceResult.NotInPortZone;

        var resolved = ResolveRecipe(sim, grid, def, x, y, w, h, recipeId);
        if (resolved.Error != PlaceResult.Ok)
            return resolved.Error;

        // Filled in before the cost check too, so planners can lay out what they can't pay for yet. The cost is
        // ignored when a caller pays a total at once (a blueprint). Only the player's cores on this tile pay (FB-019).
        plan = new PlacementPlan(def, x, y, w, h, rotation, resolved.RecipeId);
        if (!ignoreCost && !Funds.CanPay(tile, player, def.Cost))
            return PlaceResult.CannotAfford;
        return PlaceResult.Ok;
    }

    /// <summary>
    /// Whether a cell of the tile takes an entry of <paramref name="layer"/> by its stacking rules (FB-022): the layer is
    /// empty there, no building stands there unless the layer goes <see cref="LayerDef.UnderBuildings"/>, and the cell
    /// has something the layer <see cref="LayerDef.SitsOn"/>. Presence, wrecks, circles and cost are the caller's.
    /// </summary>
    public static PlaceResult Fits(ContentDb content, TileGrid grid, int layer, int x, int y)
    {
        if (!grid.InBounds(x, y))
            return PlaceResult.OutOfBounds;
        if (grid.Has(layer, x, y) || (!grid.Layers[layer].Def.UnderBuildings && grid.BuildingAt(x, y) != null))
            return PlaceResult.Blocked;
        return Supported(content, grid, layer, x, y, -1) ? PlaceResult.Ok : PlaceResult.BadTerrain;
    }

    /// <summary>
    /// Whether the cell has something <paramref name="layer"/> sits on, counting layer <paramref name="without"/> as empty
    /// (to see what removing it would leave).
    /// </summary>
    internal static bool Supported(ContentDb content, TileGrid grid, int layer, int x, int y, int without)
    {
        foreach (var on in grid.Layers[layer].Def.SitsOn ?? [])
        {
            bool ok = on switch
            {
                LayerDef.Ground => grid.BuildableWithout(x, y, without),
                LayerDef.Water => content.TerrainList[grid.TerrainAt(x, y)].Water,
                _ => grid.Layers.ById(on) is { } below && below.Index != without && grid.Has(below.Index, x, y),
            };
            if (ok)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Whether the entry of <paramref name="layer"/> at a cell may be taken up: no layer above it or building on it loses
    /// what it stands on.
    /// </summary>
    internal static bool CanLift(ContentDb content, TileGrid grid, int layer, int x, int y)
    {
        for (int above = layer + 1; above < grid.Layers.Count; above++)
        {
            if (grid.Has(above, x, y) && !Supported(content, grid, above, x, y, layer))
                return false;
        }
        return grid.BuildingAt(x, y) == null || grid.BuildableWithout(x, y, layer);
    }

    /// <summary>A cell of the footprint carries a layer marked <see cref="LayerDef.BlocksBuildings"/>.</summary>
    static bool BlockedByLayer(TileGrid grid, int x, int y, int w, int h)
    {
        foreach (var layer in grid.Layers.All)
        {
            if (!layer.Def.BlocksBuildings || grid.CountOn(layer.Index) == 0)
                continue;
            for (int cy = y; cy < y + h; cy++)
            {
                for (int cx = x; cx < x + w; cx++)
                {
                    if (grid.Has(layer.Index, cx, cy))
                        return true;
                }
            }
        }
        return false;
    }

    /// <summary>Every cell is buildable ground or water, for buildings marked <see cref="BuildingDef.OnWater"/>.</summary>
    static bool StandsOn(ContentDb content, TileGrid grid, int x, int y, int w, int h)
    {
        for (int cy = y; cy < y + h; cy++)
        {
            for (int cx = x; cx < x + w; cx++)
            {
                if (!grid.Buildable(cx, cy) && !content.TerrainList[grid.TerrainAt(cx, cy)].Water)
                    return false;
            }
        }
        return true;
    }

    readonly record struct Resolved(PlaceResult Error, string? RecipeId);

    static Resolved ResolveRecipe(Simulation sim, TileGrid grid, BuildingDef def, int x, int y, int w, int h, string? recipeId)
    {
        var all = sim.Content.RecipesByBuilding.GetValueOrDefault(def.Id) ?? [];

        if (recipeId != null)
        {
            var recipe = all.FirstOrDefault(r => r.Id == recipeId);
            if (recipe == null)
                return new(PlaceResult.InvalidRecipe, null);
            if (recipe.Inputs.Count == 0 && CountDeposits(grid, x, y, w, h, recipe.Outputs.Keys.First()) == 0)
                return new(PlaceResult.NeedsDeposit, null);
            return new(PlaceResult.Ok, recipe.Id);
        }

        var extraction = all.Where(r => r.Inputs.Count == 0).ToList();
        if (extraction.Count > 0)
        {
            // Mine whichever extractable deposit covers most of the footprint; ties go to the lowest id.
            RecipeDef? best = null;
            int bestCount = 0;
            foreach (var r in extraction)
            {
                int n = CountDeposits(grid, x, y, w, h, r.Outputs.Keys.First());
                if (n > bestCount)
                {
                    best = r;
                    bestCount = n;
                }
            }
            return best == null ? new(PlaceResult.NeedsDeposit, null) : new(PlaceResult.Ok, best.Id);
        }

        return all.Count switch
        {
            0 => new(PlaceResult.Ok, null),
            1 => new(PlaceResult.Ok, all[0].Id),
            _ => new(PlaceResult.InvalidRecipe, null),
        };
    }

    static int CountDeposits(TileGrid grid, int x, int y, int w, int h, string itemId)
    {
        int n = 0;
        for (int cy = y; cy < y + h; cy++)
        {
            for (int cx = x; cx < x + w; cx++)
            {
                if (grid.DepositAt(cx, cy) == itemId)
                    n++;
            }
        }
        return n;
    }
}
