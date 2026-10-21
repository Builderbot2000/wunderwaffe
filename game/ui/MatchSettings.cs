using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Wunderwaffe.Sim;
using Wunderwaffe.Sim.Content;

namespace Wunderwaffe.Game.Screens;

/// <summary>
/// The match settings form (MP-005), shared by the new-game page and the multiplayer lobby: map (generated from a seed
/// or a scenario, WM-017), seed, map size, AI count with a head start per AI seat (MP-008), capture time, victory share
/// and resource richness. Everything it sets is part of <see cref="GameSetup"/>, so it is saved with the match.
/// </summary>
public sealed class MatchSettings
{
    static readonly (string Name, int Size)[] MapSizes = [("Small · 3 × 3", 3), ("Medium · 5 × 5", 5), ("Large · 7 × 7", 7)];

    /// <summary>Control names the UI tour finds them by.</summary>
    public const string MapPickName = "MapPick", AiName = "AiCount", HeadStartName = "HeadStart", FactionName = "Faction";

    readonly LineEdit _seed;
    readonly OptionButton _size, _map;
    readonly SpinBox _ai, _capture, _victory, _richness;
    readonly VBoxContainer _heads;
    readonly List<OptionButton> _headPicks = [];
    readonly VBoxContainer _factionBox;
    readonly List<OptionButton> _factionPicks = [];
    /// <summary>The lobby's faction choices: Random, then every faction not hidden (MP-009).</summary>
    readonly List<FactionDef> _factions;
    string?[] _factionIds;
    readonly List<ScenarioDef> _scenarios;
    readonly IReadOnlyList<HeadStartDef> _presets;
    readonly int _humans;
    readonly Action? _onChange;
    string?[] _headIds;
    /// <summary>The AI count to go back to when the map goes from a scenario back to generated.</summary>
    int _generatedAi;

    /// <summary>The controls, ready to add to a page.</summary>
    public Control Root { get; }

    /// <summary>The form built last, for the UI tour to read back what it picked.</summary>
    public static MatchSettings? Shown { get; private set; }

    /// <param name="humans">People in the match (1 alone); AI seats fill up to <see cref="GameSetup.MaxPlayers"/>.</param>
    /// <param name="onChange">Called after any field changes (the lobby broadcasts it).</param>
    public MatchSettings(GameSetup initial, int humans, Action? onChange = null)
    {
        _humans = humans;
        _onChange = onChange;
        var content = SimHost.Instance.Content;
        _scenarios = [.. content.Scenarios.Values.OrderBy(s => s.Name, StringComparer.Ordinal)];
        _presets = content.HeadStartList;
        _headIds = initial.AiHeadStarts ?? [];
        _factions = [.. content.FactionList.Where(f => !f.Hidden)];
        _factionIds = initial.Factions ?? [];

        var grid = new GridContainer { Columns = 2, MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 8);
        Root = grid;

        _map = new OptionButton { Name = MapPickName, FocusMode = Control.FocusModeEnum.None, TooltipText = "A generated world, or a scenario: a saved or hand-made map with what stands on it" };
        _map.AddItem("Generated from the seed");
        foreach (var s in _scenarios)
            _map.AddItem($"Scenario · {s.Name} ({s.Seats.Count} seats)");
        _map.Selected = initial.Scenario is { } id && _scenarios.FindIndex(s => s.Id == id) is >= 0 and var at ? at + 1 : 0;
        _map.ItemSelected += _ => { Refresh(); RebuildFactions(); onChange?.Invoke(); };
        Field(grid, "Map", _map);

        var seedRow = Ui.Row();
        _seed = new LineEdit { Text = initial.Seed.ToString(), CustomMinimumSize = new Vector2(200, 0), PlaceholderText = "number or any text" };
        _seed.TextChanged += _ => onChange?.Invoke();
        seedRow.AddChild(_seed);
        seedRow.AddChild(Ui.Button("Random", () => { _seed.Text = RandomSeed(); onChange?.Invoke(); }));
        Field(grid, "Seed", seedRow);

        _size = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        int pick = 0;
        for (int i = 0; i < MapSizes.Length; i++)
        {
            _size.AddItem(MapSizes[i].Name);
            if (MapSizes[i].Size == initial.Width)
                pick = i;
        }
        _size.Selected = pick;
        _size.ItemSelected += _ => onChange?.Invoke();
        Field(grid, "Map size", _size);

        _ai = Spin(grid, "AI opponents", 0, GameSetup.MaxPlayers - humans, 1, initial.AiPlayers, "", null);
        _ai.Name = AiName;
        _ai.ValueChanged += _ => { RebuildHeads(); RebuildFactions(); onChange?.Invoke(); };
        _heads = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        Field(grid, "AI head starts", _heads);
        _factionBox = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        Field(grid, "Factions", _factionBox);
        _capture = Spin(grid, "Capture time", GameSetup.MinCaptureSeconds, GameSetup.MaxCaptureSeconds, 5, initial.CaptureSeconds, " s", onChange,
            "How long you must hold a tile's victory circle to take it");
        _victory = Spin(grid, "Victory share", GameSetup.MinVictoryPercent, GameSetup.MaxVictoryPercent, 5, initial.VictoryPercent, " %", onChange,
            "Own this share of all tiles to win; or be the last player with a core");
        _richness = Spin(grid, "Resource richness", GameSetup.MinRichness, GameSetup.MaxRichness, 25, initial.RichnessPercent, " %", onChange,
            "Deposit size compared with normal");
        Refresh();
        RebuildHeads();
        RebuildFactions();
        Shown = this;
    }

