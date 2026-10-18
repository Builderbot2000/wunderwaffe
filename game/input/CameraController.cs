using System;
using Godot;
using Wunderwaffe.Game.Render;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Input;

/// <summary>
/// One continuous zoom over both layers (WM-007, OQ-36). The camera's state is a centre in world cells and a scale in
/// screen pixels per cell; each frame it is expressed in whichever layer's canvas is up (the tile view draws the viewed
/// tile in local cells at <see cref="Palette.CellPx"/>, the world map world cells at <see cref="WorldMapView.CellPx"/>),
/// so crossing between them leaves the picture where it was. Below <see cref="Threshold"/> the view is the world map;
/// at or above it, the tile under the screen centre (one the player has presence on). Around the threshold the tile
/// view fades over the world map (<see cref="SimHost.TileOpacity"/>). Wheel zooms toward the cursor, keyboard and
/// middle-drag pan; Tab/Enter requests (<see cref="SimHost.ZoomTarget"/>) animate the same zoom.
/// </summary>
public partial class CameraController : Camera2D
{
    [Export] public float PanSpeed = 900f;

    /// <summary>Closest zoom, in screen pixels per cell (4x the tile view's 16 px).</summary>
    [Export] public float MaxScale = 64f;

    [Export] public float ZoomStep = 1.15f;

    /// <summary>
    /// The threshold as a share of "a nominal tile fits the screen height" (OQ-36): above 1 the world map takes over
    /// sooner.
    /// </summary>
    [Export] public float ThresholdFit = 1f;

    /// <summary>The cross-fade runs from Threshold / FadeBand to Threshold * FadeBand.</summary>
    [Export] public float FadeBand = 1.35f;

    /// <summary>Seconds a Tab or Enter zoom takes.</summary>
    [Export] public float AnimSeconds = 0.45f;

    [Export] public WorldMapView WorldMap = null!;

    /// <summary>Scale the tile view opens at: 0.75 zoom on 16 px cells (Wave 0).</summary>
    const float TileScale = 0.75f * Palette.CellPx;

    SimHost _host = null!;
    ViewMode _mode;
    bool _rebasing;

    /// <summary>Where Tab returns to on each layer: the tile camera (with its tile) and the world map's scale.</summary>
    (int Tile, Vector2 Centre, float Scale)? _tileCamera;
    float? _worldScale;

    /// <summary>A running Tab/Enter zoom, or null. <c>Tile</c> is the tile to enter on the way in, -1 on the way out.</summary>
    (Vector2 FromC, float FromS, Vector2 ToC, float ToS, float T, int Tile)? _anim;

    /// <summary>Screen pixels per cell at which a nominal tile fits the screen height.</summary>
    public float Threshold => GetViewportRect().Size.Y / Sim.Simulation.DefaultTileSize * ThresholdFit;

    /// <summary>The current zoom in screen pixels per world cell, whichever layer is up.</summary>
    public float CellScale => Logical().Scale;

    float MinScale =>WorldFit().Scale * 0.6f;

    public override void _Ready()
    {
        _host = SimHost.Instance;
        _mode = _host.View;
        _host.WatchView(this, OnViewChanged);
        var (c, s) = _mode == ViewMode.World ? WorldFit() : TileHome(_host.ViewedTile);
        Apply(c, s);
    }

    (Vector2 Centre, float Scale) WorldFit()
    {
        var (center, zoom) = WorldMap.Fit(GetViewportRect().Size);
        return (center / WorldMapView.CellPx, zoom * WorldMapView.CellPx);
    }

    /// <summary>
    /// The tile's victory circle, where its core lands (WM-005), zoomed out a little so the whole circle shows: a
    /// 256-cell tile (OQ-35) is far wider than the screen, and its middle can be far from the circle.
    /// </summary>
    static (Vector2 Centre, float Scale) TileHome(TileState tile) =>
        (new Vector2(tile.OriginX + tile.CircleX + 0.5f, tile.OriginY + tile.CircleY + 0.5f), TileScale);

    static Vector2 Origin(TileState tile) => new(tile.OriginX, tile.OriginY);

