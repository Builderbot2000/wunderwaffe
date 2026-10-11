using System.Collections.Generic;
using Godot;
using Wunderwaffe.Game.Render;

namespace Wunderwaffe.Game.Screens;

/// <summary>
/// The shared look of every menu and HUD panel, and small builders for the controls they're made of. Screens are
/// built in code rather than in .tscn files so their layout stays next to the logic that fills it.
/// </summary>
public static class Ui
{
    public static readonly Color Text = new(0.92f, 0.93f, 0.95f);
    public static readonly Color Muted = new(0.62f, 0.65f, 0.7f);
    public static readonly Color Accent = new(0.98f, 0.78f, 0.3f);
    public static Color Good => Palette.Good;
    public static Color Bad => Palette.Bad;
    public static readonly Color Warn = new(1f, 0.72f, 0.25f);
    static readonly Color PanelBg = new(0.07f, 0.08f, 0.1f, 0.9f);
    static readonly Color PanelEdge = new(0.28f, 0.31f, 0.36f);
    static readonly Color ButtonBg = new(0.16f, 0.18f, 0.21f);
    static readonly Color ButtonHover = new(0.23f, 0.26f, 0.3f);

    public const int FontSize = 17;
    public const int SmallFont = 14;

    static Theme? _theme;

    public static Theme Theme => _theme ??= BuildTheme();

    static StyleBoxFlat Box(Color bg, Color edge, int border = 1, int margin = 12, int radius = 6) => new()
    {
        BgColor = bg,
        BorderColor = edge,
        BorderWidthLeft = border, BorderWidthTop = border, BorderWidthRight = border, BorderWidthBottom = border,
        CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
        ContentMarginLeft = margin, ContentMarginRight = margin, ContentMarginTop = margin * 0.75f, ContentMarginBottom = margin * 0.75f,
    };

    static Theme BuildTheme()
    {
        var t = new Theme { DefaultFontSize = FontSize };
        t.SetStylebox("panel", "PanelContainer", Box(PanelBg, PanelEdge));
        t.SetColor("font_color", "Label", Text);

        t.SetStylebox("normal", "Button", Box(ButtonBg, PanelEdge, margin: 10));
        t.SetStylebox("hover", "Button", Box(ButtonHover, Accent with { A = 0.6f }, margin: 10));
        t.SetStylebox("pressed", "Button", Box(ButtonHover, Accent, border: 2, margin: 10));
        t.SetStylebox("hover_pressed", "Button", Box(ButtonHover, Accent, border: 2, margin: 10));
        t.SetStylebox("disabled", "Button", Box(ButtonBg with { A = 0.5f }, PanelEdge with { A = 0.4f }, margin: 10));
        t.SetStylebox("focus", "Button", new StyleBoxEmpty());
        t.SetColor("font_color", "Button", Text);
        t.SetColor("font_hover_color", "Button", Accent);
        t.SetColor("font_pressed_color", "Button", Accent);
        t.SetColor("font_hover_pressed_color", "Button", Accent);
        t.SetColor("font_disabled_color", "Button", Muted with { A = 0.6f });

        t.SetStylebox("normal", "LineEdit", Box(new Color(0.04f, 0.05f, 0.06f), PanelEdge, margin: 8));
        t.SetStylebox("focus", "LineEdit", Box(new Color(0.04f, 0.05f, 0.06f), Accent, margin: 8));

        t.SetStylebox("background", "ProgressBar", Box(new Color(0.03f, 0.04f, 0.05f), PanelEdge, margin: 0, radius: 3));
        t.SetStylebox("fill", "ProgressBar", Box(Accent, Accent, border: 0, margin: 0, radius: 3));

        t.SetStylebox("panel", "TooltipPanel", Box(new Color(0.04f, 0.05f, 0.06f, 0.97f), Accent with { A = 0.5f }, margin: 10));
        t.SetColor("font_color", "TooltipLabel", Text);

        t.SetStylebox("panel", "ItemList", Box(new Color(0.04f, 0.05f, 0.06f), PanelEdge, margin: 6));
        t.SetColor("font_color", "ItemList", Text);
        t.SetColor("font_selected_color", "ItemList", Accent);
        return t;
    }