    ScenarioDef? Scenario => _map.Selected > 0 ? _scenarios[_map.Selected - 1] : null;

    /// <summary>A scenario fixes the world and the seat count: every seat no person takes is an AI.</summary>
    void Refresh()
    {
        bool generated = Scenario == null;
        _seed.Editable = generated;
        _richness.Editable = generated;
        _size.Disabled = !generated;
        if (Scenario is { } s)
        {
            if (_ai.Editable)
                _generatedAi = (int)_ai.Value;
            int ai = Math.Max(0, s.Seats.Count - _humans);
            (_ai.MinValue, _ai.MaxValue, _ai.Value) = (ai, ai, ai);
        }
        else if (!_ai.Editable)
        {
            (_ai.MinValue, _ai.MaxValue, _ai.Value) = (0, GameSetup.MaxPlayers - _humans, _generatedAi);
        }
        _ai.Editable = generated;
    }

    /// <summary>One preset picker per AI seat, keeping what was picked.</summary>
    void RebuildHeads()
    {
        _headIds = HeadIds();
        Ui.Clear(_heads);
        _headPicks.Clear();
        for (int i = 0; i < (int)_ai.Value; i++)
        {
            var option = new OptionButton { Name = $"{HeadStartName}{i}", FocusMode = Control.FocusModeEnum.None };
            option.AddItem($"AI {i + 1}: no head start");
            foreach (var p in _presets)
                option.AddItem($"AI {i + 1}: {p.Name}");
            for (int k = 0; k < _presets.Count; k++)
            {
                option.SetItemTooltip(k + 1, _presets[k].Description ?? "");
                if (i < _headIds.Length && _headIds[i] == _presets[k].Id)
                    option.Selected = k + 1;
            }
            option.ItemSelected += _ => { _headIds = HeadIds(); _onChange?.Invoke(); };
            _heads.AddChild(option);
            _headPicks.Add(option);
        }
        if (_ai.Value == 0)
            _heads.AddChild(Ui.Label("none", Ui.SmallFont, Ui.Muted));
    }

    /// <summary>
    /// One faction picker per seat, people first (MP-009): Random or a faction, keeping what was picked. A scenario seat
    /// with a faction of its own shows it, fixed.
    /// </summary>
    void RebuildFactions()
    {
        _factionIds = FactionIds();
        Ui.Clear(_factionBox);
        _factionPicks.Clear();
        int seats = _humans + (int)_ai.Value;
        for (int i = 0; i < seats; i++)
        {
            string who = i < _humans ? $"Player {i + 1}" : $"AI {i - _humans + 1}";
            var option = new OptionButton { Name = $"{FactionName}{i}", FocusMode = Control.FocusModeEnum.None };
            option.AddItem($"{who}: Random");
            foreach (var f in _factions)
                option.AddItem($"{who}: {f.Name}");
            for (int k = 0; k < _factions.Count; k++)
                option.SetItemTooltip(k + 1, _factions[k].Description ?? "");
            string? id = i < _factionIds.Length ? _factionIds[i] : null;
            int at = _factions.FindIndex(f => f.Id == (id is { Length: > 0 } ? id : _factions.FirstOrDefault()?.Id));
            option.Selected = id == GameSetup.RandomFaction ? 0 : at + 1;
            if (Scenario is { } s && i < s.Seats.Count && s.Seats[i].Faction is { } fixedId
                && _factions.FindIndex(f => f.Id == fixedId) is >= 0 and var fixedAt)
            {
                option.Selected = fixedAt + 1;
                option.Disabled = true;
                option.TooltipText = "The scenario fixes this seat's faction";
            }
            option.ItemSelected += _ => { _factionIds = FactionIds(); _onChange?.Invoke(); };
            _factionBox.AddChild(option);
            _factionPicks.Add(option);
        }
    }