    /// <summary>The camera as world cells and pixels per cell, read from the current layer's canvas.</summary>
    (Vector2 Centre, float Scale) Logical() => _host.View == ViewMode.Tile
        ? (Position / Palette.CellPx + Origin(_host.ViewedTile), Zoom.X * Palette.CellPx)
        : (Position / WorldMapView.CellPx, Zoom.X * WorldMapView.CellPx);

    /// <summary>A canvas point of the current layer in world cells.</summary>
    Vector2 ToCells(Vector2 canvas) => _host.View == ViewMode.Tile
        ? canvas / Palette.CellPx + Origin(_host.ViewedTile)
        : canvas / WorldMapView.CellPx;

    void Apply(Vector2 centre, float scale)
    {
        if (_host.View == ViewMode.Tile)
        {
            Position = (centre - Origin(_host.ViewedTile)) * Palette.CellPx;
            Zoom = Vector2.One * (scale / Palette.CellPx);
        }
        else
        {
            Position = centre * WorldMapView.CellPx;
            Zoom = Vector2.One * (scale / WorldMapView.CellPx);
        }
        ForceUpdateScroll();
    }

    /// <summary>A tile at <paramref name="cell"/> the player may open, or null.</summary>
    TileState? Enterable(Vector2 cell) =>
        _host.Sim.TileAtCell(Mathf.FloorToInt(cell.X), Mathf.FloorToInt(cell.Y)) is { } t && _host.CanEnter(t) ? t : null;

    /// <summary>Runs a view change this camera asked for: the camera is re-applied by the caller, not reset.</summary>
    void Switch(Action change)
    {
        _rebasing = true;
        change();
        _rebasing = false;
        _mode = _host.View;
    }

    /// <summary>
    /// Moves the camera, switching layers where the scale crosses the threshold. <paramref name="enter"/> is the tile
    /// to open on the way in; null picks the one under the centre. Wheeling out of a tile remembers where it was for Tab
    /// (a Tab zoom remembered that when it started).
    /// </summary>
    void Move(Vector2 centre, float scale, TileState? enter = null, bool remember = true)
    {
        if (_host.View == ViewMode.Tile && scale < Threshold)
        {
            if (remember)
                _tileCamera = (_host.ViewedTile.Id, centre, TileScale);
            Switch(_host.ShowWorld);
        }
        else if (_host.View == ViewMode.World && scale >= Threshold && (enter ?? Enterable(centre)) is { } tile)
        {
            Switch(() => _host.EnterTile(tile.Id));
        }
        Apply(centre, scale);
    }

    /// <summary>
    /// A view change from elsewhere (HUD jumps, debug scripts) jumps the camera as Tab would end; the camera's own
    /// changes keep it where it is.
    /// </summary>
    void OnViewChanged()
    {
        if (_rebasing || _host.View == _mode)
            return;
        _mode = _host.View;
        _anim = null;
        var (c, s) = _host.View == ViewMode.Tile ? TileTarget(_host.ViewedTile) : WorldTarget();
        Apply(c, s);
    }

    (Vector2 Centre, float Scale) TileTarget(TileState tile) =>
        _tileCamera is { } t && t.Tile == tile.Id ? (t.Centre, t.Scale) : TileHome(tile);

    /// <summary>The world map centred on the viewed tile at the scale last used there, else the whole world.</summary>
    (Vector2 Centre, float Scale) WorldTarget()
    {
        if (_worldScale is not { } s)
            return WorldFit();
        var tile = _host.ViewedTile;
        return (new Vector2(tile.CenterX + 0.5f, tile.CenterY + 0.5f), s);
    }

    /// <summary>Starts a Tab/Enter zoom, remembering where this layer was so the way back returns there.</summary>
    void Animate(int target)
    {
        var (c, s) = Logical();
        if (target < 0)
        {
            if (_host.View == ViewMode.Tile)
                _tileCamera = (_host.ViewedTile.Id, c, s);
            var (tc, ts) = WorldTarget();
            _anim = (c, s, tc, Mathf.Min(ts, Threshold / FadeBand), 0, -1);
        }
        else
        {
            if (_host.View == ViewMode.World)
                _worldScale = s;
            var (tc, ts) = TileTarget(_host.Sim.Tiles[target]);
            _anim = (c, s, tc, Mathf.Max(ts, Threshold * FadeBand), 0, target);
        }
    }

