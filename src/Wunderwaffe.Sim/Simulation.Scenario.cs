using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;
using Wunderwaffe.Sim.World.Gen;

namespace Wunderwaffe.Sim;

/// <summary>Scenarios (WM-017): building the world and the seats' starting state from a file, and saving one back.</summary>
public sealed partial class Simulation
{
    /// <summary>
    /// Builds the world and each seat's starting state from <paramref name="s"/>: the map, tiles (names, circles, zones,
    /// cell layers), owned tiles, buildings (by the placement rules, free) and units. A seat with no buildings or units
    /// gets the setup's start prefabs on its first tile. Throws <see cref="ContentException"/> with every problem found.
    /// </summary>
    internal void LoadScenario(ScenarioDef s)
    {
        if (_tiles.Count > 0)
            throw new InvalidOperationException("the world already has tiles");
        var errors = new List<string>();
        string at = $"scenario '{s.Id}'";
        void Fail()
        {
            if (errors.Count > 0)
                throw new ContentException(errors);
        }

        int w = s.Width, h = s.Height;
        var legend = new byte[s.Terrain.Legend?.Length ?? 0];
        if (s.Terrain.Legend == null)
            errors.Add($"{at}: terrain needs a legend of terrain ids");
        for (int i = 0; i < legend.Length; i++)
        {
            int t = Content.TerrainList.ToList().FindIndex(d => d.Id == s.Terrain.Legend![i]);
            if (t < 0)
                errors.Add($"{at}: unknown terrain '{s.Terrain.Legend![i]}'");
            legend[i] = (byte)Math.Max(0, t);
        }
        var cells = s.Terrain.Decode(w, h, 0, 0, $"{at} terrain", errors);
        var tileOf = s.TileMap.Decode(w, h, -1, s.Tiles.Count - 1, $"{at} tileMap", errors);
        Fail();

        var terrain = cells!.Select(v => legend[v]).ToArray();
        short[] elevation;
        try
        {
            elevation = s.Elevation is { } e ? Scenarios.DecodeElevation(e, w * h)
                : terrain.Select(t => (short)(Content.TerrainList[t].Water ? TerrainGen.SeaLevel / 2 : TerrainGen.LowlandElevation)).ToArray();
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException)
        {
            errors.Add($"{at}: bad elevation: {ex.Message}");
            elevation = [];
        }
        Fail();
        _map = new WorldMap(w, h, terrain, elevation, tileOf!);
        if (!CreateTiles(s.Tiles.Count))
            errors.Add($"{at}: every tile id 0 to {s.Tiles.Count - 1} needs cells in tileMap");
        Fail();

        foreach (var tile in _tiles)
            LoadTile(tile, s.Tiles[tile.Id], $"{at} tile {tile.Id}", errors);
        Fail();

        for (int p = 0; p < s.Seats.Count; p++)
            Player(p);
        for (int p = 0; p < s.Seats.Count; p++)
            LoadSeat(p, s.Seats[p], $"{at} seat {p}", errors);
        Fail();
    }