    string?[] FactionIds() =>
        _factionPicks.Count == 0 ? _factionIds
        : [.. _factionPicks.Select(o => o.Selected <= 0 ? GameSetup.RandomFaction : _factions[o.Selected - 1].Id)];

    string?[] HeadIds() =>
        _headPicks.Count == 0 ? _headIds : [.. _headPicks.Select(o => o.Selected > 0 ? _presets[o.Selected - 1].Id : null)];

    /// <summary>A fresh single-player setup with a random seed.</summary>
    public static GameSetup NewDefault() => GameSetup.Default with { Seed = ulong.Parse(RandomSeed()) };

    /// <summary>The setup the form shows, with the people plus AI seats as players.</summary>
    public GameSetup Read()
    {
        int n = MapSizes[_size.Selected].Size;
        int ai = (int)_ai.Value;
        var heads = HeadIds().Take(ai).ToArray();
        string?[]? headStarts = heads.Any(h => h != null) ? heads : null;
        var factions = FactionIds().Take(_humans + ai).ToArray();
        if (Scenario is { } s)
            return GameSetup.ForScenario(s, ai) with
            {
                CaptureSeconds = (int)_capture.Value, VictoryPercent = (int)_victory.Value, AiHeadStarts = headStarts, Factions = factions,
            };
        return new GameSetup(n, n, GameSetup.ParseSeed(_seed.Text.Length == 0 ? "3037" : _seed.Text), _humans + ai, ai,
            (int)_capture.Value, (int)_victory.Value, (int)_richness.Value, AiHeadStarts: headStarts, Factions: factions);
    }

    /// <summary>A read-only summary for players waiting in someone else's lobby.</summary>
    public static string Describe(GameSetup s)
    {
        var content = SimHost.Instance.Content;
        string map = s.Scenario is { } id
            ? $"Scenario {(content.Scenarios.TryGetValue(id, out var def) ? def.Name : $"'{id}' (you don't have it)")}"
            : $"Map {s.Width} × {s.Height} · seed {s.Seed} · richness {s.RichnessPercent}%";
        var heads = Enumerable.Range(s.Players - s.AiPlayers, s.AiPlayers).Select(s.HeadStartFor).Where(h => h != null)
            .Select(h => content.HeadStarts.TryGetValue(h!, out var p) ? p.Name : h).ToList();
        var factions = Enumerable.Range(0, s.Players).Select(p => s.Factions is { } f && p < f.Length && f[p] == GameSetup.RandomFaction
            ? "Random" : s.FactionFor(p, content).Name);
        return $"{map}\n{s.AiPlayers} AI{(heads.Count > 0 ? $" (head starts: {string.Join(", ", heads)})" : "")} · " +
               $"capture {s.CaptureSeconds} s · win at {s.VictoryPercent}% of tiles\nFactions: {string.Join(", ", factions)}";
    }

    static void Field(GridContainer grid, string name, Control control)
    {
        grid.AddChild(Ui.Label(name, Ui.SmallFont, Ui.Muted));
        grid.AddChild(control);
    }

    static SpinBox Spin(GridContainer grid, string name, int min, int max, int step, int value, string suffix, Action? onChange, string tip = "")
    {
        var spin = new SpinBox
        {
            MinValue = min, MaxValue = Math.Max(min, max), Step = step, Value = Math.Clamp(value, min, Math.Max(min, max)),
            Suffix = suffix, TooltipText = tip, CustomMinimumSize = new Vector2(140, 0), FocusMode = Control.FocusModeEnum.None,
        };
        if (onChange != null)
            spin.ValueChanged += _ => onChange();
        Field(grid, name, spin);
        return spin;
    }

    static string RandomSeed() => (GD.Randi() % 1_000_000).ToString();
}
