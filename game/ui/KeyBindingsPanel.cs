using System;
using Godot;
using Wunderwaffe.Game.Input;

namespace Wunderwaffe.Game.Screens;

/// <summary>
/// The rebinding screen (UI-005): every action with its key; click one, then press the new key (modifiers count, Esc
/// cancels). Changes apply at once and are saved with the settings.
/// </summary>
public partial class KeyBindingsPanel : PanelContainer
{
    VBoxContainer _list = null!;
    string? _waitingFor;
    Label _hint = null!;

    public static KeyBindingsPanel Build(Action onBack)
    {
        var panel = new KeyBindingsPanel { CustomMinimumSize = new Vector2(520, 0) };
        var col = Ui.Column(10);
        col.AddChild(Ui.Heading("Key bindings"));
        panel._list = Ui.Column(4);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(500, 460), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(panel._list);
        col.AddChild(scroll);
        panel._hint = Ui.Label("Click a key to change it.", Ui.SmallFont, Ui.Muted);
        col.AddChild(panel._hint);
        var row = Ui.Row();
        row.AddChild(Ui.Button("Reset to defaults", () =>
        {
            InputActions.ResetAll();
            GameSettings.SaveKeys();
            panel.Fill();
        }));
        row.AddChild(Ui.Button("Back", onBack));
        col.AddChild(row);
        panel.AddChild(col);
        panel.Fill();
        return panel;
    }

    void Fill()
    {
        Ui.Clear(_list);
        foreach (string action in InputActions.All)
        {
            var row = Ui.Row(8);
            var name = Ui.Label(InputActions.Name(action), Ui.FontSize, InputActions.Changed(action) ? Ui.Accent : Ui.Text);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(name);
            string keys = string.Join(", ", System.Linq.Enumerable.Select(InputActions.Keys(action), InputActions.KeyText));
            string label = _waitingFor == action ? "press a key…" : keys.Length == 0 ? "(none)" : keys;
            row.AddChild(Ui.Button(label, () =>
            {
                _waitingFor = action;
                _hint.Text = $"Press the new key for {InputActions.Name(action)}; Esc cancels.";
                Fill();
            }, 180));
            _list.AddChild(row);
        }
    }

    public override void _Input(InputEvent e)
    {
        if (_waitingFor == null || e is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        // A lone modifier is the start of a combination, not the binding.
        if (key.PhysicalKeycode is Key.Ctrl or Key.Shift or Key.Alt or Key.Meta)
            return;
        GetViewport().SetInputAsHandled();
        if (key.PhysicalKeycode != Key.Escape)
        {
            InputActions.Rebind(_waitingFor, key.GetPhysicalKeycodeWithModifiers());
            GameSettings.SaveKeys();
        }
        _waitingFor = null;
        _hint.Text = "Click a key to change it.";
        Fill();
    }
}
