using System.Collections.Generic;
using System.Linq;
using Godot;
using Wunderwaffe.Game.Input;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Screens;

/// <summary>
/// The blueprint library panel (B, FB-009): every saved blueprint with its derived stats (UD-002), rename, tags,
/// duplicate, delete, place, and build-in-factory. And the factory panel (UD-006, WW-004), shown while a unit factory is
/// selected: status, per-stage progress, buffer and what is still missing.
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
        _library = Ui.Pin(Ui.Panel(col, LibraryWidth), LayoutPreset.CenterRight);
        _library.Visible = false;
        AddChild(_library);

        _factoryInfo = Ui.Column(6);
        _factoryPanel = Ui.Pin(Ui.Panel(_factoryInfo, SideWidth), LayoutPreset.CenterLeft);
        _factoryPanel.Visible = false;
        AddChild(_factoryPanel);

        BlueprintTool.Notice += (text, good) => Toast(text, good ? Ui.Good : Ui.Warn);
    }

    void ToggleLibrary()
    {
        _library.Visible = !_library.Visible;
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

        var name = new LineEdit { Text = e.Name, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        name.TextSubmitted += text => BlueprintLibrary.Rename(e.Id, text);
        name.FocusExited += () => BlueprintLibrary.Rename(e.Id, name.Text);
        var top = Ui.Row(6);
        top.AddChild(name);
        if (unit && stats.IsWunderwaffe)
            top.AddChild(Ui.Label("WUNDERWAFFE", Ui.SmallFont, Ui.Accent));
        col.AddChild(top);

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

        var tags = new LineEdit { Text = string.Join(", ", e.Tags), PlaceholderText = "tags, comma separated", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        tags.TextSubmitted += text => BlueprintLibrary.SetTags(e.Id, text);
        tags.FocusExited += () => BlueprintLibrary.SetTags(e.Id, tags.Text);
        col.AddChild(tags);

        var buttons = Ui.Row(6);
        buttons.AddChild(Ui.Button("Place", () => { BlueprintTool.Pick(e); _library.Visible = false; }));
        var factory = Ui.Button("Build in factory", () => { BlueprintTool.PickForFactory(e); _library.Visible = false; });
        factory.TooltipText = "Select one of your unit factories first, then pick where it should build this";
        factory.Disabled = BlueprintTool.SelectedFactory == null;
        _factoryButtons.Add(factory);
        buttons.AddChild(factory);
        buttons.AddChild(Ui.Button("Duplicate", () => BlueprintLibrary.Duplicate(e.Id)));
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
            FactoryStatus.LiftOffFailed => ($"Can't lift off: {GameText.Assemble(f.LiftOff)}", Ui.Bad),
            _ => (f.Status.ToString(), Ui.Muted),
        };
        Line(status, color);
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
        buttons.AddChild(Ui.Button("Pick blueprint", () => { _library.Visible = true; _libraryRevision = -1; RefreshBlueprints(); }));
        if (f.Projected != null)
            buttons.AddChild(Ui.Button("Stop", BlueprintTool.StopFactory));
        _factoryInfo.AddChild(buttons);
    }

    void AddFactoryItems(string label, IReadOnlyDictionary<string, int> items)
    {
        var row = Ui.Row(6);
        row.AddChild(Ui.Label(label, Ui.SmallFont, Ui.Muted));
        row.AddChild(Ui.Items(items));
        _factoryInfo.AddChild(row);
    }
}
