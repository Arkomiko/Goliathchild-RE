using GoliathChildRE.Ui;
using Godot;

namespace GoliathChildRE.Os;

/// <summary>
/// Оформление виртуальной ОС: близко к Windows XP (скруглённые заголовки с градиентом, бежевые диалоги),
/// но в бирюзово-чернильной гамме оригинала 2023 года — «немного по-другому» (ответ 41).
/// </summary>
public static class OsStyle
{
    public const int TitleHeight = 56;

    /// <summary>Горизонтальный градиент для заголовков, панели задач и кнопки «Пуск».</summary>
    public static StyleBoxTexture Gradient(Color a, Color b, int radiusTop = 0, bool vertical = true)
    {
        var g = new Gradient();
        g.SetColor(0, a);
        g.SetColor(1, b);
        var tex = new GradientTexture2D
        {
            Gradient = g,
            Width = 64,
            Height = 64,
            FillFrom = Vector2.Zero,
            FillTo = vertical ? new Vector2(0, 1) : new Vector2(1, 0),
        };
        return new StyleBoxTexture { Texture = tex };
    }

    /// <summary>Рамка окна: тёмно-бирюзовая, со скруглением сверху, как у окон XP.</summary>
    public static StyleBoxFlat Frame(bool active)
    {
        var box = UiKit.Box(active ? Palette.OsFrame : Palette.OsTitleInactiveA, default, 0, 0, 4, 4);
        box.CornerRadiusTopLeft = 12;
        box.CornerRadiusTopRight = 12;
        box.ShadowColor = new Color(0, 0, 0, 0.45f);
        box.ShadowSize = 18;
        box.ShadowOffset = new Vector2(6, 8);
        return box;
    }

    /// <summary>Полоса заголовка с вертикальным градиентом и бликом сверху.</summary>
    public static StyleBox TitleBar(bool active)
    {
        var g = new Gradient();
        var a = active ? Palette.OsTitleA : Palette.OsTitleInactiveA;
        var b = active ? Palette.OsTitleB : Palette.OsTitleInactiveB;
        g.SetColor(0, b.Lightened(0.25f));
        g.SetColor(1, a);
        g.AddPoint(0.15f, b);
        g.AddPoint(0.85f, a.Lightened(0.08f));
        var tex = new GradientTexture2D { Gradient = g, Width = 8, Height = 64, FillFrom = Vector2.Zero, FillTo = new Vector2(0, 1) };
        return new StyleBoxTexture { Texture = tex, ContentMarginLeft = 18, ContentMarginRight = 8 };
    }

    /// <summary>Тело диалога: бежевый «XP-фон».</summary>
    public static StyleBoxFlat Face(int pad = 28) => UiKit.Box(Palette.OsFace, Palette.OsFaceDark, 2, 0, pad, pad);

    /// <summary>Тело документа: белая бумага.</summary>
    public static StyleBoxFlat Paper(int pad = 26) => UiKit.Box(Palette.OsPaper, Palette.OsFaceDark, 2, 0, pad, pad);

    /// <summary>Кнопка закрытия окна: красный квадрат с крестиком.</summary>
    public static Button CloseButton()
    {
        var normal = UiKit.Box(new Color("b8323f"), new Color("ffffff"), 2, 6);
        var hover = UiKit.Box(new Color("e04454"), new Color("ffffff"), 2, 6);
        var b = UiKit.Button("×", Fonts.BodyBold, 38, Colors.White, Colors.White, normal, hover);
        b.CustomMinimumSize = new Vector2(44, 44);
        b.FocusMode = Control.FocusModeEnum.None;
        return b;
    }

    /// <summary>Обычная кнопка диалога XP: светлая, со скруглением и тёмной рамкой.</summary>
    public static Button DialogButton(string text)
    {
        var normal = UiKit.Box(new Color("f6f5ef"), new Color("1d4f61"), 2, 6, 34, 10);
        var hover = UiKit.Box(new Color("fffbe6"), new Color("e6a23c"), 3, 6, 34, 10);
        var b = UiKit.Button(text, Fonts.Body, 32, Palette.OsInk, Palette.OsInk, normal, hover);
        b.CustomMinimumSize = new Vector2(200, 0);
        return b;
    }

    /// <summary>Поле ввода в стиле XP с пиксельным шрифтом документа.</summary>
    public static LineEdit Field()
    {
        var e = new LineEdit();
        e.AddThemeFontOverride("font", Fonts.Pixel);
        e.AddThemeFontSizeOverride("font_size", 40);
        e.AddThemeColorOverride("font_color", Palette.OsInk);
        e.AddThemeColorOverride("caret_color", Palette.OsInk);
        e.AddThemeStyleboxOverride("normal", UiKit.Box(Colors.White, new Color("7f9db9"), 2, 0, 14, 10));
        e.AddThemeStyleboxOverride("focus", UiKit.Box(Colors.White, new Color("1f6f9c"), 3, 0, 14, 10));
        return e;
    }
}