    public static Label Label(string text = "", int size = FontSize, Color? color = null, int wrapWidth = 0)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        if (size != FontSize)
            l.AddThemeFontSizeOverride("font_size", size);
        if (color is { } c)
            l.AddThemeColorOverride("font_color", c);
        if (wrapWidth > 0)
        {
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.CustomMinimumSize = new Vector2(wrapWidth, 0);
        }
        return l;
    }

    /// <summary>Upper-case, letter-spaced section heading.</summary>
    public static Label Heading(string text, Color? color = null) => Label(text.ToUpperInvariant(), SmallFont, color ?? Accent);

    public static Button Button(string text, System.Action onPressed, int minWidth = 0)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(minWidth, 0), FocusMode = Control.FocusModeEnum.None };
        b.Pressed += onPressed;
        return b;
    }

    public static PanelContainer Panel(Control content, int minWidth = 0)
    {
        var p = new PanelContainer { CustomMinimumSize = new Vector2(minWidth, 0) };
        p.AddChild(content);
        return p;
    }

    public static VBoxContainer Column(int separation = 6)
    {
        var v = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        v.AddThemeConstantOverride("separation", separation);
        return v;
    }

    public static HBoxContainer Row(int separation = 8)
    {
        var h = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        h.AddThemeConstantOverride("separation", separation);
        return h;
    }

    public static TextureRect Icon(string contentId, int size = 22)
    {
        var tex = Sprites.Get(contentId);
        var r = new TextureRect
        {
            Texture = tex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(size, size),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        if (tex == null)
        {
            // No sprite yet: a swatch in the item's placeholder colour.
            r.Texture = Swatch;
            r.Modulate = Palette.Deposit(contentId);
        }
        return r;
    }

    static readonly Texture2D Swatch = ImageTexture.CreateFromImage(Image.CreateFromData(1, 1, false, Image.Format.Rgba8, [255, 255, 255, 255]));

    /// <summary>A row of icon + count per item, e.g. a cost.</summary>
    public static HBoxContainer Items(IEnumerable<KeyValuePair<string, int>> items, int size = SmallFont, Color? color = null)
    {
        var row = Row(4);
        foreach (var (id, n) in items)
        {
            row.AddChild(Icon(id, size + 4));
            row.AddChild(Label(n.ToString(), size, color));
        }
        return row;
    }

    /// <summary>Anchors <paramref name="c"/> to a corner or edge of its parent, growing away from that edge.</summary>
    public static T Pin<T>(T c, Control.LayoutPreset preset, int margin = 16) where T : Control
    {
        c.SetAnchorsAndOffsetsPreset(preset, Control.LayoutPresetMode.Minsize, margin);
        c.GrowHorizontal = preset switch
        {
            Control.LayoutPreset.TopRight or Control.LayoutPreset.BottomRight or Control.LayoutPreset.CenterRight => Control.GrowDirection.Begin,
            Control.LayoutPreset.CenterTop or Control.LayoutPreset.CenterBottom or Control.LayoutPreset.Center => Control.GrowDirection.Both,
            _ => Control.GrowDirection.End,
        };
        c.GrowVertical = preset switch
        {
            Control.LayoutPreset.BottomLeft or Control.LayoutPreset.BottomRight or Control.LayoutPreset.CenterBottom => Control.GrowDirection.Begin,
            Control.LayoutPreset.CenterLeft or Control.LayoutPreset.CenterRight or Control.LayoutPreset.Center => Control.GrowDirection.Both,
            _ => Control.GrowDirection.End,
        };
        return c;
    }

    /// <summary>A full-screen control that lays out its children but lets clicks through to the world.</summary>
    public static Control Overlay()
    {
        var c = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = Theme };
        c.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return c;
    }

    /// <summary>Removes and frees every child.</summary>
    public static void Clear(Node n)
    {
        foreach (var child in n.GetChildren())
        {
            n.RemoveChild(child);
            child.QueueFree();
        }
    }

    /// <summary>"12:34" or "1:02:03" for a tick count.</summary>
    public static string Clock(long ticks)
    {
        long s = ticks / Sim.Simulation.TicksPerSecond;
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
    }
}
