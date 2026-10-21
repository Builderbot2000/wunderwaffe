using System.Collections.Generic;
using System.Linq;
using Godot;
using Wunderwaffe.Game.Input;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Screens;

/// <summary>
/// The blueprint library panel (B, FB-009): every saved blueprint with its derived stats (UD-002), rename, tags,
/// duplicate, delete, place, build-in-factory and copy as a code (UD-009); prefabs (FB-021) listed read-only after the
/// player's own. And the factory panel (UD-006, WW-004), shown while a unit factory is selected: status and stop reason,
/// production setting (manual or continuous) with trigger and resume, per-stage progress, buffer and what is still missing.
/// </summary>
public partial class Hud
{
    const int LibraryWidth = 520;

    [Export] public BlueprintTool BlueprintTool = null!;

    PanelContainer _library = null!;
    VBoxContainer _libraryList = null!;
    LineEdit _libraryFilter = null!;
    readonly List<Button> _factoryButtons = [];
    int _libraryRevision = -1;
    PanelContainer _factoryPanel = null!;
    VBoxContainer _factoryInfo = null!;

    void BuildBlueprints()
    {
        var col = Ui.Column(8);
        var head = Ui.Row();
        var title = Ui.Label("Blueprint library", 22, Ui.Accent);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(title);
        head.AddChild(Ui.Button("Capture area", () => { BlueprintTool.StartCapture(); _library.Visible = false; }));
        var import = Ui.Button("Import code", ImportCode);
        import.TooltipText = "Add the blueprint whose code is on the clipboard";
        head.AddChild(import);
        head.AddChild(Ui.Button("Close", () => _library.Visible = false));
        col.AddChild(head);
        col.AddChild(Ui.Label("K over a unit core records its design. Blocks can't be edited: rebuild and record again.",
            Ui.SmallFont, Ui.Muted, LibraryWidth - 24));
        _libraryFilter = new LineEdit { PlaceholderText = "Filter by name or tag", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _libraryFilter.TextChanged += _ => _libraryRevision = -1;
        col.AddChild(_libraryFilter);
        _libraryList = Ui.Column(10);
        _libraryList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(LibraryWidth - 24, 520),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        scroll.AddChild(_libraryList);
        col.AddChild(scroll);
        _library = AddLeft(Ui.Panel(col, LibraryWidth));
        _library.Visible = false;

        _factoryInfo = Ui.Column(6);
        _factoryPanel = AddLeft(Ui.Panel(_factoryInfo, SideWidth));
        _factoryPanel.Visible = false;

        BlueprintTool.Notice += (text, good) => Toast(text, good ? Ui.Good : Ui.Warn);
    }

    void ImportCode()
    {
        if (BlueprintLibrary.Import(DisplayServer.ClipboardGet(), out var error) is { } entry)
            Toast($"Imported {entry.Name}", Ui.Good);
        else
            Toast($"Can't import: {error}", Ui.Warn);
    }

    void ToggleLibrary()
    {
        ShowLeftPanel(_library, !_library.Visible && _host.View == ViewMode.Tile);
        _libraryRevision = -1;
        RefreshBlueprints();
    }

    /// <summary>The placement line while a blueprint is picked or an area is being captured.</summary>
    bool ShowBlueprintStatus()
    {
        if (!BlueprintTool.Active)
            return false;
        _placePanel.Visible = true;
        var (text, good) = BlueprintTool.Status;
        _placeLine.Text = text;
        _placeLine.AddThemeColorOverride("font_color", good ? Ui.Text : Ui.Bad);
        return true;
    }

    void RefreshBlueprints()
    {
        bool tile = _host.View == ViewMode.Tile;
        if (!tile)
            _library.Visible = false;
        if (_library.Visible && _libraryRevision != BlueprintLibrary.Revision)
            RebuildLibrary();
        bool factory = BlueprintTool.SelectedFactory != null;
        foreach (var b in _factoryButtons)
            b.Disabled = !factory;
        _factoryPanel.Visible = tile && factory;
        if (_factoryPanel.Visible)
            RefreshFactory(BlueprintTool.SelectedFactory!);
    }

    void RebuildLibrary()
    {
        _libraryRevision = BlueprintLibrary.Revision;
        Ui.Clear(_libraryList);
        _factoryButtons.Clear();
        string filter = _libraryFilter.Text.Trim();
        var entries = BlueprintLibrary.Entries.Where(e => filter.Length == 0
            || e.Name.Contains(filter, System.StringComparison.OrdinalIgnoreCase)
            || e.Tags.Any(t => t.Contains(filter, System.StringComparison.OrdinalIgnoreCase))).ToList();
        if (entries.Count == 0)
            _libraryList.AddChild(Ui.Label(filter.Length == 0 ? "No blueprints yet." : "Nothing matches.", Ui.FontSize, Ui.Muted));
        foreach (var e in entries)
            _libraryList.AddChild(LibraryRow(e));
    }

    Control LibraryRow(BlueprintEntry e)
    {
        var col = Ui.Column(4);
        var bp = e.Blueprint;
        var stats = UnitStats.Of(Content, bp);
        bool unit = bp.IsUnit(Content.Buildings);

        var top = Ui.Row(6);
        if (e.IsPrefab)
        {
            var label = Ui.Label(e.Name, Ui.FontSize, Ui.Text);
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            top.AddChild(label);
            top.AddChild(Ui.Label("PREFAB", Ui.SmallFont, Ui.Muted));
        }
        else
        {
            var name = new LineEdit { Text = e.Name, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            name.TextSubmitted += text => BlueprintLibrary.Rename(e.Id, text);
            name.FocusExited += () => BlueprintLibrary.Rename(e.Id, name.Text);
            top.AddChild(name);
        }
        if (unit && stats.IsWunderwaffe)
            top.AddChild(Ui.Label("WUNDERWAFFE", Ui.SmallFont, Ui.Accent));
        col.AddChild(top);
        // Blueprints are shared across factions (OQ-55): mark those this seat's faction can't build (MP-009).
        if (Query.CheckBlueprintFaction(bp) is not FactionResult.Ok and var misfit)
            col.AddChild(Ui.Label(misfit == FactionResult.NotCaptured ? "Needs a building your faction must capture first"
                : "Doesn't work for your faction", Ui.SmallFont, Ui.Bad));

        string kind = unit ? $"Unit · {GameText.Plural(stats.Cells, "cell", "cells")} · {GameText.Plural(stats.Blocks, "block", "blocks")}"
            : $"{GameText.Plural(bp.Blocks.Length, "building", "buildings")}" + (bp.Frames.Length > 0 ? $" · {bp.Frames.Length} frame" : "");
        col.AddChild(Ui.Label(kind, Ui.SmallFont, Ui.Text));
        var cost = Ui.Row(6);
        cost.AddChild(Ui.Label("Cost", Ui.SmallFont, Ui.Muted));
        cost.AddChild(Ui.Items(stats.Cost));
        col.AddChild(cost);
        if (unit)
        {
            string line = $"HP {stats.MaxHp} · mass {stats.Mass} · power {stats.PowerDraw}/{stats.PowerSupply} · DPS {stats.Dps} · build {GameText.Duration(stats.BuildTicks)}";
            if (stats.Upkeep.Count > 0)
                line += $" · upkeep {GameText.Cost(Content, stats.Upkeep)}/min";
            col.AddChild(Ui.Label(line, Ui.SmallFont, Ui.Muted, LibraryWidth - 40));
        }

        if (e.IsPrefab)
        {
            if (e.Tags.Count > 0)
                col.AddChild(Ui.Label(string.Join(", ", e.Tags), Ui.SmallFont, Ui.Muted));
        }
        else
        {
            var tags = new LineEdit { Text = string.Join(", ", e.Tags), PlaceholderText = "tags, comma separated", SizeFlagsHorizontal = SizeFlags.ExpandFill };
            tags.TextSubmitted += text => BlueprintLibrary.SetTags(e.Id, text);
            tags.FocusExited += () => BlueprintLibrary.SetTags(e.Id, tags.Text);
            col.AddChild(tags);
        }

        var buttons = Ui.Row(6);
        buttons.AddChild(Ui.Button("Place", () => { BlueprintTool.Pick(e); _library.Visible = false; }));
        var factory = Ui.Button("Build in factory", () => { BlueprintTool.PickForFactory(e); _library.Visible = false; });
        factory.TooltipText = "Select one of your unit factories first, then pick where it should build this";
        factory.Disabled = BlueprintTool.SelectedFactory == null;
        _factoryButtons.Add(factory);
        buttons.AddChild(factory);
        buttons.AddChild(Ui.Button("Duplicate", () => BlueprintLibrary.Duplicate(e.Id)));
        var copy = Ui.Button("Copy code", () => { DisplayServer.ClipboardSet(BlueprintLibrary.Export(e)); Toast("Blueprint code copied", Ui.Good); });
        copy.TooltipText = "Copy this blueprint as a text code another player can import";
        buttons.AddChild(copy);
        if (!e.IsPrefab)
            buttons.AddChild(Ui.Button("Delete", () => BlueprintLibrary.Delete(e.Id)));
        col.AddChild(buttons);
        return Ui.Panel(col);
    }

    void RefreshFactory(PlacedBuilding factory)
    {
        var f = factory.Factory!;
        var tile = _host.ViewedTile;
        Ui.Clear(_factoryInfo);
        _factoryInfo.AddChild(Ui.Label("Unit Factory", 20, Ui.Text));
        void Line(string text, Color? color = null) => _factoryInfo.AddChild(Ui.Label(text, Ui.SmallFont, color ?? Ui.Muted, SideWidth - 24));

        Line($"Radius {UnitFactory.Radius(Sim, tile, factory)} · block cap {UnitFactory.BlockCap(Sim, tile, factory)} · speed {UnitFactory.BuildSpeed(Sim, tile, factory)}%");
        var (status, color) = f.Status switch
        {
            FactoryStatus.Idle => ("Idle: pick a blueprint for it to build", Ui.Muted),
            FactoryStatus.Waiting => ("Waiting for items by belt", Ui.Warn),
            FactoryStatus.Building => ("Building", Ui.Good),
            FactoryStatus.Blocked => ("Blocked: something stands where the next block goes", Ui.Bad),
            FactoryStatus.Stopped => ($"Stopped, can't lift off: {GameText.Assemble(f.LiftOff)}. Fix it, then resume.", Ui.Bad),
            FactoryStatus.AwaitingTrigger => ("Done: waiting for Build one", Ui.Muted),
            _ => (f.Status.ToString(), Ui.Muted),
        };
        Line(status, color);
        Line(f.Mode == FactoryMode.Manual ? "Production: manual, one unit per Build one" : "Production: continuous, starts the next copy at once", Ui.Text);
        if (f.Projected is { } bp)
        {
            var stats = UnitStats.Of(Content, bp);
            Line(bp.IsUnit(Content.Buildings)
                ? $"Unit of {GameText.Plural(stats.Cells, "cell", "cells")}{(stats.IsWunderwaffe ? " (Wunderwaffe)" : "")} · {f.Built} built"
                : $"{GameText.Plural(bp.Blocks.Length, "building", "buildings")} · {f.Built} built", Ui.Text);
            foreach (var (stage, done, total) in UnitFactory.StageProgress(f))
            {
                if (total == 0)
                    continue;
                var row = Ui.Row(6);
                var label = Ui.Label($"{stage} {done}/{total}", Ui.SmallFont, done == total ? Ui.Good : Ui.Text);
                label.CustomMinimumSize = new Vector2(130, 0);
                row.AddChild(label);
                row.AddChild(new ProgressBar
                {
                    MaxValue = total, Value = done, ShowPercentage = false,
                    CustomMinimumSize = new Vector2(0, 12), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter,
                });
                _factoryInfo.AddChild(row);
            }
            if (f is { Working: true, Current: { } step })
                Line($"Placing {(step.Block < 0 ? "frame" : Content.Buildings[bp.Blocks[step.Block].DefId].Name)} · {f.Progress / step.Ticks}%", Ui.Text);
            if (f.Buffer.Count > 0)
                AddFactoryItems("Buffer", f.Buffer);
            if (UnitFactory.Missing(f) is { Count: > 0 } missing)
                AddFactoryItems("Needs", missing);
        }
        var buttons = Ui.Row(6);
        buttons.AddChild(Ui.Button("Pick blueprint", () => { ShowLeftPanel(_library, true); _libraryRevision = -1; RefreshBlueprints(); }));
        if (f.Projected != null)
            buttons.AddChild(Ui.Button("Stop", BlueprintTool.StopFactory));
        _factoryInfo.AddChild(buttons);
        var control = Ui.Row(6);
        int me = _host.LocalPlayer, tileId = tile.Id;
        var other = f.Mode == FactoryMode.Manual ? FactoryMode.Continuous : FactoryMode.Manual;
        control.AddChild(Ui.Button($"Make {other.ToString().ToLowerInvariant()}",
            () => _host.Send(new SetFactoryModeCommand(me, tileId, factory.X, factory.Y, other))));
        if (UnitFactory.CheckTrigger(tile, me, factory.X, factory.Y) == FactoryControlResult.Ok)
            control.AddChild(Ui.Button("Build one", () => _host.Send(new TriggerFactoryCommand(me, tileId, factory.X, factory.Y))));
        if (UnitFactory.CheckResume(tile, me, factory.X, factory.Y) == FactoryControlResult.Ok)
            control.AddChild(Ui.Button("Resume", () => _host.Send(new ResumeFactoryCommand(me, tileId, factory.X, factory.Y))));
        _factoryInfo.AddChild(control);
    }

    void AddFactoryItems(string label, IReadOnlyDictionary<string, int> items)
    {
        var row = Ui.Row(6);
        row.AddChild(Ui.Label(label, Ui.SmallFont, Ui.Muted));
        row.AddChild(Ui.Items(items));
        _factoryInfo.AddChild(row);
    }
}
