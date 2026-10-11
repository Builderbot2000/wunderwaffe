using System.Collections.Generic;
using System.Linq;
using Godot;
using Wunderwaffe.Sim;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Screens;

/// <summary>
/// Left panel, world map only: the selected tile's profile, deposits and either its inventory and ports (yours) or
/// what claiming it costs. Rebuilt only when what it shows changes (<c>_sideKey</c>).
/// </summary>
public partial class Hud
{
    TileState SelectedWorldTile() => Sim.TryGetTile(WorldMap.SelectedTileId) ?? _host.ViewedTile;

    /// <summary>Sends the selected units to the selected tile's victory circle, to land there and claim it (WM-006).</summary>
    void SendToCircle()
    {
        var tile = SelectedWorldTile();
        if (_host.Selected.Count > 0)
            _host.Send(new MoveUnitsCommand(Me, [.. _host.Selected.Order()], tile.Id, tile.CircleX, tile.CircleY));
    }

    static string InventoryKey(TileState tile) => string.Join(",", tile.Inventory.Entries.Select(kv => kv.Key + kv.Value));

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
        bool mine = tile.Owner == Me;
        bool contested = Territory.IsContested(Sim, tile);
        _claim.Visible = !mine;
        _claim.Disabled = _host.Selected.Count == 0;
        _enter.Visible = Territory.HasPresence(Sim, Me, tile);

        string progress = string.Join(",", tile.ClaimTicks.Select(kv => $"{kv.Key}:{kv.Value / Simulation.TicksPerSecond}"));
        string key = $"w{tile.Id}/{tile.Owner}/{contested}/{progress}/{_enter.Visible}/{tile.Grid.BuildingCount}/" + (mine ? InventoryKey(tile) + LinkKey(tile) : "");
        if (key == _sideKey)
            return;
        _sideKey = key;
        Ui.Clear(_sideInfo);

        var head = Ui.Row();
        var name = Ui.Label(tile.Name, 20);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(name);
        if (contested)
            head.AddChild(Ui.Label("Contested", Ui.FontSize, Ui.Warn));
        head.AddChild(mine ? Ui.Label("Yours", Ui.FontSize, Ui.Good)
            : tile.Owner == TileState.Unowned ? Ui.Label("Unclaimed", Ui.FontSize, Ui.Muted)
            : Ui.Label($"Player {tile.Owner}", Ui.FontSize, Ui.Bad));
        _sideInfo.AddChild(head);
        AddProfile(tile);
        AddDeposits(tile);
        AddCircle(tile, mine);

        if (mine)
        {
            AddInventory(tile);
            AddPorts(tile);
        }
    }

    /// <summary>Who is claiming the tile's victory circle and how far along they are (WM-006), and how to claim it.</summary>
    void AddCircle(TileState tile, bool mine)
    {
        _sideInfo.AddChild(Ui.Heading("Victory circle"));
        int capture = Expansion.CaptureTicks(Sim);
        foreach (var (player, ticks) in tile.ClaimTicks)
        {
            string who = player == Me ? "You" : $"Player {player}";
            _sideInfo.AddChild(Ui.Label($"{who}: {ticks * 100 / capture}% claimed", Ui.SmallFont, player == Me ? Ui.Good : Ui.Bad));
        }
        string help = mine
            ? "Yours. A rival who keeps a building in the circle for the capture time takes the tile."
            : Territory.HasPresence(Sim, Me, tile)
                ? $"Keep a building of yours in the circle for {capture / Simulation.TicksPerSecond} s to claim the tile."
                : "Select a unit and press C to send it to the circle, then G to land it there.";
        _sideInfo.AddChild(Ui.Label(help, Ui.SmallFont, Ui.Muted, SideWidth - 24));
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

    static string LinkKey(TileState tile) => string.Join(",", tile.Neighbors.Select(n => tile.Stats.SentPerMinute(n)));

    /// <summary>One line per I/O zone that has ports: the neighbour it links to and its ports each way.</summary>
    void AddPorts(TileState tile)
    {
        var lines = new List<string>();
        foreach (int n in tile.Neighbors)
        {
            int exports = tile.Grid.ExportPorts.Count(p => PortRules.PartnerOf(tile.Grid, p) == n);
            int imports = tile.Grid.ImportPorts.Count(p => PortRules.PartnerOf(tile.Grid, p) == n);
            if (exports + imports > 0)
                lines.Add($"To {Sim.Tiles[n].Name}: {exports} out, {imports} in · sends {tile.Stats.SentPerMinute(n)}/min, "
                    + $"receives {Sim.Tiles[n].Stats.SentPerMinute(tile.Id)}/min · {PortRules.TransitTicksPerTile / Simulation.TicksPerSecond} s transit");
        }
        if (lines.Count == 0)
            return;
        _sideInfo.AddChild(Ui.Heading("Ports"));
        _sideInfo.AddChild(Ui.Label(string.Join("\n", lines), Ui.SmallFont, Ui.Text));
    }
}
