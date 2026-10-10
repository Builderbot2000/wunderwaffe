using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Wunderwaffe.Game.Input;
using Wunderwaffe.Game.Render;
using Wunderwaffe.Game.Screens;
using Wunderwaffe.Sim;
using Wunderwaffe.Sim.Content;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game;

public enum ViewMode
{
    Tile,
    World,
}

/// <summary>
/// Owns the match: starts a new one or rebuilds a saved one, then advances it at a fixed tick rate, independent of
/// frame rate. Frame delta is only used to decide how many ticks to run; it never enters sim state.
/// Pausing the scene tree pauses the sim too.
/// </summary>
public partial class SimHost : Node
{
    public const string TitleScene = "res://scenes/title.tscn";
    public const string GameScene = "res://scenes/main.tscn";

    const double TickSeconds = 1.0 / Simulation.TicksPerSecond;
    const int MaxTicksPerFrame = 8;
    const int StressBuildings = 2000;
    const int BenchTicks = 600;

    /// <summary>Wall time a loading replay may take per frame, so the progress bar keeps moving.</summary>
    const double LoadBudgetMs = 40;

    double _accumulator;
    long _benchStartTick = -1;
    double _benchStepMs;
    Replay? _replay;

    public ContentDb Content { get; private set; } = null!;

    /// <summary>The running match. Only valid while <see cref="HasGame"/>.</summary>
    public Simulation Sim { get; private set; } = null!;

    public bool HasGame { get; private set; }
    public int LocalPlayer { get; } = 0;

    /// <summary>Ids of the local player's selected units. Shared by the tile view and the world map.</summary>
    public HashSet<int> Selected { get; } = [];

    /// <summary>Which layer the local player is looking at (WM-007).</summary>
    public ViewMode View { get; private set; } = ViewMode.World;

    /// <summary>The tile shown in <see cref="ViewMode.Tile"/>; the last one visited otherwise.</summary>
    public TileState ViewedTile => Sim.Tiles[_viewedTileId];

    int _viewedTileId;

    /// <summary>
    /// How many times the view has switched between the world map and a tile this match. The world map refreshes its
    /// pictures of the tiles every <see cref="GameSettings.SnapshotEvery"/> switches.
    /// </summary>
    public int ToggleCount { get; private set; }

    /// <summary>Raised after <see cref="View"/> or <see cref="ViewedTile"/> changes.</summary>
    public event System.Action? ViewChanged;

    /// <summary>0..1 while a save is replaying, null otherwise.</summary>
    public double? LoadProgress => _replay == null ? null : _replay.TotalTicks == 0 ? 1 : (double)_replay.Sim.Tick / _replay.TotalTicks;

    /// <summary>Why the last load failed, or a warning about the last load that succeeded. Cleared by the next one.</summary>
    public string? LoadMessage { get; set; }

    /// <summary>A save the title screen should load when it opens (set by the pause menu's Load).</summary>
    public string? PendingLoad { get; set; }

    public void ShowWorld()
    {
        if (View != ViewMode.World)
            ToggleCount++;
        View = ViewMode.World;
        ViewChanged?.Invoke();
    }

    /// <summary>Opens one of the local player's tiles. Returns false for tiles they don't own.</summary>
    public bool EnterTile(int tileId)
    {
        if (Sim.TryGetTile(tileId) is not { } tile || tile.Owner != LocalPlayer)
            return false;
        _viewedTileId = tile.Id;
        if (View != ViewMode.Tile)
            ToggleCount++;
        View = ViewMode.Tile;
        ViewChanged?.Invoke();
        return true;
    }

    /// <summary>Smoothed wall time of one <see cref="Simulation.Step"/>, for the NF-001 budget.</summary>
    public double StepMs { get; private set; }

    public override void _EnterTree() => InputActions.Register();

    public override void _Ready()
    {
        Content = LoadContent();
        Palette.Init(Content);
        GameSettings.Load();

        // `-- --tour out_dir` clicks through the menus and HUD, saving screenshots (needs a window).
        var args = OS.GetCmdlineUserArgs();
        int tour = System.Array.IndexOf(args, "--tour");
        if (tour >= 0)
            UiTour.Attach(this, tour + 1 < args.Length ? args[tour + 1] : "tour");
    }

    /// <summary>Starts a fresh match and opens it on the world map, centred on the player's first tile.</summary>
    public void StartNew(GameSetup setup, ViewMode view = ViewMode.World)
    {
        Begin(Simulation.NewGame(Content, setup, LocalPlayer), view);
        GoTo(GameScene);
    }

