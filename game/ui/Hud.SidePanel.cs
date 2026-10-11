using System.Collections.Generic;
using System.Linq;
using Godot;
using Wunderwaffe.Sim;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Screens;

/// <summary>
/// Left panel: on a tile, its inventory; on the world map, the selected tile's profile, deposits and either its
/// inventory and ports (yours) or what claiming it costs. Rebuilt only when what it shows changes (<c>_sideKey</c>).
/// </summary>
public partial class Hud
{
    TileState SelectedWorldTile() => Sim.TryGetTile(WorldMap.SelectedTileId) ?? _host.ViewedTile;

    void ClaimSelected() => _host.Send(new ClaimTileCommand(Me, SelectedWorldTile().Id));

    static string InventoryKey(TileState tile) => string.Join(",", tile.Inventory.Entries.Select(kv => kv.Key + kv.Value));

    void RefreshTileSide()
    {
        var tile = _host.ViewedTile;
        string key = "t" + tile.Id + InventoryKey(tile);
        if (key == _sideKey)
            return;
        _sideKey = key;
        Ui.Clear(_sideInfo);
        AddInventory(tile);
    }

    void AddInventory(TileState tile)
    {
        _sideInfo.AddChild(Ui.Heading($"Inventory · {GameText.Plural(tile.Grid.BuildingCount, "building", "buildings")}"));
        var grid = new GridContainer { Columns = 2, MouseFilter = MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 24);
        foreach (var (id, n) in tile.Inventory.Entries)
        {
            var name = Ui.Row(6);
            name.AddChild(Ui.Icon(id, 24));
            name.AddChild(Ui.Label(GameText.ItemName(Content, id)));
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            grid.AddChild(name);
            var count = Ui.Label(n.ToString("N0"));
            count.HorizontalAlignment = HorizontalAlignment.Right;
            grid.AddChild(count);
        }
        if (grid.GetChildCount() == 0)
            _sideInfo.AddChild(Ui.Label("Empty. Belt items into the core to stock it.", Ui.SmallFont, Ui.Muted));
        else
            _sideInfo.AddChild(grid);
    }

    void RefreshWorldSide()
    {
        var tile = SelectedWorldTile();
        var claim = Expansion.Check(Sim, Me, tile.Id, out var payer);
        bool mine = tile.Owner == Me;
        _claim.Visible = tile.Owner == TileState.Unowned;
        _claim.Disabled = claim != ClaimResult.Ok;
        _enter.Visible = mine;

        string key = $"w{tile.Id}/{claim}/{tile.Owner}/{tile.Grid.BuildingCount}/" + (mine ? InventoryKey(tile) : "");
        if (key == _sideKey)
            return;
        _sideKey = key;
        Ui.Clear(_sideInfo);

        var head = Ui.Row();
        var name = Ui.Label(tile.Name, 20);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(name);
        head.AddChild(mine ? Ui.Label("Yours", Ui.FontSize, Ui.Good)
            : tile.Owner == TileState.Unowned ? Ui.Label("Unclaimed", Ui.FontSize, Ui.Muted)
            : Ui.Label($"Player {tile.Owner}", Ui.FontSize, Ui.Bad));
        _sideInfo.AddChild(head);
        AddProfile(tile);
        AddDeposits(tile);

        if (mine)
        {
            AddInventory(tile);
            AddPorts(tile);
        }
        else if (tile.Owner == TileState.Unowned)
        {
            _sideInfo.AddChild(Ui.Heading("Claim"));
            var cost = Ui.Row(6);
            cost.AddChild(Ui.Label("Cost", Ui.SmallFont, Ui.Muted));
            cost.AddChild(Ui.Items(Balance.ClaimCost));
            _sideInfo.AddChild(cost);
            _sideInfo.AddChild(Ui.Label(claim switch
            {
                ClaimResult.Ok => $"Paid from {payer!.Name}.",
                ClaimResult.CannotAfford => "A neighbouring tile of yours needs this much in its core.",
                ClaimResult.NotAdjacent => "Only tiles next to one of yours can be claimed.",
                _ => "",
            }, Ui.SmallFont, claim == ClaimResult.Ok ? Ui.Good : Ui.Warn, SideWidth - 24));
        }
    }

    /// <summary>The tile's fixed attributes as label/value rows: what its name leaves out.</summary>
    void AddProfile(TileState tile)
    {
        _sideInfo.AddChild(Ui.Heading("Profile"));
        var grid = tile.Grid;
        Stat("Terrain", GameText.TerrainName(Content, tile.Biome));
        Stat("Size", $"{grid.Area} cells");
        Stat("Buildable", $"{grid.BuildableCount * 100 / Mathf.Max(1, grid.Area)}%");
        Stat("Neighbours", tile.Neighbors.Count.ToString());

        var counts = new int[Content.TerrainList.Count];
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                if (grid.InBounds(x, y))
                    counts[grid.TerrainAt(x, y)]++;
            }
        }
        var mix = counts.Select((n, i) => (n, i)).Where(c => c.n * 100 >= grid.Area * 5).OrderByDescending(c => c.n)
            .Select(c => $"{Content.TerrainList[c.i].Name} {c.n * 100 / Mathf.Max(1, grid.Area)}%");
        Stat("Ground", string.Join(", ", mix));

        void Stat(string label, string value)
        {
            var row = Ui.Row(6);
            var key = Ui.Label(label, Ui.SmallFont, Ui.Muted);
            key.CustomMinimumSize = new Vector2(96, 0);
            row.AddChild(key);
            var text = Ui.Label(value, Ui.SmallFont, Ui.Text, SideWidth - 130);
            text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(text);
            _sideInfo.AddChild(row);
        }
    }

    /// <summary>Each deposit with its size, flagging regional ones and saying which regional items are missing (RS-003).</summary>
    void AddDeposits(TileState tile)
    {
        _sideInfo.AddChild(Ui.Heading("Deposits"));
        var deposits = tile.Grid.DepositCounts();
        foreach (var (item, cells) in deposits)
        {
            var row = Ui.Row(6);
            row.AddChild(Ui.Icon(item, 24));
            var label = Ui.Label(GameText.ItemName(Content, item));
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(label);
            if (Content.Items[item].Regional)
                row.AddChild(Ui.Label("only on some tiles", Ui.SmallFont, Ui.Accent));
            row.AddChild(Ui.Label($"{cells} cells", Ui.SmallFont, Ui.Muted));
            _sideInfo.AddChild(row);
        }
        foreach (var item in Content.Items.Values.Where(i => i.Regional && !deposits.ContainsKey(i.Id)))
            _sideInfo.AddChild(Ui.Label($"No {item.Name.ToLowerInvariant()} here.", Ui.SmallFont, Ui.Muted));
    }

    /// <summary>One line per I/O zone that has ports: the neighbour it links to and its ports each way.</summary>
    void AddPorts(TileState tile)
    {
        var lines = new List<string>();
        foreach (int n in tile.Neighbors)
        {
            int exports = tile.Grid.ExportPorts.Count(p => PortRules.PartnerOf(tile.Grid, p) == n);
            int imports = tile.Grid.ImportPorts.Count(p => PortRules.PartnerOf(tile.Grid, p) == n);
            if (exports + imports > 0)
                lines.Add($"To {Sim.Tiles[n].Name}: {exports} out, {imports} in");
        }
        if (lines.Count == 0)
            return;
        _sideInfo.AddChild(Ui.Heading("Ports"));
        _sideInfo.AddChild(Ui.Label(string.Join("\n", lines), Ui.SmallFont, Ui.Text));
    }
}
