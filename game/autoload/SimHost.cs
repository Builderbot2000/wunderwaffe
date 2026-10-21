using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Wunderwaffe.Game.Debug;
using Wunderwaffe.Game.Input;
using Wunderwaffe.Game.Net;
using Wunderwaffe.Game.Render;
using Wunderwaffe.Game.Screens;
using Wunderwaffe.Sim;
using Wunderwaffe.Sim.Ai;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.Net;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game;

public enum ViewMode
{
    Tile,
    World,
}

/// <summary>
/// The autoload that owns the match (<see cref="Instance"/>): starts a new one or rebuilds a saved one, then advances it
/// at a fixed tick rate, independent of frame rate. Frame delta is only used to decide how many ticks to run; it never
/// enters sim state. Pausing the scene tree pauses the sim too (single player only: multiplayer never pauses, OQ-32).
/// Also holds what every view shares: which layer is shown, the viewed tile and the unit selection.
/// <para>
/// Every player action goes through <see cref="Send"/>: straight into the sim alone, through <see cref="Lockstep"/> in
/// multiplayer (MP-003). The match's AI seats run here, on the host only (MP-002).
/// </para>
/// </summary>
public partial class SimHost : Node
{
    public const string TitleScene = "res://scenes/title.tscn";
    public const string GameScene = "res://scenes/main.tscn";

    const double TickSeconds = 1.0 / Simulation.TicksPerSecond;
    const int MaxTicksPerFrame = 8;

    /// <summary>Wall time a loading replay may take per frame, so the progress bar keeps moving.</summary>
    const double LoadBudgetMs = 40;

    double _accumulator;
    Replay? _replay;
    int _viewedTileId;
    AiDirector? _ai;

    public static SimHost Instance { get; private set; } = null!;

    public ContentDb Content { get; private set; } = null!;

    /// <summary>The running match. Only valid while <see cref="HasGame"/>.</summary>
    public Simulation Sim { get; private set; } = null!;

    public bool HasGame { get; private set; }

    /// <summary>The seat this machine plays: 0 alone, the lobby's seat in multiplayer.</summary>
    public int LocalPlayer { get; private set; }

    /// <summary>The multiplayer link (lobby and transport). Lives as long as the game.</summary>
    public NetSession Net { get; private set; } = null!;

    /// <summary>The running multiplayer match's lockstep, or null alone.</summary>
    public Lockstep? Lockstep { get; private set; }

    public bool IsMultiplayer => Lockstep != null;

    /// <summary>
    /// Issues a command for the local player: into the sim's next tick alone, or into lockstep in multiplayer, where it
    /// applies a few turns later on every machine at once. Never call <see cref="Simulation.Enqueue"/> from the game.
    /// </summary>
    public void Send(Command command)
    {
        if (Lockstep is { } lockstep)
            lockstep.Submit(command);
        else
            Sim.Enqueue(command);
    }

    /// <summary>Ids of the local player's selected units. Shared by the tile view and the world map.</summary>
    public HashSet<int> Selected { get; } = [];

    /// <summary>Which layer the local player is looking at (WM-007).</summary>
    public ViewMode View { get; private set; } = ViewMode.World;

    /// <summary>The tile shown in <see cref="ViewMode.Tile"/>; the last one visited otherwise.</summary>
    public TileState ViewedTile => Sim.Tiles[_viewedTileId];

    /// <summary>
    /// How many times the view has switched between the world map and a tile this match. The world map refreshes its
    /// pictures of the tiles every <see cref="GameSettings.SnapshotEvery"/> switches.
    /// </summary>
    public int ToggleCount { get; private set; }

    /// <summary>
    /// A world cell the camera should centre on at its next frame (alerts and the armies list jump here), or null.
    /// The camera clears it.
    /// </summary>
    public Vector2I? FocusCell { get; set; }

