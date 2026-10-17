using System.Collections.Generic;
using System.Linq;
using Godot;
using Wunderwaffe.Sim;
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
            : tile.Grid.WreckAt(cell.X, cell.Y) is { } wreck ? DescribeWreck(wreck)
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
                Line($"A core. Belts deliver into its stock, up to {def.Capacity:N0} of each item; its owner's cores on this tile pay for building here. " +
                     "On frame it lifts off as a unit with G, taking its stock as cargo; K records the design.");
                Line(b.Stock!.Entries.Any()
                    ? "Holds " + string.Join(", ", b.Stock.Entries.Select(kv => $"{kv.Value:N0} {GameText.ItemName(Content, kv.Key)}"))
                    : "Empty.", Ui.Text);
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
            case BuildingKind.Thruster:
            case BuildingKind.Locomotor:
                Line("A unit part. Lay frame under it and a core, then hover the core with no building picked and press G to lift off.");
                break;
            case BuildingKind.UnitFactory when b.Factory is { } f:
                Line($"Builds a library blueprint within {UnitFactory.Radius(Sim, tile, b)} cells, up to {UnitFactory.BlockCap(Sim, tile, b)} blocks, " +
                     "from items belts bring it. Click to select it.");
                Line($"{f.Status} · {f.Built} built", f.Status is FactoryStatus.Building ? Ui.Good : Ui.Muted);
                break;
            case BuildingKind.Weapon or BuildingKind.Wall or BuildingKind.Shield or BuildingKind.Repair:
                DescribeCombat(tile, b, def, Line);
                break;
            case BuildingKind.Machine when b.Machine is { Recipe: { } recipe } m:
                Line(GameText.Recipe(Content, recipe), Ui.Text);
                if (def.PowerSupply > 0)
                    Line($"Makes {def.PowerSupply} power while it burns.", Ui.Text);
                int speed = PowerNet.SpeedPermille(b) / 10;
                var (status, color) = m.Status switch
                {
                    MachineStatus.Working => ($"Working · {m.ProgressPercent}%" + (speed != 100 ? $" · {speed}% speed" : ""), Ui.Good),
                    MachineStatus.Starved => ($"Waiting for {GameText.ItemName(Content, m.MissingInput ?? recipe.Inputs.Keys.First())}", Ui.Warn),
                    MachineStatus.OutputFull => ($"Output full: put a belt or building next to it that takes {GameText.ItemName(Content, recipe.Outputs.Keys.First())}", Ui.Bad),
                    MachineStatus.NoPower => ("No power: touch it to a core or generator, or run wire to one", Ui.Bad),
                    _ => ("Idle", Ui.Muted),
                };
                Line(status, color);
                if (m.Input.Count > 0)
                    AddBuffer("In", m.Input);
                if (m.Output.Count > 0)
                    AddBuffer("Out", m.Output);
                break;
            case BuildingKind.Wire:
                Line("Joins every power building it touches into one network.");
                break;
            case BuildingKind.Battery when b.Battery is { } bat:
                Line($"Stored {bat.Charge / Simulation.TicksPerSecond} of {bat.Capacity / Simulation.TicksPerSecond} power-seconds · up to {def.Discharge} a tick in or out.", Ui.Text);
                break;
            case BuildingKind.Booster when def.Booster is { } booster:
                Line($"Boosts touching {string.Join(", ", booster.Hosts)}: {GameText.Effects(booster.Effects)}", Ui.Text);
                Line(GameText.BoosterRules(booster));
                break;
            case BuildingKind.Storage when b.Storage is { } store:
                Line($"Holding {store.OutputCount} of {store.Capacity} items. Unloads onto belts leading away.", Ui.Text);
                if (store.Output.Count > 0)
                    AddBuffer("Holds", store.Output);
                DescribeCombat(tile, b, def, Line);
                break;
            case BuildingKind.Junction:
            case BuildingKind.Router:
                Line(b.Kind == BuildingKind.Junction ? "Passes items straight across, so two lines can cross."
                    : "Hands items to its other sides in turn.");
                Line($"Holding {b.Relay!.Count} items.");
                break;
            case BuildingKind.Bridge:
                var link = Logistics.BridgeLink(tile.Grid, b);
                Line(link is { } l ? $"Sends to the bridge {l.Distance} cells ahead." : $"Last of its line: puts items into whatever it faces (range {def.Range}).");
                Line($"Holding {b.Relay!.Count} items.");
                break;
            case BuildingKind.Sorter:
                var filter = b.Sorter!.Filter;
                Line(filter == null ? "Lets nothing through: every item turns aside." : $"Lets {GameText.ItemName(Content, filter)} through; anything else turns aside.", Ui.Text);
                Line("I changes the item.");
                break;
        }
        AddPowerLine(tile, b, def);
        AddBoosts(tile, b);
        if (b.Hp < def.Hp)
            Line($"Damaged: {b.Hp} / {def.Hp} hp", Ui.Warn);
        return true;
    }

    /// <summary>Weapons, walls, shields, repair and storage (FB-010, CB-004, CB-005, FB-018).</summary>
    void DescribeCombat(TileState tile, PlacedBuilding b, BuildingDef def, System.Action<string, Color?> line)
    {
        if (def.Weapon is { } w)
        {
            line($"{Combat.Dps(def)} {w.Type} damage a second, range {w.Range}.", Ui.Text);
            if (w.Ammo != null)
                line($"Loaded {b.Combat!.Ammo} / {w.Magazine} {GameText.ItemName(Content, w.Ammo)}. Belts and the resource radius of a core or depot load it.", null);
            var (text, color) = Combat.StatusOf(b) switch
            {
                WeaponStatus.Firing => ("Firing", Ui.Good),
                WeaponStatus.NoAmmo => ("Out of ammo: it can't fire", Ui.Bad),
                WeaponStatus.NoPower => ("No power: it can't fire", Ui.Bad),
                _ => ("No enemy in range", Ui.Muted),
            };
            line(text, color);
        }
        if (def.Shield is { } s)
            line($"Shield radius {s.Radius}: {b.Combat!.Charge} / {s.Capacity} charge, regains {s.Regen} a second.", Ui.Text);
        if (def.Repair is { } r)
            line($"Repairs your buildings and units within {r.Radius} cells, {r.Rate} hp a second, paying {GameText.Cost(Content, r.Cost)} per {r.Per} hp.", Ui.Text);
        if (b.SupplyRadius > 0)
            line($"Shares this tile's stock (ammo) with your units within {b.SupplyRadius} cells.", Ui.Text);
        if (def.Resist is { Count: > 0 } resist)
            line("Resists " + string.Join(", ", resist.Select(kv => $"{kv.Key} {kv.Value}%")), null);
    }

    bool DescribeWreck(Wreck wreck)
    {
        Ui.Clear(_hover);
        _hover.AddChild(Ui.Label($"Wreck of {Content.Buildings[wreck.DefId].Name}", 20));
        _hover.AddChild(Ui.Label($"Blocks building but not units. Pick any building and right-click it to salvage {Wunderwaffe.Sim.Balance.WreckRefundPercent}% of its cost.",
            Ui.SmallFont, Ui.Muted, SideWidth - 24));
        return true;
    }

    /// <summary>What the building draws and how much of it its network meets (FB-005).</summary>
    void AddPowerLine(TileState tile, PlacedBuilding b, BuildingDef def)
    {
        if (!def.IsPower || def.Kind is BuildingKind.Wire or BuildingKind.Battery)
            return;
        var net = tile.Grid.Power.NetworkOf(b);
        string supply = def.PowerSupply > 0 && def.Kind == BuildingKind.Core ? $"Makes {def.PowerSupply} power. " : "";
        string draw = def.Power > 0 ? $"Draws {(b.Machine is { } m ? BoostStats.Scaled(def.Power, m.Boost.Power) : def.Power)} power. " : "";
        string share = net == null ? "" : $"Network: makes {net.Supply}, uses {net.Demand}, {net.Satisfaction / 10}% met.";
        var color = net == null || net.Satisfaction >= 1000 ? Ui.Muted : net.Satisfaction > 0 ? Ui.Warn : Ui.Bad;
        _hover.AddChild(Ui.Label(supply + draw + share, Ui.SmallFont, color, SideWidth - 24));
    }

    /// <summary>The boosted stats and which boosters give them (FB-016).</summary>
    void AddBoosts(TileState tile, PlacedBuilding b)
    {
        var boosters = Boosters.Of(Sim, tile, b);
        if (boosters.Count == 0)
            return;
        var stats = Boosters.StatsOf(Sim, tile, b);
        _hover.AddChild(Ui.Label("Boosted: " + GameText.Effects(BoosterDef.Stats.ToDictionary(s => s, stats.Get)),
            Ui.SmallFont, Ui.Good, SideWidth - 24));
        var by = boosters.GroupBy(x => x.DefId).Select(g => g.Count() == 1 ? Content.Buildings[g.Key].Name : $"{Content.Buildings[g.Key].Name} x{g.Count()}");
        _hover.AddChild(Ui.Label("By " + string.Join(", ", by), Ui.SmallFont, Ui.Muted, SideWidth - 24));
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
            return $"{n.Name} isn't yours. Claim its victory circle to send items there.";
        if (!n.Grid.ImportPorts.Any(p => PortRules.PartnerOf(n.Grid, p) == tile.Id))
            return $"No import port in {n.Name}'s zone facing this tile. Items wait here until there is.";
        return null;
    }
}
