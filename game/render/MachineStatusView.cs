using Godot;
using Wunderwaffe.Sim.World;

namespace Wunderwaffe.Game.Render;

/// <summary>
/// Marks machines on the viewed tile that have stopped (FB-006): amber when waiting for input, red when nothing takes
/// their output, blue when its power network gives it nothing (FB-005). A machine only counts as stopped after <see cref="GraceTicks"/>, so the short gap between batches
/// doesn't flicker. Only machines in view are drawn.
/// </summary>
public partial class MachineStatusView : Node2D
{
    const int GraceTicks = 90;

    static readonly Color Starved = new(1f, 0.72f, 0.2f);
    static readonly Color Blocked = new(1f, 0.3f, 0.28f);
    static readonly Color NoPower = new(0.45f, 0.65f, 1f);

    SimHost _host = null!;

    public override void _Ready()
    {
        _host = SimHost.Instance;
        _host.ShowOnlyIn(this, ViewMode.Tile);
    }

    public override void _Process(double delta) => QueueRedraw();

    /// <summary>Why a machine is stopped, once it has been stopped long enough to matter; null if it's fine.</summary>
    public static MachineStatus? Stopped(MachineState m) =>
        m.IdleTicks >= GraceTicks && m.Status is MachineStatus.Starved or MachineStatus.OutputFull or MachineStatus.NoPower ? m.Status : null;

    public override void _Draw()
    {
        float px = Palette.CellPx;
        var view = GetViewportRect();
        var toWorld = GetCanvasTransform().AffineInverse();
        var visible = new Rect2(toWorld * view.Position, Vector2.Zero).Expand(toWorld * view.End).Grow(px * 3);
        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.GetTicksMsec() / 180f);

        foreach (var b in _host.ViewedTile.Grid.Machines)
        {
            if (Stopped(b.Machine!) is not { } why)
                continue;
            var corner = new Vector2((b.X + b.Width) * px - 4, b.Y * px + 4);
            if (!visible.HasPoint(corner))
                continue;
            var color = why switch { MachineStatus.Starved => Starved, MachineStatus.NoPower => NoPower, _ => Blocked };
            DrawCircle(corner, 4.5f, Colors.Black);
            DrawCircle(corner, 3.5f, color with { A = pulse });
        }
    }
}
