using Godot;
using Wunderwaffe.Game.Render;

namespace Wunderwaffe.Game.Input;

/// <summary>Keyboard / middle-drag pan and wheel zoom toward the cursor.</summary>
public partial class CameraController : Camera2D
{
    [Export] public float PanSpeed = 900f;
    [Export] public float MinZoom = 0.25f;
    [Export] public float MaxZoom = 4f;
    [Export] public float ZoomStep = 1.15f;

    [Export] public WorldMapView WorldMap = null!;

    SimHost _host = null!;
    ViewMode _mode;
    (Vector2 Position, Vector2 Zoom)? _tileCamera, _worldCamera;
    int _tileCameraFor = -1;

    public override void _Ready()
    {
        _host = SimHost.Instance;
        _mode = _host.View;
        _host.WatchView(this, OnViewChanged);
        if (_mode == ViewMode.World)
        {
            FitWorld();
        }
        else
        {
            CenterOnTile();
            _tileCameraFor = _host.ViewedTile.Id;
        }
    }

    void FitWorld()
    {
        var (center, zoom) = WorldMap.Fit(GetViewportRect().Size);
        Position = center;
        Zoom = new Vector2(zoom, zoom);
    }

    /// <summary>The middle of the tile, where its core is, at 1:1.</summary>
    void CenterOnTile()
    {
        var tile = _host.ViewedTile;
        Position = new Vector2(tile.CenterX - tile.OriginX + 0.5f, tile.CenterY - tile.OriginY + 0.5f) * Palette.CellPx;
        Zoom = Vector2.One;
    }

    /// <summary>
    /// Each layer keeps its own zoom. The tile view returns to where you left the same tile; the world map centres on
    /// the tile you just left, so Tab shows where you are in the world.
    /// </summary>
    void OnViewChanged()
    {
        if (_host.View == _mode)
            return;
        var here = (Position, Zoom);
        if (_mode == ViewMode.Tile)
            _tileCamera = here;
        else
            _worldCamera = here;
        _mode = _host.View;

        if (_mode == ViewMode.Tile)
        {
            if (_tileCamera is { } t && _tileCameraFor == _host.ViewedTile.Id)
                (Position, Zoom) = t;
            else
                CenterOnTile();
            _tileCameraFor = _host.ViewedTile.Id;
        }
        else if (_worldCamera is { } w)
        {
            Zoom = w.Zoom;
            Position = WorldMapView.Center(_host.ViewedTile);
        }
        else
        {
            FitWorld();
        }
    }

    public override void _Process(double delta)
    {
        var dir = Godot.Input.GetVector(InputActions.CamLeft, InputActions.CamRight, InputActions.CamUp, InputActions.CamDown);
        Position += dir * PanSpeed * (float)delta / Zoom.X;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion m when (m.ButtonMask & MouseButtonMask.Middle) != 0:
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

    void ZoomAtCursor(float factor)
    {
        var before = GetGlobalMousePosition();
        float z = Mathf.Clamp(Zoom.X * factor, MinZoom, MaxZoom);
        Zoom = new Vector2(z, z);
        ForceUpdateScroll();
        Position += before - GetGlobalMousePosition();
    }
}
