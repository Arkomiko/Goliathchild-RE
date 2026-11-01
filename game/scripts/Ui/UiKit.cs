using System;
using Godot;

namespace GoliathChildRE.Ui;

/// <summary>
/// Короткие «фабрики» элементов интерфейса. Весь интерфейс собирается кодом, а не в .tscn:
/// так его проще менять без редактора Godot и легче держать в одном стиле.
/// </summary>
public static class UiKit
{
    /// <summary>Растянуть элемент на всего родителя.</summary>
    public static T Full<T>(T control) where T : Control
    {
        control.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return control;
    }

    public static Label Label(string text, Font font, int size, Color color, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var label = new Label { Text = text, HorizontalAlignment = align };
        label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    /// <summary>Текст с переносом строк (описания, подсказки).</summary>
    public static Label Paragraph(string text, Font font, int size, Color color)
    {
        var label = Label(text, font, size, color);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return label;
    }

    /// <summary>Прямоугольник с фоном, рамкой, скруглением и внутренними отступами.</summary>
    public static StyleBoxFlat Box(Color bg, Color border = default, int borderWidth = 0, int radius = 0, int padH = 0, int padV = 0)
    {
        var box = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            ContentMarginLeft = padH,
            ContentMarginRight = padH,
            ContentMarginTop = padV,
            ContentMarginBottom = padV,
        };
        box.SetBorderWidthAll(borderWidth);
        box.SetCornerRadiusAll(radius);
        return box;
    }

    /// <summary>Пустой StyleBox — убрать стандартный фон кнопки или панели.</summary>
    public static StyleBoxEmpty Empty(int padH = 0, int padV = 0) => new()
    {
        ContentMarginLeft = padH,
        ContentMarginRight = padH,
        ContentMarginTop = padV,
        ContentMarginBottom = padV,
    };