    /// <summary>
    /// A zoom the camera should animate next frame (Tab, Enter, Open tile): a tile id to zoom into, -1 for the world
    /// map, or null. The camera clears it and switches the view when the zoom crosses the threshold (WM-007).
    /// </summary>
    public int? ZoomTarget { get; set; }

    /// <summary>
    /// How opaque the tile rendering is over the world map, 0..1, set by the camera every frame: 1 well inside a tile,
    /// 0 well out on the world map, in between around the threshold (the cross-fade, OQ-36).
    /// </summary>
    public float TileOpacity { get; set; } = 1;

    /// <summary>On the world map near the threshold, the tile the tile view fades in for (the one a zoom would open).</summary>
    public TileState? FadeTile { get; set; }

    /// <summary>Whether the local player may open <paramref name="tile"/>: presence (WM-015), owned or holding something of theirs.</summary>
    public bool CanEnter(TileState tile) => Territory.HasPresence(Sim, LocalPlayer, tile);

    /// <summary>Animates the zoom into a tile the player may open. Returns false for the others.</summary>
    public bool ZoomInto(int tileId)
    {
        if (Sim.TryGetTile(tileId) is not { } tile || !CanEnter(tile))
            return false;
        ZoomTarget = tile.Id;
        return true;
    }

    /// <summary>Animates the zoom out to the world map.</summary>
    public void ZoomOut() => ZoomTarget = -1;

    /// <summary>Raised after <see cref="View"/> or <see cref="ViewedTile"/> changes. Prefer <see cref="WatchView"/>.</summary>
    public event Action? ViewChanged;

    /// <summary>Smoothed wall time of one <see cref="Simulation.Step"/>, for the NF-001 budget.</summary>
    public double StepMs { get; private set; }

    /// <summary>Wall time of every <see cref="Simulation.Step"/> this match, summed, for benchmarks.</summary>
    public double TotalStepMs { get; private set; }

    /// <summary>0..1 while a save is replaying, null otherwise.</summary>
    public double? LoadProgress => _replay == null ? null : _replay.TotalTicks == 0 ? 1 : (double)_replay.Sim.Tick / _replay.TotalTicks;

    /// <summary>Why the last load failed, or a warning about the last load that succeeded. Cleared by the next one.</summary>
    public string? LoadMessage { get; set; }

    /// <summary>A save the title screen should load when it opens (set by the pause menu's Load).</summary>
    public string? PendingLoad { get; set; }

    /// <summary>
    /// Calls <paramref name="onChange"/> now and after every view change, until <paramref name="owner"/> leaves the tree.
    /// </summary>
    public void WatchView(Node owner, Action onChange)
    {
        ViewChanged += onChange;
        owner.TreeExiting += () => ViewChanged -= onChange;
        onChange();
    }

    /// <summary>
    /// Shows <paramref name="node"/>, and lets it process, only while <paramref name="mode"/> is the view. Runs
    /// <paramref name="onChange"/>, if given, after each view change. Tile-layer nodes also fade with
    /// <see cref="TileOpacity"/> near the threshold.
    /// </summary>
    public void ShowOnlyIn(CanvasItem node, ViewMode mode, Action? onChange = null)
    {
        if (mode == ViewMode.Tile)
        {
            _tileLayers.Add(node);
            node.TreeExiting += () => _tileLayers.Remove(node);
        }
        WatchView(node, () =>
        {
            bool shown = View == mode;
            node.Visible = shown;
            node.ProcessMode = shown ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
            onChange?.Invoke();
        });
    }

    readonly List<CanvasItem> _tileLayers = [];

    public void ShowWorld()
    {
        if (View != ViewMode.World)
            ToggleCount++;
        View = ViewMode.World;
        SyncView();
        ViewChanged?.Invoke();
    }

    /// <summary>
    /// Tells the sim which tile the local player looks at (FB-013): only viewed or Contested tiles run the full entity
    /// sim, the rest rate mode. It is lockstep input, so it goes through a command, and only when it changes.
    /// </summary>
    void SyncView()
    {
        int viewed = View == ViewMode.Tile ? _viewedTileId : -1;
        if (Sim.PlayerOrNull(LocalPlayer)?.ViewedTile != viewed)
            Send(new SetViewedTileCommand(LocalPlayer, viewed));
    }