    /// <summary>Starts replaying a save; <see cref="LoadProgress"/> reports how far it is. Returns false if the file can't be read.</summary>
    public bool BeginLoad(string path)
    {
        LoadMessage = null;
        try
        {
            var save = SaveFile.FromJson(FileAccess.GetFileAsString(path));
            _replay = new Replay(Content, save, LocalPlayer);
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
        GetTree().Paused = false;
        GoTo(TitleScene);
    }

    void Begin(Simulation sim, ViewMode view)
    {
        Sim = sim;
        HasGame = true;
        Selected.Clear();
        _accumulator = 0;
        _benchStartTick = -1;
        StepMs = 0;
        var home = sim.Tiles.FirstOrDefault(t => t.Owner == LocalPlayer) ?? sim.Tiles[0];
        _viewedTileId = home.Id;
        ToggleCount = 0;
        View = view;
        GetTree().Paused = false;
    }

    /// <summary>
    /// Debug: `-- --stress` and `-- --drive [shot.png]` skip the title and start the default match on the home tile.
    /// Returns true if it did.
    /// </summary>
    public bool TryAutoStart()
    {
        var args = OS.GetCmdlineUserArgs();
        int drive = System.Array.IndexOf(args, "--drive");
        bool stress = args.Contains("--stress");
        if (drive < 0 && !stress)
            return false;

        StartNew(GameSetup.Default, ViewMode.Tile);
        if (stress)
            StressFill();
        // Plays a scripted unit session through the input path (needs a window).
        if (drive >= 0)
            DebugDriver.Attach(this, drive + 1 < args.Length ? args[drive + 1] : null);
        return true;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!HasGame)
            return;
        if (e.IsActionPressed(InputActions.DebugCheat))
        {
            Cheat();
        }
        else if (e.IsActionPressed(InputActions.WorldToggle))
        {
            if (View == ViewMode.Tile)
                ShowWorld();
            else
                EnterTile(_viewedTileId);
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

        _accumulator += delta;
        int steps = 0;
        while (_accumulator >= TickSeconds && steps < MaxTicksPerFrame)
        {
            long t0 = Stopwatch.GetTimestamp();
            Sim.Step();
            double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            StepMs = StepMs == 0 ? ms : StepMs * 0.95 + ms * 0.05;
            if (_benchStartTick >= 0)
                _benchStepMs += ms;

            _accumulator -= TickSeconds;
            steps++;
        }
        if (steps == MaxTicksPerFrame)
            _accumulator = 0; // Falling behind: drop time rather than spiral.

        if (_benchStartTick >= 0 && Sim.Tick - _benchStartTick >= BenchTicks)
            ReportBench();
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

    /// <summary>Debug (F4): every tech node, and plenty of every item on the viewed tile.</summary>
    void Cheat()
    {
        foreach (var tech in Content.TechList)
            Sim.Enqueue(new DebugUnlockCommand(LocalPlayer, tech.Id));
        foreach (var item in Content.Items.Keys)
            Sim.Enqueue(new DebugGrantCommand(LocalPlayer, ViewedTile.Id, item, 1000));
    }

    /// <summary>Debug: covers the viewed tile with ~2,000 producing buildings (NF-001 stress test).</summary>
    public void StressFill()
    {
        var tile = ViewedTile;
        Sim.Enqueue(new DebugGrantCommand(LocalPlayer, tile.Id, "copper_ore", 1_000_000));
        Sim.Enqueue(new DebugGrantCommand(LocalPlayer, tile.Id, "iron_ore", 1_000_000));

        int placed = 0;
        for (int y = 0; y < tile.Grid.Height - 1 && placed < StressBuildings; y += 2)
        {
            for (int x = 0; x < tile.Grid.Width - 1 && placed < StressBuildings; x += 2)
            {
                if (!tile.Grid.IsFree(x, y, 2, 2))
                    continue;
                bool onOre = Placement.Check(Sim, tile, LocalPlayer, "drill", x, y, 0, null, out _)
                    != PlaceResult.NeedsDeposit;
                Sim.Enqueue(onOre
                    ? new PlaceBuildingCommand(LocalPlayer, tile.Id, "drill", x, y)
                    : new PlaceBuildingCommand(LocalPlayer, tile.Id, "smelter", x, y, RecipeId: "smelt_iron"));
                placed++;
            }
        }
        _benchStartTick = Sim.Tick + 1;
        _benchStepMs = 0;
    }

    void ReportBench()
    {
        GD.Print($"stress: {ViewedTile.Grid.BuildingCount} buildings, " +
                 $"{_benchStepMs / BenchTicks:F4} ms/tick over {BenchTicks} ticks, " +
                 $"{Engine.GetFramesPerSecond()} fps");
        _benchStartTick = -1;
        if (DisplayServer.GetName() == "headless")
            GetTree().Quit();
    }

    static ContentDb LoadContent()
    {
        static string Read(string name) => FileAccess.GetFileAsString($"res://data/{name}");
        return ContentDb.FromJson(Read("items.json"), Read("recipes.json"), Read("buildings.json"), Read("tech.json"), Read("goals.json"), Read("terrain.json"));
    }
}
