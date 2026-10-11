using System;
using Godot;
using Wunderwaffe.Sim;

namespace Wunderwaffe.Game.Screens;

/// <summary>
/// The match settings form (MP-005), shared by the new-game page and the multiplayer lobby: seed, map size, AI count,
/// capture time, victory share and resource richness. Everything it sets is part of <see cref="GameSetup"/>, so it is
/// saved with the match.
/// </summary>
public sealed class MatchSettings
{
    static readonly (string Name, int Size)[] MapSizes = [("Small · 5 × 5", 5), ("Medium · 7 × 7", 7), ("Large · 9 × 9", 9), ("Huge · 12 × 12", 12)];

    readonly LineEdit _seed;
    readonly OptionButton _size;
    readonly SpinBox _ai, _capture, _victory, _richness;
    readonly int _humans;

    /// <summary>The controls, ready to add to a page.</summary>
    public Control Root { get; }

    /// <param name="humans">People in the match (1 alone); AI seats fill up to <see cref="GameSetup.MaxPlayers"/>.</param>
    /// <param name="onChange">Called after any field changes (the lobby broadcasts it).</param>
    public MatchSettings(GameSetup initial, int humans, Action? onChange = null)
    {
        _humans = humans;
        var grid = new GridContainer { Columns = 2, MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 8);
        Root = grid;

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

        _ai = Spin(grid, "AI opponents", 0, GameSetup.MaxPlayers - humans, 1, initial.AiPlayers, "", onChange);
        _capture = Spin(grid, "Capture time", GameSetup.MinCaptureSeconds, GameSetup.MaxCaptureSeconds, 5, initial.CaptureSeconds, " s", onChange,
            "How long you must hold a tile's victory circle to take it");
        _victory = Spin(grid, "Victory share", GameSetup.MinVictoryPercent, GameSetup.MaxVictoryPercent, 5, initial.VictoryPercent, " %", onChange,
            "Own this share of all tiles to win; or be the last player with a core");
        _richness = Spin(grid, "Resource richness", GameSetup.MinRichness, GameSetup.MaxRichness, 25, initial.RichnessPercent, " %", onChange,
            "Deposit size compared with normal");
    }

    /// <summary>A fresh single-player setup with a random seed.</summary>
    public static GameSetup NewDefault() => GameSetup.Default with { Seed = ulong.Parse(RandomSeed()) };

    /// <summary>The setup the form shows, with the people plus AI seats as players.</summary>
    public GameSetup Read()
    {
        int n = MapSizes[_size.Selected].Size;
        int ai = (int)_ai.Value;
        return new GameSetup(n, n, GameSetup.ParseSeed(_seed.Text.Length == 0 ? "3037" : _seed.Text), _humans + ai, ai,
            (int)_capture.Value, (int)_victory.Value, (int)_richness.Value);
    }

    /// <summary>A read-only summary for players waiting in someone else's lobby.</summary>
    public static string Describe(GameSetup s) =>
        $"Map {s.Width} × {s.Height} · seed {s.Seed}\n{s.AiPlayers} AI · capture {s.CaptureSeconds} s · " +
        $"win at {s.VictoryPercent}% of tiles · richness {s.RichnessPercent}%";

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
        spin.ValueChanged += _ => onChange?.Invoke();
        Field(grid, name, spin);
        return spin;
    }

    static string RandomSeed() => (GD.Randi() % 1_000_000).ToString();
}