    /// <summary>
    /// Opens a tile the player may enter at once (the camera calls it at the threshold; debug and HUD jumps directly).
    /// Returns false for the others.
    /// </summary>
    public bool EnterTile(int tileId)
    {
        if (Sim.TryGetTile(tileId) is not { } tile || !CanEnter(tile))
            return false;
        _viewedTileId = tile.Id;
        if (View != ViewMode.Tile)
            ToggleCount++;
        View = ViewMode.Tile;
        SyncView();
        ViewChanged?.Invoke();
        return true;
    }

    public override void _EnterTree()
    {
        Instance = this;
        InputActions.Register();
        Net = new NetSession { Name = "Net" };
        AddChild(Net);
    }

    public override void _Ready()
    {
        Content = LoadContent();
        Palette.Init(Content);
        GameSettings.Load();

        // `-- --tour out_dir` clicks through the menus and HUD, saving screenshots (needs a window).
        var args = OS.GetCmdlineUserArgs();
        int tour = Array.IndexOf(args, "--tour");
        if (tour >= 0)
            UiTour.Attach(this, tour + 1 < args.Length ? args[tour + 1] : "tour");
    }

    /// <summary>Starts a fresh match and opens it on the world map, centred on the player's first tile.</summary>
    public void StartNew(GameSetup setup, ViewMode view = ViewMode.World)
    {
        Begin(Simulation.NewGame(Content, setup), view);
        GoTo(GameScene);
    }

    /// <summary>
    /// Starts a multiplayer match on every machine at once (the lobby calls it): same setup, this machine's seat. The
    /// host also runs the AI seats and passes their moves on as its own input.
    /// </summary>
    public void StartMultiplayer(GameSetup setup, int seat, bool isHost, IEnumerable<int> clientSeats)
    {
        var sim = Simulation.NewGame(Content, setup);
        Begin(sim, ViewMode.World, seat, new Lockstep(sim, seat, isHost, clientSeats));
        GoTo(GameScene);
    }

    /// <summary>Debug: opens an already built sim (the stress scene, which has no <see cref="GameSetup"/>, so no saves).</summary>
    public void StartWith(Simulation sim, ViewMode view)
    {
        Begin(sim, view);
        GoTo(GameScene);
    }

    /// <summary>Starts replaying a save; <see cref="LoadProgress"/> reports how far it is. Returns false if the file can't be read.</summary>
    public bool BeginLoad(string path)
    {
        LoadMessage = null;
        try
        {
            var save = SaveFile.FromJson(FileAccess.GetFileAsString(path));
            _replay = new Replay(Content, save);
            return true;
        }
        catch (SaveException e)
        {
            LoadMessage = $"Couldn't load {path.GetFile()}: {e.Message}";
            return false;
        }
    }

    /// <summary>Writes the match to a new file in the saves folder. Returns the path, or null if it failed.</summary>
    public string? Save()
    {
        if (!HasGame)
            return null;
        SaveSlots.EnsureDir();
        string path = SaveSlots.NewPath();
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (file == null)
            return null;
        file.StoreString(SaveFile.From(Sim, SaveLabel()).ToJson());
        return path;
    }

    string SaveLabel()
    {
        var goals = Content.Goals;
        int done = Sim.PlayerOrNull(LocalPlayer)?.GoalsDone ?? 0;
        int tiles = Sim.Tiles.Count(t => t.Owner == LocalPlayer);
        string progress = done >= goals.Count ? "demo complete" : $"goal {done + 1}/{goals.Count}: {goals[done].Name}";
        return $"{Ui.Clock(Sim.Tick)} played · {tiles} tile{(tiles == 1 ? "" : "s")} · {progress}";
    }