    void StepAnimation(double delta)
    {
        var a = _anim!.Value;
        a.T = Mathf.Min(1f, a.T + (float)delta / AnimSeconds);
        _anim = a.T >= 1 ? null : a;
        float e = Mathf.SmoothStep(0f, 1f, a.T);
        var centre = a.FromC.Lerp(a.ToC, e);
        float scale = Mathf.Exp(Mathf.Lerp(Mathf.Log(a.FromS), Mathf.Log(a.ToS), e));
        // Zooming in, the target tile opens at the threshold whatever lies under the centre on the way.
        Move(centre, scale, a.Tile >= 0 ? _host.Sim.Tiles[a.Tile] : null, remember: false);
    }

    public override void _Process(double delta)
    {
        if (_host.ZoomTarget is { } target)
        {
            _host.ZoomTarget = null;
            Animate(target);
        }
        if (_host.FocusCell is { } focus)
        {
            // A jump from the HUD: centre on that world cell in whichever view is up.
            _host.FocusCell = null;
            _anim = null;
            Apply(new Vector2(focus.X + 0.5f, focus.Y + 0.5f), Logical().Scale);
        }
        if (_anim != null)
            StepAnimation(delta);

        var dir = Godot.Input.GetVector(InputActions.CamLeft, InputActions.CamRight, InputActions.CamUp, InputActions.CamDown);
        Position += dir * PanSpeed * (float)delta / Zoom.X;

        var (c, s) = Logical();
        if (_host.View == ViewMode.Tile && _anim == null && Enterable(c) is { } under && under != _host.ViewedTile)
        {
            // Viewed = the tile under the screen centre (OQ-36), when the player may open it.
            Switch(() => _host.EnterTile(under.Id));
            Apply(c, s);
        }
        UpdateFade(c, s);
    }

    /// <summary>How opaque the tile view is over the world map, and which tile it shows while the world map is up.</summary>
    void UpdateFade(Vector2 centre, float scale)
    {
        float lo = Mathf.Log(Threshold / FadeBand), hi = Mathf.Log(Threshold * FadeBand);
        float opacity = Mathf.SmoothStep(0f, 1f, Mathf.Clamp((Mathf.Log(scale) - lo) / (hi - lo), 0f, 1f));
        if (_host.View == ViewMode.World)
        {
            int next = _anim is { Tile: >= 0 } a ? a.Tile : Enterable(centre)?.Id ?? -1;
            _host.FadeTile = next >= 0 ? _host.Sim.Tiles[next] : null;
            if (_host.FadeTile == null)
                opacity = 0;
        }
        _host.TileOpacity = opacity;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion m when (m.ButtonMask & MouseButtonMask.Middle) != 0:
                _anim = null;
                Position -= m.Relative / Zoom.X;
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                ZoomAtCursor(ZoomStep);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                ZoomAtCursor(1 / ZoomStep);
                break;
        }
    }

    /// <summary>
    /// Zooms keeping the cell under the cursor put. Zooming in over the world map past the threshold opens the tile
    /// under the centre, else the one under the cursor; with neither open to the player the zoom stops short of it.
    /// </summary>
    void ZoomAtCursor(float factor)
    {
        _anim = null;
        var (c, s) = Logical();
        var cursor = ToCells(GetGlobalMousePosition());
        float ns = Mathf.Clamp(s * factor, MinScale, MaxScale);
        TileState? enter = null;
        if (_host.View == ViewMode.World && ns >= Threshold)
        {
            enter = Enterable(cursor - (cursor - c) * s / ns) ?? Enterable(cursor);
            if (enter == null)
                ns = Mathf.Min(ns, Threshold * 0.999f);
        }
        Move(cursor - (cursor - c) * s / ns, ns, enter);
    }
}
