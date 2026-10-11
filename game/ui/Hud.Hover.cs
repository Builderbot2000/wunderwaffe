using System.Collections.Generic;
using System.Linq;
using Godot;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Screens;

/// <summary>Bottom left: the building or deposit under the cursor, or the unit tool's status when nothing is.</summary>
public partial class Hud
{
    void RefreshHover()
    {
        var tile = _host.ViewedTile;
        var cell = BuildTool.HoveredCell();
        bool show = GetViewport().GuiGetHoveredControl() == null && tile.Grid.InBounds(cell.X, cell.Y) && (
            tile.Grid.BuildingAt(cell.X, cell.Y) is { } b ? DescribeBuilding(tile, b)
            : tile.Grid.DepositAt(cell.X, cell.Y) is { } ore && DescribeDeposit(ore));
        if (!show && !BuildTool.HasTool && (_host.Selected.Count > 0 || UnitTool.Status.Contains('\n')))
            show = SetHover(UnitTool.Status);
        _hoverPanel.Visible = show;
    }

    /// <summary>Fills the hover panel with plain text. Returns true, so it can stand in a visibility expression.</summary>
    bool SetHover(string text)
    {
        Ui.Clear(_hover);
        _hover.AddChild(Ui.Label(text, Ui.SmallFont, Ui.Text, SideWidth - 24));
        return true;
    }

    bool DescribeDeposit(string item)
    {
        Ui.Clear(_hover);
        var row = Ui.Row(8);
        row.AddChild(Ui.Icon(item, 28));
        row.AddChild(Ui.Label($"{GameText.ItemName(Content, item)} deposit", 20));
        _hover.AddChild(row);
        _hover.AddChild(Ui.Label("A drill placed on it mines it.", Ui.SmallFont, Ui.Muted));
        return true;
    }

    bool DescribeBuilding(TileState tile, PlacedBuilding b)
    {
        Ui.Clear(_hover);
        var def = Content.Buildings[b.DefId];
        var head = Ui.Row(8);
        head.AddChild(Ui.Icon(b.DefId, 28));
        head.AddChild(Ui.Label(def.Name, 20));
        _hover.AddChild(head);

        void Line(string text, Color? color = null) => _hover.AddChild(Ui.Label(text, Ui.SmallFont, color ?? Ui.Muted, SideWidth - 24));

        switch (b.Kind)
        {
            case BuildingKind.Core:
                Line("The tile's core. Anything delivered here goes into the tile inventory, which pays for building.");
                break;
            case BuildingKind.Conveyor:
                Line($"Carrying {b.Belt!.Count} of {BeltState.Capacity} items, heading {Directions.FromRotation(b.Rotation)}.");
                break;
            case BuildingKind.ExportPort:
                var problem = ExportProblem(tile, b);
                Line(problem ?? $"Paired with {Sim.Tiles[PortRules.PartnerOf(tile.Grid, b)].Name}'s zone facing this tile.",
                    problem == null ? Ui.Good : Ui.Warn);
                Line($"{b.Export!.InFlight.Count} items in transit.");
                break;
            case BuildingKind.ImportPort:
                var from = Sim.TryGetTile(PortRules.PartnerOf(tile.Grid, b));
                Line(from == null ? "Not in an I/O zone: nothing can arrive." : $"Receives from {from.Name}.");
                Line($"Holding {b.Machine!.OutputCount} items for the belt next to it.");
                break;
            case BuildingKind.UnitCore:
            case BuildingKind.Thruster:
                Line("A unit part. Lay frame under it, then hover the unit core with no building picked and press G to lift off.");
                if (b.Kind == BuildingKind.UnitCore)
                    Line("K records the design on its frame to the blueprint library.");
                break;
            case BuildingKind.UnitFactory when b.Factory is { } f:
                Line($"Builds a library blueprint within {UnitFactory.Radius(Sim, tile, b)} cells, up to {UnitFactory.BlockCap(Sim, tile, b)} blocks, " +
                     "from items belts bring it. Click to select it.");
                Line($"{f.Status} · {f.Built} built", f.Status is FactoryStatus.Building ? Ui.Good : Ui.Muted);
                break;
            case BuildingKind.Machine when b.Machine is { Recipe: { } recipe } m:
                Line(GameText.Recipe(Content, recipe), Ui.Text);
                var (status, color) = m.Status switch
                {
                    MachineStatus.Working => ($"Working · {m.Progress * 100 / recipe.Ticks}%", Ui.Good),
                    MachineStatus.Starved => ($"Waiting for {GameText.ItemName(Content, m.MissingInput ?? recipe.Inputs.Keys.First())}", Ui.Warn),
                    MachineStatus.OutputFull => ($"Output full: put a belt or building next to it that takes {GameText.ItemName(Content, recipe.Outputs.Keys.First())}", Ui.Bad),
                    _ => ("Idle", Ui.Muted),
                };
                Line(status, color);
                if (m.Input.Count > 0)
                    AddBuffer("In", m.Input);
                if (m.Output.Count > 0)
                    AddBuffer("Out", m.Output);
                break;
        }
        return true;
    }

    void AddBuffer(string label, IReadOnlyDictionary<string, int> items)
    {
        var row = Ui.Row(6);
        row.AddChild(Ui.Label(label, Ui.SmallFont, Ui.Muted));
        row.AddChild(Ui.Items(items));
        _hover.AddChild(row);
    }

    /// <summary>Why an export port can't send right now, mirroring the sim's checks; null if it can.</summary>
    string? ExportProblem(TileState tile, PlacedBuilding port)
    {
        if (Sim.TryGetTile(PortRules.PartnerOf(tile.Grid, port)) is not { } n)
            return "Not in an I/O zone: nowhere to send items.";
        if (n.Owner != tile.Owner)
            return $"{n.Name} isn't yours. Claim it to send items there.";
        if (!n.Grid.ImportPorts.Any(p => PortRules.PartnerOf(n.Grid, p) == tile.Id))
            return $"No import port in {n.Name}'s zone facing this tile. Items wait here until there is.";
        return null;
    }
}
