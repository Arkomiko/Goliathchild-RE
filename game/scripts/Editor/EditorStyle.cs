using GoliathChildRE.Ui;
using Godot;

namespace GoliathChildRE.Editor;

/// <summary>
/// Оформление редактора GiC_OS: тёмная тема в духе Blueprint из Unreal (ответ 62).
/// Окно редактора работает в обычных экранных пикселях (без масштабирования игры), поэтому размеры шрифтов привычные.
/// </summary>
public static class EditorStyle
{
    public const int FontSize = 17;

    /// <summary>Цвет блока на таймлайне и заголовка узла по виду команды.</summary>
    public static Color KindColor(string kind) => kind switch
    {
        "say" => new Color("3949ab"),
        "doc" => new Color("546e7a"),
        "scene" or "show" or "hide" or "viewer" => new Color("00897b"),
        "music" or "ambient" or "sound" or "voice" or "stop" => new Color("c49000"),
        "pause" => new Color("616161"),
        "glitch" or "shake" or "flash" => new Color("c62828"),
        "jump" or "call" or "return" => new Color("6d4c41"),
        "set" or "if" or "menu" or "input" => new Color("8e24aa"),
        "checkpoint" => new Color("2e7d32"),
        "end" => new Color("b71c1c"),
        _ => kind.StartsWith("os") ? new Color("00838f") : new Color("455a64"),
    };

