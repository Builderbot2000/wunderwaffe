using Godot;
using Wunderwaffe.Game.Render;
using Wunderwaffe.Sim;

namespace Wunderwaffe.Game.Screens;

/// <summary>
/// The first screen: new game (seed and map size), continue the newest save, load, settings, quit. Also shows load
/// progress while <see cref="SimHost"/> replays a save, whether it was picked here or in the pause menu.
/// </summary>
public partial class TitleScreen : Control
{
    static readonly (string Name, int Size)[] MapSizes = [("Small · 5 × 5", 5), ("Medium · 7 × 7", 7), ("Large · 9 × 9", 9)];

    SimHost _host = null!;
    Control _pages = null!;
    Control _menu = null!;
    Control? _page;
    Label _message = null!;
    ProgressBar? _loadBar;

    public override void _Ready()
    {
        _host = GetNode<SimHost>("/root/SimHost");
        if (_host.TryAutoStart())
            return;

        Theme = Ui.Theme;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        BuildBackground();

        var left = Ui.Pin(Ui.Column(18), LayoutPreset.CenterLeft, 140);
        AddChild(left);
        var title = Ui.Column(0);
        title.AddChild(Ui.Label("WUNDERWAFFE", 92, Ui.Accent));
        title.AddChild(Ui.Label("3 0 3 7", 40, Ui.Text));
        title.AddChild(Ui.Label("Build megafactories. Design war machines. Take the map.", 20, Ui.Muted));
        left.AddChild(title);
        left.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });

        _pages = Ui.Column(0);
        left.AddChild(_pages);
        _menu = BuildMenu();
        _pages.AddChild(_menu);

        _message = Ui.Label("", Ui.FontSize, Ui.Warn, 560);
        left.AddChild(_message);

        var version = Ui.Pin(Ui.Label("Demo build · progress is saved from the Esc menu", Ui.SmallFont, Ui.Muted), LayoutPreset.BottomRight);
        AddChild(version);

        if (_host.PendingLoad is { } path)
        {
            _host.PendingLoad = null;
            Load(path);
        }
    }

    void BuildBackground()
    {
        AddChild(new ColorRect { Color = Palette.Ground, MouseFilter = MouseFilterEnum.Ignore }.Also(c => c.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect)));
        if (Sprites.Get(Sprites.Ground) is { } ground)
        {
            var tiles = new TextureRect
            {
                Texture = ground, StretchMode = TextureRect.StretchModeEnum.Tile, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                Modulate = new Color(0.55f, 0.55f, 0.6f), MouseFilter = MouseFilterEnum.Ignore, Scale = new Vector2(3, 3),
            };
            tiles.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(tiles);
        }
        AddChild(new ColorRect { Color = new Color(0.03f, 0.035f, 0.045f, 0.55f), MouseFilter = MouseFilterEnum.Ignore }.Also(c => c.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect)));

        // A few factory pieces on the right as decoration.
        var deco = new Control { MouseFilter = MouseFilterEnum.Ignore };
        deco.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(deco);
        (string Id, float X, float Y, float Size)[] pieces =
        [
            ("core", 1340, 380, 210), ("steel_mill", 1590, 300, 190), ("smelter", 1560, 560, 130),
            ("drill", 1160, 600, 130), ("thruster", 1420, 700, 70), ("unit_core", 1500, 700, 70),
            ("thruster", 1580, 700, 70), ("conveyor", 1290, 640, 60), ("conveyor", 1350, 640, 60),
        ];
        foreach (var (id, x, y, size) in pieces)
        {
            if (Sprites.Get(id) is not { } tex)
                continue;
            deco.AddChild(new TextureRect
            {
                Texture = tex, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspect,
                Position = new Vector2(x, y), Size = new Vector2(size, size), Modulate = new Color(1, 1, 1, 0.85f),
                MouseFilter = MouseFilterEnum.Ignore,
            });
        }
    }

    Control BuildMenu()
    {
        var col = Ui.Column(10);
        col.AddChild(Ui.Button("New game", () => Show(BuildNewGame()), 320));
        var newest = SaveSlots.Newest();
        var cont = Ui.Button("Continue", () => Load(newest!.Path));
        cont.Disabled = newest == null;
        if (newest != null)
            cont.TooltipText = newest.Label;
        col.AddChild(cont);
        if (newest != null)
            col.AddChild(Ui.Label(newest.Label, Ui.SmallFont, Ui.Muted, 320));
        col.AddChild(Ui.Button("Load game", () => Show(LoadMenu.Build(Load, Back))));
        col.AddChild(Ui.Button("Settings", () => Show(GameSettings.BuildPanel(Back))));
        col.AddChild(Ui.Button("Quit", () => GetTree().Quit()));
        return col;
    }

    Control BuildNewGame()
    {
        var col = Ui.Column(10);
        col.AddChild(Ui.Heading("New game"));

        col.AddChild(Ui.Label("Seed", Ui.SmallFont, Ui.Muted));
        var seedRow = Ui.Row();
        var seed = new LineEdit { Text = RandomSeed(), CustomMinimumSize = new Vector2(260, 0), PlaceholderText = "number or any text" };
        seedRow.AddChild(seed);
        seedRow.AddChild(Ui.Button("Random", () => seed.Text = RandomSeed()));
        col.AddChild(seedRow);

        col.AddChild(Ui.Label("Map size", Ui.SmallFont, Ui.Muted));
        var size = new OptionButton { FocusMode = FocusModeEnum.None };
        foreach (var (name, _) in MapSizes)
            size.AddItem(name);
        size.Selected = 0;
        col.AddChild(size);

        col.AddChild(Ui.Label("You start in the middle of the map with one tile, a core and some ore. " +
                              "Follow the objectives in the top right.", Ui.SmallFont, Ui.Muted, 400));
        var row = Ui.Row();
        row.AddChild(Ui.Button("Start", () =>
        {
            int n = MapSizes[size.Selected].Size;
            _host.StartNew(new GameSetup(n, n, GameSetup.ParseSeed(seed.Text.Length == 0 ? "3037" : seed.Text)));
        }, 160));
        row.AddChild(Ui.Button("Back", Back));
        col.AddChild(row);
        return Ui.Panel(col);
    }

    static string RandomSeed() => (GD.Randi() % 1_000_000).ToString();

    void Show(Control page)
    {
        Back();
        _message.Text = "";
        _menu.Visible = false;
        _page = page;
        _pages.AddChild(page);
    }

    void Back()
    {
        _page?.QueueFree();
        _page = null;
        _loadBar = null;
        _menu.Visible = true;
    }

    void Load(string path)
    {
        if (!_host.BeginLoad(path))
        {
            Back();
            _message.Text = _host.LoadMessage ?? "Couldn't load that save.";
            return;
        }
        var col = Ui.Column(10);
        col.AddChild(Ui.Heading("Loading"));
        col.AddChild(Ui.Label("Replaying your save…", Ui.FontSize, Ui.Text));
        var bar = new ProgressBar { CustomMinimumSize = new Vector2(400, 18), ShowPercentage = false, MaxValue = 1, Step = 0 };
        col.AddChild(bar);
        Show(Ui.Panel(col));
        _loadBar = bar;
    }

    public override void _Process(double delta)
    {
        if (_loadBar != null && _host.LoadProgress is { } p)
            _loadBar.Value = p;
    }
}

static class NodeExtensions
{
    /// <summary>Runs <paramref name="setup"/> on <paramref name="node"/> and returns it, for one-expression construction.</summary>
    public static T Also<T>(this T node, System.Action<T> setup)
    {
        setup(node);
        return node;
    }
}