    /// <summary>Кнопка с одинаковым оформлением всех состояний, отличающимся только цветом текста и фона.</summary>
    public static Button Button(string text, Font font, int size, Color fg, Color fgHover, StyleBox normal, StyleBox hover, StyleBox pressed = null)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.All };
        b.AddThemeFontOverride("font", font);
        b.AddThemeFontSizeOverride("font_size", size);
        b.AddThemeColorOverride("font_color", fg);
        b.AddThemeColorOverride("font_hover_color", fgHover);
        b.AddThemeColorOverride("font_focus_color", fgHover);
        b.AddThemeColorOverride("font_pressed_color", fgHover);
        b.AddThemeColorOverride("font_disabled_color", Palette.TextDim);
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("focus", hover);
        b.AddThemeStyleboxOverride("pressed", pressed ?? hover);
        b.AddThemeStyleboxOverride("disabled", normal);
        return b;
    }

    /// <summary>Кнопка в стиле «киношного» меню: тёмная полоса, бирюзовая рамка при наведении.</summary>
    public static Button CinemaButton(string text, int size = 40)
    {
        var normal = Box(new Color(1, 1, 1, 0.03f), Palette.CinemaLine, 2, 6, 36, 18);
        var hover = Box(new Color(0.17f, 0.6f, 0.64f, 0.18f), Palette.TealBright, 2, 6, 36, 18);
        var b = Button(text, Fonts.Menu, size, Palette.Text, Palette.TealBright, normal, hover);
        b.Alignment = HorizontalAlignment.Left;
        return b;
    }

    public static VBoxContainer VBox(int separation)
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", separation);
        return v;
    }

    public static HBoxContainer HBox(int separation)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", separation);
        return h;
    }

    public static MarginContainer Margin(int left, int top, int right, int bottom)
    {
        var m = new MarginContainer();
        m.AddThemeConstantOverride("margin_left", left);
        m.AddThemeConstantOverride("margin_top", top);
        m.AddThemeConstantOverride("margin_right", right);
        m.AddThemeConstantOverride("margin_bottom", bottom);
        return m;
    }

    /// <summary>Пустое место заданной высоты (отступы между группами меню).</summary>
    public static Control Spacer(int height, int width = 0) => new() { CustomMinimumSize = new Vector2(width, height), MouseFilter = Control.MouseFilterEnum.Ignore };

    /// <summary>Элемент, занимающий всё свободное место в контейнере (прижимает соседей к краям).</summary>
    public static Control Expander() => new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };

    /// <summary>Панель с фоном.</summary>
    public static PanelContainer Panel(StyleBox style)
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", style);
        return p;
    }

    /// <summary>Подписаться на нажатие и вернуть кнопку (для цепочек при сборке интерфейса).</summary>
    public static T OnPressed<T>(this T button, Action action) where T : BaseButton
    {
        button.Pressed += action;
        return button;
    }

    /// <summary>Добавить ребёнка и вернуть его — удобно при сборке дерева.</summary>
    public static T Add<T>(this Node parent, T child) where T : Node
    {
        parent.AddChild(child);
        return child;
    }

    private static Theme _cinema;

    /// <summary>Квадратный флажок: рамка, а у включённого — заливка с отступом.</summary>
    public static ImageTexture CheckTexture(int size, bool on, Color color)
    {
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        var b = Math.Max(2, size / 14);
        img.FillRect(new Rect2I(0, 0, size, b), color);
        img.FillRect(new Rect2I(0, size - b, size, b), color);
        img.FillRect(new Rect2I(0, 0, b, size), color);
        img.FillRect(new Rect2I(size - b, 0, b, size), color);
        if (on) img.FillRect(new Rect2I(b * 3, b * 3, size - b * 6, size - b * 6), color);
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>Круг (ручка слайдера). Края сглажены по расстоянию до центра.</summary>
    public static ImageTexture CircleTexture(int size, Color color)
    {
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var r = size / 2f;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var d = new Vector2(x + 0.5f - r, y + 0.5f - r).Length();
                var a = Mathf.Clamp(r - d, 0f, 1f);
                img.SetPixel(x, y, new Color(color, color.A * a));
            }
        }
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>Стрелка вниз для выпадающего списка.</summary>
    public static ImageTexture ArrowTexture(int size, Color color)
    {
        var img = Image.CreateEmpty(size, size / 2 + 2, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        for (var y = 0; y < size / 2; y++)
        {
            for (var x = y; x < size - y; x++) img.SetPixel(x, y + 1, color);
        }
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>Тема для стандартных элементов (слайдеры, чекбоксы, выпадающие списки) в «киношных» экранах.</summary>
    public static Theme CinemaTheme()
    {
        // Тема собирается один раз: значки рисуются попиксельно, пересоздавать их на каждом экране незачем.
        if (_cinema != null) return _cinema;
        var t = new Theme { DefaultFont = Fonts.Body, DefaultFontSize = 34 };
        _cinema = t;
        t.SetColor("font_color", "Label", Palette.Text);
        t.SetColor("font_color", "CheckBox", Palette.Text);
        t.SetColor("font_hover_color", "CheckBox", Palette.TealBright);
        t.SetColor("font_pressed_color", "CheckBox", Palette.Text);
        t.SetColor("font_color", "OptionButton", Palette.Text);
        t.SetColor("font_hover_color", "OptionButton", Palette.TealBright);
        t.SetColor("font_color", "LineEdit", Palette.Text);
        t.SetColor("font_color", "PopupMenu", Palette.Text);
        t.SetColor("font_hover_color", "PopupMenu", Palette.TealBright);
        t.SetFontSize("font_size", "PopupMenu", 30);

        var field = Box(new Color(1, 1, 1, 0.05f), Palette.CinemaLine, 2, 6, 20, 10);
        var fieldHover = Box(new Color(0.17f, 0.6f, 0.64f, 0.15f), Palette.TealBright, 2, 6, 20, 10);
        foreach (var type in new[] { "OptionButton", "LineEdit" })
        {
            t.SetStylebox("normal", type, field);
            t.SetStylebox("hover", type, fieldHover);
            t.SetStylebox("focus", type, fieldHover);
            t.SetStylebox("pressed", type, fieldHover);
        }
        t.SetStylebox("read_only", "LineEdit", field);
        t.SetStylebox("panel", "PopupMenu", Box(new Color("0d1216"), Palette.TealBright, 2, 6, 12, 12));
        t.SetStylebox("hover", "PopupMenu", Box(new Color(0.17f, 0.6f, 0.64f, 0.3f), default, 0, 4));

        // Стандартные значки Godot рассчитаны на 1080p и при 1440p выглядят крошечными — рисуем свои крупные.
        t.SetIcon("checked", "CheckBox", CheckTexture(44, true, Palette.TealBright));
        t.SetIcon("unchecked", "CheckBox", CheckTexture(44, false, Palette.TealBright));
        t.SetIcon("checked_disabled", "CheckBox", CheckTexture(44, true, Palette.TextDim));
        t.SetIcon("unchecked_disabled", "CheckBox", CheckTexture(44, false, Palette.TextDim));
        t.SetIcon("grabber", "HSlider", CircleTexture(36, Palette.Text));
        t.SetIcon("grabber_highlight", "HSlider", CircleTexture(36, Palette.TealBright));
        t.SetIcon("arrow", "OptionButton", ArrowTexture(28, Palette.TextMuted));

        t.SetStylebox("slider", "HSlider", Box(new Color(1, 1, 1, 0.12f), default, 0, 4, 0, 4));
        t.SetStylebox("grabber_area", "HSlider", Box(Palette.Teal, default, 0, 4, 0, 4));
        t.SetStylebox("grabber_area_highlight", "HSlider", Box(Palette.TealBright, default, 0, 4, 0, 4));
        t.SetConstant("center_grabber", "HSlider", 1);

        var tabSel = Box(new Color(0.17f, 0.6f, 0.64f, 0.25f), Palette.TealBright, 0, 6, 28, 12);
        tabSel.BorderWidthBottom = 3;
        t.SetStylebox("tab_selected", "TabContainer", tabSel);
        t.SetStylebox("tab_unselected", "TabContainer", Box(new Color(1, 1, 1, 0.03f), default, 0, 6, 28, 12));
        t.SetStylebox("tab_hovered", "TabContainer", Box(new Color(1, 1, 1, 0.08f), default, 0, 6, 28, 12));
        t.SetStylebox("panel", "TabContainer", Box(new Color(0, 0, 0, 0.25f), Palette.CinemaLine, 1, 6, 36, 28));
        t.SetColor("font_selected_color", "TabContainer", Palette.TealBright);
        t.SetColor("font_unselected_color", "TabContainer", Palette.TextMuted);
        t.SetColor("font_hovered_color", "TabContainer", Palette.Text);
        t.SetFont("font", "TabContainer", Fonts.Menu);
        t.SetFontSize("font_size", "TabContainer", 28);

        t.SetStylebox("panel", "PanelContainer", Box(Palette.CinemaPanel, Palette.CinemaLine, 2, 10, 40, 32));
        t.SetStylebox("scroll", "VScrollBar", Box(new Color(1, 1, 1, 0.05f), default, 0, 6));
        t.SetStylebox("grabber", "VScrollBar", Box(new Color(0.17f, 0.6f, 0.64f, 0.5f), default, 0, 6));
        t.SetStylebox("grabber_highlight", "VScrollBar", Box(Palette.TealBright, default, 0, 6));
        t.SetStylebox("grabber_pressed", "VScrollBar", Box(Palette.TealBright, default, 0, 6));
        return t;
    }
}
