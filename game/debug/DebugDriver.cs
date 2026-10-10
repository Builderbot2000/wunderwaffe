using System.IO;
using System.Linq;
using Godot;
using Wunderwaffe.Game.Render;
using Wunderwaffe.Sim.Core;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Debug;

/// <summary>
/// Debug (`-- --drive [screenshot.png]`): plays a scripted session through the real input path (injected mouse and key
/// events, not commands), logs what happened and saves a screenshot, so a unit-control change can be checked without
/// someone at the keyboard. Needs a window; it does not work with --headless.
/// </summary>
public partial class DebugDriver : Node
{
    /// <summary>How far east of the unit core the unit is sent, in cells.</summary>
    const int Reach = 21;

    // Set on the first frame: the first open row on the home tile with room for the unit and its trip.
    int CoreX = 41, CoreY = 48, TargetX = 62, TargetY = 48;

    SimHost _host = null!;
    string? _shot;
    int _frame;
    long _orderTick = -1;
    Vector2? _want;

    public static void Attach(SimHost host, string? screenshot)
    {
        var driver = new DebugDriver { _host = host, _shot = screenshot };
        host.AddChild(driver);
    }

    Vector2 Screen(double cellX, double cellY) =>
        GetViewport().GetCanvasTransform() * (new Vector2((float)cellX, (float)cellY) * Palette.CellPx);

    // Injected motion events don't move the viewport's notion of where the mouse is, so warp the real cursor.
    // Viewport.WarpMouse, not Input.WarpMouse: the latter takes window pixels, which only match viewport coordinates
    // when the window happens to be 1920x1080 (stretch mode scales the canvas, e.g. by 1.5 under Windows display scaling).
    void Mouse(Vector2 screen)
    {
        GetViewport().WarpMouse(screen);
        _want = screen;
    }

    // Fails the run if the cursor isn't where the last Mouse() put it (OS refused the warp, someone moved the mouse).
    bool MouseLanded()
    {
        var at = GetViewport().GetMousePosition();
        if (_want is not { } want || at.DistanceTo(want) <= 2)
            return true;
        GD.PrintErr($"[drive] FAIL: mouse at {at}, want {want}");
        GetTree().Quit(1);
        return false;
    }

    void Click(Vector2 screen, MouseButton button)
    {
        // PushInput on the root viewport expects window pixels, like a real event; it applies the stretch itself.
        var window = GetViewport().GetScreenTransform() * screen;
        foreach (bool pressed in new[] { true, false })
        {
            GetViewport().PushInput(new InputEventMouseButton
            {
                Position = window, GlobalPosition = window, ButtonIndex = button, Pressed = pressed,
                ButtonMask = pressed ? (button == MouseButton.Left ? MouseButtonMask.Left : MouseButtonMask.Right) : 0,
            });
        }
    }

    void Key(Key key)
    {
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = pressed });
    }

    /// <summary>The home tile's shape and terrain depend on the seed: find a clear run of ground for the unit and its trip.</summary>
    void FindRoom(TileState tile)
    {
        // Nearest the middle of the tile, where the camera starts, so it is all on screen.
        var g = tile.Grid;
        int mx = tile.CenterX - tile.OriginX - Reach / 2, my = tile.CenterY - tile.OriginY;
        var spots = Enumerable.Range(0, g.Width * g.Height)
            .Select(i => (X: i % g.Width, Y: i / g.Width))
            .Where(p => g.InBounds(p.X - 1, p.Y - 1, Reach + 3, 3) && g.Buildable(p.X - 1, p.Y - 1, Reach + 3, 3)
                && g.IsFree(p.X - 1, p.Y - 1, Reach + 3, 3))
            .OrderBy(p => System.Math.Abs(p.X - mx) + System.Math.Abs(p.Y - my));
        foreach (var (x, y) in spots)
        {
            (CoreX, CoreY, TargetX, TargetY) = (x, y, x + Reach, y);
            GD.Print($"[drive] unit at ({CoreX},{CoreY}), target ({TargetX},{TargetY})");
            return;
        }
        GD.PrintErr("[drive] no room for the unit on the home tile");
    }

    public override void _Process(double delta)
    {
        var sim = _host.Sim;
        int me = _host.LocalPlayer;
        var tile = _host.ViewedTile;
        _frame++;

        switch (_frame)
        {
            case 1:
                FindRoom(tile);
                break;
            case 5:
                // Three framed cells with an engine on each end and the unit core between (commands: this is the setup).
                // Unit parts cost steel.
                foreach (var item in new[] { "steel", "iron_plate", "copper_plate" })
                    sim.Enqueue(new DebugGrantCommand(me, tile.Id, item, 100));
                for (int x = CoreX - 1; x <= CoreX + 1; x++)
                    sim.Enqueue(new PlaceFrameCommand(me, tile.Id, x, CoreY));
                sim.Enqueue(new PlaceBuildingCommand(me, tile.Id, "thruster", CoreX - 1, CoreY, 0));
                sim.Enqueue(new PlaceBuildingCommand(me, tile.Id, "unit_core", CoreX, CoreY));
                sim.Enqueue(new PlaceBuildingCommand(me, tile.Id, "thruster", CoreX + 1, CoreY, 0));
                break;
            case 20:
                GD.Print($"[drive] built: {tile.Grid.BuildingCount} buildings, {tile.Grid.FrameCount} frames");
                Mouse(Screen(CoreX + 0.5, CoreY + 0.5));
                break;
            case 22:
                GD.Print($"[drive] viewport mouse {GetViewport().GetMousePosition()} want {_want}");
                if (MouseLanded())
                    Key(Godot.Key.G);
                break;
            case 40:
                GD.Print($"[drive] after G: {sim.Units.Count} units, {tile.Grid.BuildingCount} buildings");
                if (sim.Units.Count > 0)
                    Mouse(Screen(sim.Units[0].X / (double)Unit.CellScale - tile.OriginX, sim.Units[0].Y / (double)Unit.CellScale - tile.OriginY));
                break;
            case 43:
                if (MouseLanded())
                    Click(GetViewport().GetMousePosition(), MouseButton.Left);
                break;
            case 46:
                GD.Print($"[drive] after click: selected {_host.Selected.Count}");
                Mouse(Screen(TargetX + 0.5, TargetY + 0.5));
                break;
            case 49:
                if (!MouseLanded())
                    break;
                Click(GetViewport().GetMousePosition(), MouseButton.Right);
                _orderTick = sim.Tick;
                break;
            case 52:
                if (sim.Units.Count > 0)
                    GD.Print($"[drive] after right-click: moving={sim.Units[0].Moving} maxSpeed={sim.Units[0].MaxSpeed}");
                break;
            case int when _orderTick >= 0 && sim.Tick >= _orderTick + 900:
                if (sim.Units.Count > 0)
                {
                    var u = sim.Units[0];
                    GD.Print($"[drive] end: at ({u.X / (double)Unit.CellScale - tile.OriginX:F1}, {u.Y / (double)Unit.CellScale - tile.OriginY:F1}) " +
                             $"target ({TargetX + 0.5}, {TargetY + 0.5}) angle {u.Angle * 360 / Trig.Turn}deg moving={u.Moving}");
                }
                if (_shot != null)
                {
                    GetViewport().GetTexture().GetImage().SavePng(Path.GetFullPath(_shot));
                    GD.Print($"[drive] screenshot {Path.GetFullPath(_shot)}");
                }
                GetTree().Quit();
                break;
        }
    }
}