    public static Theme Build()
    {
        var t = new Theme { DefaultFont = Fonts.Body, DefaultFontSize = FontSize };

        var panel = UiKit.Box(Palette.EdPanel, Palette.EdLine, 1, 4, 8, 6);
        var field = UiKit.Box(Palette.EdBg, Palette.EdLine, 1, 4, 8, 4);
        var fieldFocus = UiKit.Box(Palette.EdBg, Palette.EdAccent, 1, 4, 8, 4);
        var button = UiKit.Box(Palette.EdPanelLight, Palette.EdLine, 1, 4, 12, 5);
        var buttonHover = UiKit.Box(Palette.EdPanelLight.Lightened(0.08f), Palette.EdAccent, 1, 4, 12, 5);
        var buttonPressed = UiKit.Box(Palette.EdAccent.Darkened(0.3f), Palette.EdAccent, 1, 4, 12, 5);

        foreach (var type in new[] { "Label", "Button", "MenuButton", "OptionButton", "CheckBox", "CheckButton", "LineEdit", "ItemList", "Tree", "TabContainer", "PopupMenu", "CodeEdit", "RichTextLabel" })
        {
            t.SetColor("font_color", type, Palette.EdText);
        }
        t.SetColor("default_color", "RichTextLabel", Palette.EdText);
        foreach (var type in new[] { "Button", "MenuButton", "OptionButton" })
        {
            t.SetStylebox("normal", type, button);
            t.SetStylebox("hover", type, buttonHover);
            t.SetStylebox("pressed", type, buttonPressed);
            t.SetStylebox("focus", type, buttonHover);
            t.SetStylebox("disabled", type, button);
            t.SetColor("font_hover_color", type, Colors.White);
            t.SetColor("font_pressed_color", type, Colors.White);
            t.SetColor("font_disabled_color", type, Palette.EdMuted);
        }
        t.SetStylebox("normal", "LineEdit", field);
        t.SetStylebox("focus", "LineEdit", fieldFocus);
        t.SetStylebox("panel", "ItemList", field);
        t.SetStylebox("focus", "ItemList", fieldFocus);
        t.SetStylebox("selected", "ItemList", UiKit.Box(Palette.EdAccent.Darkened(0.35f)));
        t.SetStylebox("selected_focus", "ItemList", UiKit.Box(Palette.EdAccent.Darkened(0.2f)));
        t.SetStylebox("panel", "Tree", field);
        t.SetStylebox("focus", "Tree", fieldFocus);
        t.SetStylebox("selected", "Tree", UiKit.Box(Palette.EdAccent.Darkened(0.35f)));
        t.SetStylebox("selected_focus", "Tree", UiKit.Box(Palette.EdAccent.Darkened(0.2f)));
        t.SetColor("font_selected_color", "Tree", Colors.White);
        t.SetStylebox("panel", "PopupMenu", UiKit.Box(Palette.EdPanel, Palette.EdAccent, 1, 4, 6, 6));
        t.SetStylebox("hover", "PopupMenu", UiKit.Box(Palette.EdAccent.Darkened(0.3f), default, 0, 3));
        t.SetColor("font_hover_color", "PopupMenu", Colors.White);
        t.SetStylebox("panel", "PanelContainer", panel);
        t.SetStylebox("panel", "TabContainer", UiKit.Box(Palette.EdBg, Palette.EdLine, 1, 0, 0, 0));
        t.SetStylebox("tab_selected", "TabContainer", UiKit.Box(Palette.EdBg, Palette.EdAccent, 0, 0, 16, 6));
        t.SetStylebox("tab_unselected", "TabContainer", UiKit.Box(Palette.EdPanel, default, 0, 0, 16, 6));
        t.SetStylebox("tab_hovered", "TabContainer", UiKit.Box(Palette.EdPanelLight, default, 0, 0, 16, 6));
        t.SetColor("font_selected_color", "TabContainer", Colors.White);
        t.SetColor("font_unselected_color", "TabContainer", Palette.EdMuted);
        t.SetColor("font_hovered_color", "TabContainer", Palette.EdText);

        // Редактор кода: моноширинный шрифт и тёмный фон.
        t.SetFont("font", "CodeEdit", Fonts.Code);
        t.SetFontSize("font_size", "CodeEdit", 16);
        t.SetStylebox("normal", "CodeEdit", UiKit.Box(new Color("121418"), default, 0, 0, 6, 6));
        t.SetStylebox("focus", "CodeEdit", UiKit.Box(new Color("121418"), default, 0, 0, 6, 6));
        t.SetColor("current_line_color", "CodeEdit", new Color(1, 1, 1, 0.04f));
        t.SetColor("caret_color", "CodeEdit", Palette.EdAccent);
        t.SetColor("line_number_color", "CodeEdit", Palette.EdMuted.Darkened(0.2f));
        t.SetColor("selection_color", "CodeEdit", new Color(0.23f, 0.65f, 0.7f, 0.35f));

        // Граф: тёмная сетка как в Blueprint.
        t.SetStylebox("panel", "GraphEdit", UiKit.Box(Palette.EdBg));
        t.SetColor("grid_major", "GraphEdit", new Color(1, 1, 1, 0.07f));
        t.SetColor("grid_minor", "GraphEdit", new Color(1, 1, 1, 0.03f));
        t.SetColor("selection_fill", "GraphEdit", new Color(0.23f, 0.65f, 0.7f, 0.15f));
        t.SetColor("selection_stroke", "GraphEdit", Palette.EdAccent);
        t.SetStylebox("panel", "GraphNode", UiKit.Box(new Color(0.11f, 0.12f, 0.14f, 0.95f), new Color(0, 0, 0, 0.6f), 1, 6, 12, 8));
        t.SetStylebox("panel_selected", "GraphNode", UiKit.Box(new Color(0.11f, 0.12f, 0.14f, 0.95f), Palette.EdAccent, 2, 6, 12, 8));
        t.SetColor("font_color", "GraphNode", Palette.EdText);

        t.SetStylebox("scroll", "VScrollBar", UiKit.Box(Palette.EdBg));
        t.SetStylebox("grabber", "VScrollBar", UiKit.Box(Palette.EdPanelLight, default, 0, 4));
        t.SetStylebox("grabber_highlight", "VScrollBar", UiKit.Box(Palette.EdAccent, default, 0, 4));
        t.SetStylebox("scroll", "HScrollBar", UiKit.Box(Palette.EdBg));
        t.SetStylebox("grabber", "HScrollBar", UiKit.Box(Palette.EdPanelLight, default, 0, 4));
        t.SetStylebox("grabber_highlight", "HScrollBar", UiKit.Box(Palette.EdAccent, default, 0, 4));
        return t;
    }

    /// <summary>Подсветка синтаксиса .gics в текстовом редакторе.</summary>
    public static CodeHighlighter Highlighter()
    {
        var h = new CodeHighlighter
        {
            NumberColor = new Color("f0a35e"),
            SymbolColor = new Color("9aa4b1"),
            FunctionColor = new Color("7fd1d8"),
            MemberVariableColor = new Color("d7dae0"),
        };
        foreach (var kw in new[] { "label", "character", "var" }) h.AddKeywordColor(kw, new Color("ff7a90"));
        foreach (var kw in new[] { "if", "elif", "else", "menu", "jump", "call", "return", "end", "set", "and", "or", "not", "true", "false" })
        {
            h.AddKeywordColor(kw, new Color("c792ea"));
        }
        foreach (var kw in new[] { "say", "doc", "scene", "show", "hide", "viewer", "music", "ambient", "sound", "voice", "stop", "pause",
                     "glitch", "shake", "flash", "input", "os", "checkpoint", "notify" })
        {
            h.AddKeywordColor(kw, new Color("82aaff"));
        }
        h.AddColorRegion("\"", "\"", new Color("c3e88d"));
        h.AddColorRegion("# ", "", new Color("5f6b7a"), true);
        return h;
    }
}