    void LoadTile(TileState tile, ScenarioTile st, string at, List<string> errors)
    {
        var grid = tile.Grid;
        if (!string.IsNullOrWhiteSpace(st.Name))
            tile.Name = st.Name;
        if (!grid.InBounds(st.CircleX, st.CircleY))
            errors.Add($"{at}: victory circle centre ({st.CircleX}, {st.CircleY}) is not on the tile");
        (tile.CircleX, tile.CircleY) = (st.CircleX, st.CircleY);

        if (st.Zones is { } z && z.Decode(grid.Width, grid.Height, -1, _tiles.Count - 1, $"{at} zones", errors) is { } zones)
        {
            for (int i = 0; i < zones.Length; i++)
            {
                if (zones[i] >= 0 && (!grid.InBounds(i % grid.Width, i / grid.Width) || !tile.Neighbors.Contains(zones[i])))
                {
                    errors.Add($"{at}: zone {zones[i]} at ({i % grid.Width}, {i / grid.Width}) is not a neighbour on the tile");
                    break;
                }
            }
            grid.SetZones(zones);
        }

        // Every layer the same way (FB-022): no per-layer code.
        foreach (var (id, layerCells) in st.Layers ?? [])
        {
            if (Content.Layers.ById(id) is not { } layer)
            {
                errors.Add($"{at}: unknown layer '{id}'");
                continue;
            }
            var entries = (layerCells.Legend ?? []).Select(e => e is "" ? 0 : layer.EntryOf(e)).ToArray();
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] == 0 && layerCells.Legend![i] is not "")
                    errors.Add($"{at}: '{layerCells.Legend[i]}' does not go on layer '{id}'");
            }
            if (layerCells.Legend == null)
                errors.Add($"{at}: layer '{id}' needs a legend");
            else if (layerCells.Decode(grid.Width, grid.Height, 0, 0, $"{at} layer '{id}'", errors) is { } values)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    int x = i % grid.Width, y = i / grid.Width, e = entries[values[i]];
                    if (e == 0)
                        continue;
                    if (!grid.InBounds(x, y))
                    {
                        errors.Add($"{at}: layer '{id}' has '{layerCells.Legend[values[i]]}' at ({x}, {y}), off the tile");
                        break;
                    }
                    grid.SetEntry(layer.Index, x, y, e);
                }
            }
        }
        LoadPatches(grid, st, at, errors);
    }

    /// <summary>Deposit patches (RS-004) as the file has them, or found from the deposits if it has none.</summary>
    void LoadPatches(TileGrid grid, ScenarioTile st, string at, List<string> errors)
    {
        if (st.Patches is not { } patches)
        {
            grid.Patches.Find(Content);
            return;
        }
        int before = errors.Count;
        foreach (var p in patches)
        {
            if (!Content.Items.TryGetValue(p.Item ?? "", out var item) || item.Deposit == null || p.Cells <= 0 || p.Start <= 0 || p.Amount < 0 || p.Amount > p.Start)
                errors.Add($"{at}: patch of '{p.Item}' needs an item with a deposit, cells > 0 and 0 <= amount <= start > 0");
        }
        if (st.PatchMap is null)
            errors.Add($"{at}: patches need a patchMap");
        var ids = errors.Count == before ? st.PatchMap!.Decode(grid.Width, grid.Height, 0, patches.Count, $"{at} patchMap", errors) : null;
        if (ids == null)
            return;
        var runs = new List<int>();
        for (int i = 0; i < ids.Length;)
        {
            int j = i;
            while (j < ids.Length && ids[j] == ids[i])
                j++;
            if (ids[i] != 0)
            {
                for (int k = i; k < j; k++)
                {
                    if (grid.DepositAt(k % grid.Width, k / grid.Width) != patches[ids[i] - 1].Item)
                    {
                        errors.Add($"{at}: patch {ids[i]} covers ({k % grid.Width}, {k / grid.Width}), which has no {patches[ids[i] - 1].Item} deposit");
                        return;
                    }
                }
                runs.AddRange([i, j - i, ids[i]]);
            }
            i = j;
        }
        grid.Patches.Restore(Content, [.. patches.Select(p => (p.Item, p.Cells, p.Start, p.Amount))], runs);
    }

    void LoadSeat(int p, ScenarioSeat seat, string at, List<string> errors)
    {
        foreach (int id in seat.Tiles ?? [])
        {
            if (TryGetTile(id) is not { } tile)
                errors.Add($"{at}: no tile {id}");
            else if (tile.Owner != TileState.Unowned)
                errors.Add($"{at}: tile {id} already belongs to seat {tile.Owner}");
            else
                tile.Owner = p;
        }

        foreach (var b in seat.Buildings ?? [])
        {
            string where = $"{at}: '{b.DefId}' at ({b.X}, {b.Y}) on tile {b.Tile}";
            if (TryGetTile(b.Tile) is not { } tile || !Content.Buildings.TryGetValue(b.DefId ?? "", out var def))
            {
                errors.Add($"{where}: no such tile or building");
                continue;
            }
            if (def.Layer != null || b.Rotation is < 0 or > 3 || b.Hp is <= 0 || b.Hp > def.Hp)
            {
                errors.Add($"{where}: layer buildings go in the tile's layers; rotation 0 to 3; hp 1 to {def.Hp}");
                continue;
            }
            // The placement rules, but for free and with the seat counted as present: the scenario puts it there.
            int owner = tile.Owner;
            tile.Owner = p;
            var result = Placement.Check(this, tile, p, def.Id, b.X, b.Y, b.Rotation, b.RecipeId, out var plan, ignoreCost: true, ignoreRadius: true, ignoreFaction: true);
            tile.Owner = owner;
            if (result != PlaceResult.Ok)
            {
                errors.Add($"{where}: {result}");
                continue;
            }
            var recipe = plan.RecipeId == null ? null : Content.Recipes[plan.RecipeId];
            var placed = tile.Grid.Add(def, p, plan.X, plan.Y, plan.Width, plan.Height, plan.Rotation, recipe, b.Hp);
            if (b.Stock is { Count: > 0 } stock)
                AddItems(placed.Stock, stock, where, errors);
        }

        foreach (var u in seat.Units ?? [])
            LoadUnit(p, u, $"{at}: unit at ({u.X}, {u.Y}) on tile {u.Tile}", errors);

        int start = seat.Tiles is { Count: > 0 } t ? t[0]
            : seat.Buildings is { Count: > 0 } bs ? bs[0].Tile
            : seat.Units is { Count: > 0 } us ? us[0].Tile
            : -1;
        if (TryGetTile(start) is not { } home)
        {
            errors.Add($"{at}: has nothing to start with (no tiles, buildings or units)");
            return;
        }
        _startTileIds.Add(home.Id);
        if (seat.Buildings is not { Count: > 0 } && seat.Units is not { Count: > 0 } && errors.Count == 0)
            StampStart(home, p);
    }

    void LoadUnit(int p, ScenarioUnit u, string where, List<string> errors)
    {
        if (TryGetTile(u.Tile) is not { } tile || !tile.Grid.InBounds(u.X, u.Y) || u.Blocks is null || u.Frames is null)
        {
            errors.Add($"{where}: not on the tile, or no blocks and frames");
            return;
        }
        // Checked like a prefab once shifted to (0, 0); then the core must be the block at (0, 0).
        int minX = u.Blocks.Select(b => b.X).Concat(u.Frames.Select(f => f.X)).DefaultIfEmpty().Min();
        int minY = u.Blocks.Select(b => b.Y).Concat(u.Frames.Select(f => f.Y)).DefaultIfEmpty().Min();
        var shifted = new Blueprint(
            u.Blocks.Select(b => b with { X = b.X - minX, Y = b.Y - minY }).ToArray(),
            u.Frames.Select(f => new BlueprintCell(f.X - minX, f.Y - minY)).ToArray());
        var problems = Prefabs.Problems(Content, where, shifted, null);
        if (problems.Count == 0 && !u.Blocks.Any(b => b.X == 0 && b.Y == 0 && Content.Buildings[b.DefId].Kind == BuildingKind.Core))
            problems.Add($"{where}: its core must be the block at (0, 0)");
        if (problems.Count == 0 && u.Blocks.Count(b => Content.Buildings[b.DefId].Kind == BuildingKind.Core) > 1)
            problems.Add($"{where}: a unit has one core");
        if (problems.Count > 0)
        {
            errors.AddRange(problems);
            return;
        }
        var blocks = u.Blocks.Select(b =>
        {
            var def = Content.Buildings[b.DefId];
            var (bw, bh) = def.Footprint(b.Rotation);
            return new UnitBlock(b.DefId, b.X, b.Y, bw, bh, b.Rotation, b.RecipeId, def.Hp);
        }).ToList();
        var unit = UnitAssembly.Spawn(this, tile, p, u.X, u.Y, u.Frames.Select(f => (f.X, f.Y)).ToList(), blocks);
        if (u.Cargo is { Count: > 0 } cargo)
            AddItems(unit.Cargo, cargo, where, errors);
    }

    void AddItems(Inventory? into, SortedDictionary<string, long> items, string where, List<string> errors)
    {
        if (into == null)
        {
            errors.Add($"{where}: only cores hold stock");
            return;
        }
        foreach (var (id, n) in items)
        {
            if (!Content.Items.ContainsKey(id) || n <= 0)
                errors.Add($"{where}: item '{id}' x {n}: unknown item or not > 0");
            else
                into.Add(id, n);
        }
    }

    /// <summary>
    /// The match as a scenario (WM-017): the whole map, each tile's name, circle, zones and cell layers, and per seat its
    /// owned tiles (start tile first), buildings (with core stock and damage) and units (at rest, heading 0, with cargo).
    /// What's running (belt items, machine buffers, orders, claim timers) is left out: a scenario is a start. A
    /// generated match saved at tick 0 loads back to the same state hash.
    /// </summary>
    public ScenarioDef ToScenario(string id, string name, string? description = null)
    {
        int w = _map.Width, h = _map.Height;
        var terrainIds = Content.TerrainList.Select(t => t.Id).ToArray();
        var tiles = _tiles.Select(t => new ScenarioTile(t.Name, t.CircleX, t.CircleY,
            CellGrid.Encode(t.Grid.Width, t.Grid.Height, t.Grid.ZoneAt), LayersOf(t.Grid),
            [.. t.Grid.Patches.All.Select(p => new PatchRecord(p.Item, p.Cells, p.Start, p.Amount))],
            CellGrid.Encode(t.Grid.Width, t.Grid.Height, t.Grid.Patches.IdAt))).ToList();

        int seats = Math.Max(1, Math.Max(Setup?.Players ?? 0, _players.Count));
        var list = new List<ScenarioSeat>();
        for (int p = 0; p < seats; p++)
        {
            var owned = _tiles.Where(t => t.Owner == p).Select(t => t.Id).ToList();
            if (p < _startTileIds.Count && owned.Remove(_startTileIds[p]))
                owned.Insert(0, _startTileIds[p]);
            var buildings = _tiles.SelectMany(t => t.Grid.Buildings.Where(b => b.Owner == p).Select(b => new ScenarioBuilding(
                t.Id, b.DefId, b.X, b.Y, b.Rotation, b.RecipeId, b.Hp < b.Def.Hp ? b.Hp : null, Items(b.Stock)))).ToList();
            var units = _units.Where(u => u.Owner == p).Select(UnitOf).ToList();
            var faction = FactionOf(p) is var f && f != Content.DefaultFaction ? f.Id : null;
            list.Add(new ScenarioSeat(owned, buildings.Count > 0 ? buildings : null, units.Count > 0 ? units : null, faction));
        }

        return new ScenarioDef(id, name, w, h,
            CellGrid.Encode(w, h, _map.TerrainAt, terrainIds),
            CellGrid.Encode(w, h, _map.TileAtCell),
            tiles, list, description,
            Scenarios.EncodeElevation(i => _map.ElevationAt(i % w, i / w), w * h));
    }

    /// <summary>Each non-empty cell layer, its legend only the entries used.</summary>
    static SortedDictionary<string, CellGrid>? LayersOf(TileGrid g)
    {
        var layers = new SortedDictionary<string, CellGrid>(StringComparer.Ordinal);
        for (int l = 0; l < g.Layers.Count; l++)
        {
            if (g.CountOn(l) == 0)
                continue;
            var layer = g.Layers[l];
            var used = Enumerable.Range(1, layer.Ids.Count - 1).Where(e => g.CountOn(l, e) > 0).ToList();
            var index = new int[layer.Ids.Count];
            for (int i = 0; i < used.Count; i++)
                index[used[i]] = i + 1;
            string[] legend = ["", .. used.Select(e => layer.Ids[e])];
            layers[layer.Id] = CellGrid.Encode(g.Width, g.Height, (x, y) => index[g.EntryAt(l, x, y)], legend);
        }
        return layers.Count > 0 ? layers : null;
    }

    static SortedDictionary<string, long>? Items(Inventory? inventory) =>
        inventory is { } i && i.Entries.Any() ? new SortedDictionary<string, long>(i.Entries.ToDictionary(e => e.Key, e => e.Value), StringComparer.Ordinal) : null;

    /// <summary>A unit as it would stand at heading 0 with its core on the cell the core is over now.</summary>
    ScenarioUnit UnitOf(Unit u)
    {
        var (rx, ry) = Trig.Rotate(u.CoreOffX, u.CoreOffY, u.Angle);
        int gx = (int)IntMath.FloorDiv(u.X + rx, Unit.CellScale), gy = (int)IntMath.FloorDiv(u.Y + ry, Unit.CellScale);
        var tile = TileAtCell(gx, gy) ?? _tiles[u.TileId];
        var blocks = u.Blocks.Select(b => new BlueprintBlock(b.DefId, b.GX - u.CoreGX, b.GY - u.CoreGY, b.Rotation, b.RecipeId)).ToArray();
        var frames = u.Cells.Select(c => new BlueprintCell(c.GX - u.CoreGX, c.GY - u.CoreGY)).ToArray();
        return new ScenarioUnit(tile.Id, gx - tile.OriginX, gy - tile.OriginY, blocks, frames, Items(u.Cargo));
    }
}
