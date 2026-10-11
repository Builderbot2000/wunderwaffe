using Godot;

namespace Wunderwaffe.Game.Screens;

/// <summary>
/// A small line chart of one item's last 10 minutes on a tile (FB-011): made per bucket in green, used in amber,
/// oldest on the left, on a shared scale.
/// </summary>
public partial class HistoryGraph : Control
{
    long[] _made = [], _used = [];

    public void SetData(long[] made, long[] used)
    {
        _made = made;
        _used = used;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(1f, 1f, 1f, 0.05f));
        long max = 1;
        foreach (long v in _made)
            max = System.Math.Max(max, v);
        foreach (long v in _used)
            max = System.Math.Max(max, v);
        Plot(_used, Ui.Warn, max, size);
        Plot(_made, Ui.Good, max, size);
    }

    void Plot(long[] values, Color color, long max, Vector2 size)
    {
        if (values.Length < 2)
            return;
        var points = new Vector2[values.Length];
        for (int i = 0; i < values.Length; i++)
            points[i] = new Vector2(i * size.X / (values.Length - 1), size.Y - 1 - values[i] * (size.Y - 2) / max);
        DrawPolyline(points, color, 1.5f, true);
    }
}
