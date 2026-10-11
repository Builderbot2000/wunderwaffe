using System.Linq;
using Godot;
using Wunderwaffe.Game.Net;
using Wunderwaffe.Game.Render;
using Wunderwaffe.Sim;

namespace Wunderwaffe.Game.Screens;

/// <summary>
/// The first screen: new game (match settings, MP-005), multiplayer (host/join lobby, MP-003), continue the newest
/// save, load, settings, quit. Also shows load progress while <see cref="SimHost"/> replays a save, whether it was
/// picked here or in the pause menu.
/// </summary>
public partial class TitleScreen : Control
{
    SimHost _host = null!;
    Control _pages = null!;
    Control _menu = null!;
    Control? _page;
    Label _message = null!;
    ProgressBar? _loadBar;

    public override void _Ready()
    {
        _host = SimHost.Instance;
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
        _host.Net.Changed += OnNetChanged;
        TreeExiting += () => _host.Net.Changed -= OnNetChanged;
    }

    void BuildBackground()
    {
        AddChild(FullRect(new ColorRect { Color = Palette.Ground, MouseFilter = MouseFilterEnum.Ignore }));
        if (Sprites.Get(Sprites.Ground) is { } ground)
        {
            var tiles = new TextureRect
            {
                Texture = ground, StretchMode = TextureRect.StretchModeEnum.Tile, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                Modulate = new Color(0.55f, 0.55f, 0.6f), MouseFilter = MouseFilterEnum.Ignore, Scale = new Vector2(3, 3),
            };
            AddChild(FullRect(tiles));
        }
        AddChild(FullRect(new ColorRect { Color = new Color(0.03f, 0.035f, 0.045f, 0.55f), MouseFilter = MouseFilterEnum.Ignore }));

        // A few factory pieces on the right as decoration.
        var deco = FullRect(new Control { MouseFilter = MouseFilterEnum.Ignore });
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
        col.AddChild(Ui.Button("Multiplayer", () => Show(BuildMultiplayer())));
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
        var form = new MatchSettings(MatchSettings.NewDefault(), 1);
        col.AddChild(form.Root);
        col.AddChild(Ui.Label("You start with one tile, a core and some ore. Follow the objectives in the top right. " +
                              "AI opponents start on their own tiles elsewhere on the map.", Ui.SmallFont, Ui.Muted, 400));
        var row = Ui.Row();
        row.AddChild(Ui.Button("Start", () => _host.StartNew(form.Read()), 160));
        row.AddChild(Ui.Button("Back", Back));
        col.AddChild(row);
        return Ui.Panel(col);
    }

    // ---- Multiplayer (MP-003) ----

    string _playerName = "Commander", _address = "127.0.0.1";
    int _port = NetSession.DefaultPort;
    Control? _lobbyPage;

    /// <summary>Name, address and port; host or join.</summary>
    Control BuildMultiplayer()
    {
        var col = Ui.Column(10);
        col.AddChild(Ui.Heading("Multiplayer"));
        var grid = new GridContainer { Columns = 2, MouseFilter = MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 14);
        var name = new LineEdit { Text = _playerName, CustomMinimumSize = new Vector2(260, 0) };
        var address = new LineEdit { Text = _address, PlaceholderText = "host address" };
        var port = new SpinBox { MinValue = 1024, MaxValue = 65535, Value = _port, FocusMode = FocusModeEnum.None };
        grid.AddChild(Ui.Label("Your name", Ui.SmallFont, Ui.Muted));
        grid.AddChild(name);
        grid.AddChild(Ui.Label("Host address", Ui.SmallFont, Ui.Muted));
        grid.AddChild(address);
        grid.AddChild(Ui.Label("Port", Ui.SmallFont, Ui.Muted));
        grid.AddChild(port);
        col.AddChild(grid);
        var error = Ui.Label(_host.Net.Notice ?? "", Ui.SmallFont, Ui.Warn, 400);
        void Remember() => (_playerName, _address, _port) = (name.Text, address.Text, (int)port.Value);

        var row = Ui.Row();
        row.AddChild(Ui.Button("Host game", () =>
        {
            Remember();
            if (_host.Net.Host(_playerName, _port, MatchSettings.NewDefault()) is { } why)
                error.Text = why;
            else
                Show(BuildLobby());
        }, 160));
        row.AddChild(Ui.Button("Join", () =>
        {
            Remember();
            if (_host.Net.Join(_playerName, _address, _port) is { } why)
                error.Text = why;
            else
                Show(BuildLobby());
        }, 120));
        row.AddChild(Ui.Button("Back", Back));
        col.AddChild(row);
        col.AddChild(error);
        col.AddChild(Ui.Label("Everyone runs the same match in lockstep; nothing pauses. Players on other machines need " +
                              "the host's address and an open UDP port.", Ui.SmallFont, Ui.Muted, 400));
        return Ui.Panel(col);
    }

    /// <summary>The lobby: who is in and the match settings, editable by the host only.</summary>
    Control BuildLobby()
    {
        var net = _host.Net;
        var col = Ui.Column(10);
        col.AddChild(Ui.Heading(net.IsHost ? $"Hosting on port {_port}" : $"Lobby · {_address}:{_port}"));
        var lobby = net.Lobby;
        if (lobby == null)
        {
            col.AddChild(Ui.Label(net.State == NetState.Joining ? "Connecting…" : "Waiting for the host…", Ui.FontSize, Ui.Text));
        }
        else
        {
            var players = Ui.Column(4);
            for (int i = 0; i < lobby.Names.Count; i++)
                players.AddChild(Ui.Label($"{i + 1}. {lobby.Names[i]}{(i == 0 ? "  (host)" : "")}", Ui.FontSize, Palette.Owner(i)));
            for (int i = 0; i < lobby.Final.AiPlayers; i++)
                players.AddChild(Ui.Label($"{lobby.Names.Count + i + 1}. AI", Ui.FontSize, Palette.Owner(lobby.Names.Count + i)));
            col.AddChild(players);
            if (net.IsHost)
            {
                MatchSettings? form = null;
                form = new MatchSettings(lobby.Setup, lobby.Names.Count, () => net.SetSetup(form!.Read()));
                col.AddChild(form.Root);
            }
            else
            {
                col.AddChild(Ui.Label(MatchSettings.Describe(lobby.Final), Ui.SmallFont, Ui.Text, 400));
            }
        }
        var row = Ui.Row();
        if (net.IsHost)
            row.AddChild(Ui.Button("Start", net.Start, 160));
        row.AddChild(Ui.Button("Leave", () => { net.Leave(); Show(BuildMultiplayer()); }));
        col.AddChild(row);
        return _lobbyPage = Ui.Panel(col);
    }

    /// <summary>Redraws the lobby on news from the network; back to the join page if the link failed.</summary>
    void OnNetChanged()
    {
        if (_page == null || _page != _lobbyPage)
            return;
        var net = _host.Net;
        if (net.State == NetState.Offline)
            Show(BuildMultiplayer());
        // The host's own edits come back here too; rebuilding would steal the field being typed in.
        else if (!net.IsHost || net.Lobby?.Names.Count != _shownNames)
            Show(BuildLobby());
        _shownNames = net.Lobby?.Names.Count ?? 0;
    }

    int _shownNames;

    static T FullRect<T>(T c) where T : Control
    {
        c.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        return c;
    }

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