    /// <summary>Deferred, so it is safe from inside another scene's _Ready or a button's signal.</summary>
    void GoTo(string scene) => GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, scene);

    public void QuitToTitle()
    {
        HasGame = false;
        _replay = null;
        Lockstep = null;
        _ai = null;
        Net.Leave();
        GetTree().Paused = false;
        GoTo(TitleScene);
    }

    void Begin(Simulation sim, ViewMode view, int seat = 0, Lockstep? lockstep = null)
    {
        Sim = sim;
        HasGame = true;
        LocalPlayer = seat;
        Lockstep = lockstep;
        // The multiplayer host always has a director: a leaver's seat goes to an AI (OQ-46).
        bool host = lockstep?.IsHost ?? true;
        _ai = sim.Setup is { } setup && host && (setup.AiPlayers > 0 || lockstep != null) ? new AiDirector(setup) : null;
        if (lockstep != null && _ai is { } ai)
        {
            lockstep.HostInputs = (s, ticks) => ai.Collect(s, ticks);
            lockstep.SeatLeft += seat => ai.TakeOver(seat, sim);
        }
        Selected.Clear();
        ZoomTarget = null;
        FadeTile = null;
        TileOpacity = view == ViewMode.Tile ? 1 : 0;
        _accumulator = 0;
        StepMs = TotalStepMs = 0;
        var home = sim.Tiles.FirstOrDefault(t => t.Owner == LocalPlayer) ?? sim.Tiles[0];
        _viewedTileId = home.Id;
        ToggleCount = 0;
        View = view;
        SyncView();
        GetTree().Paused = false;
    }

    /// <summary>
    /// Debug: `-- --stress` and `-- --drive [shot.png]` skip the title and start the default match on the home tile.
    /// Returns true if it did.
    /// </summary>
    public bool TryAutoStart()
    {
        var args = OS.GetCmdlineUserArgs();
        // `-- --net-smoke host|join`: two local instances play a short online match (MP-003).
        int smoke = Array.IndexOf(args, "--net-smoke");
        if (smoke >= 0)
        {
            NetSmoke.Attach(this, smoke + 1 < args.Length ? args[smoke + 1] : "host");
            return true;
        }
        int drive = Array.IndexOf(args, "--drive");
        bool stress = args.Contains("--stress");
        if (drive < 0 && !stress)
            return false;

        if (stress)
            DebugTools.StressWorld(this);
        else
            StartNew(GameSetup.Default, ViewMode.Tile);
        // Plays a scripted unit session through the input path (needs a window).
        if (drive >= 0)
            DebugDriver.Attach(this, drive + 1 < args.Length ? args[drive + 1] : null);
        return true;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!HasGame)
            return;
        if (e.IsActionPressed(InputActions.DebugCheat) && !IsMultiplayer)
            DebugTools.Cheat(this);
        else if (e.IsActionPressed(InputActions.Stress) && View == ViewMode.Tile && !IsMultiplayer)
            DebugTools.StressFill(this);
        else if (e.IsActionPressed(InputActions.WorldToggle))
        {
            // Animated, as the wheel would do it (WM-007).
            if (View == ViewMode.Tile)
                ZoomOut();
            else
                ZoomInto(_viewedTileId);
        }
    }

    public override void _Process(double delta)
    {
        if (_replay != null)
        {
            AdvanceLoad();
            return;
        }
        if (!HasGame)
            return;
        foreach (var layer in _tileLayers)
            layer.Modulate = Colors.White with { A = TileOpacity };

        _accumulator += delta;
        int steps = 0;
        while (_accumulator >= TickSeconds && steps < MaxTicksPerFrame)
        {
            long t0 = Stopwatch.GetTimestamp();
            if (Lockstep is { } lockstep)
            {
                // Waiting for a turn from the host (or, on the host, for a slow client): time stands still.
                if (lockstep.Advance(1) == 0)
                {
                    _accumulator = Math.Min(_accumulator, TickSeconds);
                    break;
                }
            }
            else
            {
                if (_ai != null)
                {
                    foreach (var command in _ai.Collect(Sim))
                        Sim.Enqueue(command);
                }
                Sim.Step();
            }
            double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            StepMs = StepMs == 0 ? ms : StepMs * 0.95 + ms * 0.05;
            TotalStepMs += ms;
            _accumulator -= TickSeconds;
            steps++;
        }
        // Falling behind: alone, drop time rather than spiral; in multiplayer catch up over the next frames instead.
        if (steps == MaxTicksPerFrame)
            _accumulator = IsMultiplayer ? Math.Min(_accumulator, 1.0) : 0;
        if (Lockstep is { } ls)
            Net.Flush(ls);
    }

    void AdvanceLoad()
    {
        var replay = _replay!;
        long t0 = Stopwatch.GetTimestamp();
        while (!replay.Done && Stopwatch.GetElapsedTime(t0).TotalMilliseconds < LoadBudgetMs)
            replay.Advance(120);
        if (!replay.Done)
            return;

        _replay = null;
        GD.Print($"load: replayed {replay.TotalTicks} ticks, {(replay.Matches ? "state matches the save" : "state DIVERGED from the save")}");
        if (!replay.Matches)
            LoadMessage = "This save was made with different game rules, so it may not match what you left.";
        Begin(replay.Sim, ViewMode.World);
        GoTo(GameScene);
    }

    static ContentDb LoadContent()
    {
        static string Read(string name) => FileAccess.GetFileAsString($"res://data/{name}");
        static IEnumerable<string> Files(string dir) =>
            DirAccess.GetFilesAt(dir).Where(f => f.EndsWith(".json")).Order(StringComparer.Ordinal).Select(f => FileAccess.GetFileAsString($"{dir}/{f}"));
        var content = ContentDb.FromJson(Read("items.json"), Read("recipes.json"), Read("buildings.json"), Read("goals.json"), Read("terrain.json"),
            Read("layers.json"), Files("res://data/prefabs"), Read("head_starts.json"), Files("res://data/scenarios"), Read("factions.json"));
        // The player's own scenarios: one that no longer fits the content is skipped, not fatal.
        if (DirAccess.DirExistsAbsolute(ScenarioDir))
        {
            foreach (var text in Files(ScenarioDir))
            {
                try
                {
                    content.AddScenario(text);
                }
                catch (ContentException e)
                {
                    GD.PushWarning($"skipping a scenario in {ScenarioDir}: {e.Errors.FirstOrDefault()}");
                }
            }
        }
        return content;
    }

    /// <summary>Where <see cref="SaveScenario"/> writes the player's scenarios (WM-017); loaded with the content.</summary>
    public const string ScenarioDir = "user://scenarios";

    /// <summary>
    /// Saves the map and everything standing on it as a scenario (WM-017) in <see cref="ScenarioDir"/> and offers it in
    /// the new-game menu from now on. Returns its name, or null if it failed.
    /// </summary>
    public string? SaveScenario()
    {
        if (!HasGame)
            return null;
        var now = DateTime.Now;
        string id = $"saved_{now:yyyyMMdd_HHmmss}";
        string home = Sim.TryGetTile(Sim.StartTileIds.ElementAtOrDefault(LocalPlayer))?.Name ?? "map";
        string json = Scenarios.ToJson(Sim.ToScenario(id, $"{home} · {now:d MMM HH:mm}", $"Saved from a match at {Ui.Clock(Sim.Tick)}."));
        try
        {
            var def = Content.AddScenario(json);
            DirAccess.MakeDirRecursiveAbsolute(ScenarioDir);
            using var file = FileAccess.Open($"{ScenarioDir}/{id}.json", FileAccess.ModeFlags.Write);
            if (file == null)
                return null;
            file.StoreString(json);
            return def.Name;
        }
        catch (ContentException e)
        {
            GD.PushWarning($"scenario not saved: {e.Errors.FirstOrDefault()}");
            return null;
        }
    }
}
