using System.Collections.Generic;
using Godot;

namespace GoliathChildRE.Ui;

/// <summary>
/// Шрифты игры. Все бесплатные (OFL / CC BY-SA), с кириллицей; лицензии лежат рядом с файлами в assets/fonts.
/// </summary>
public static class Fonts
{
    private static readonly Dictionary<string, Font> Cache = new();

    /// <summary>Заголовок GoliathChild:RE — «рваный» глитч-шрифт Rubik Glitch.</summary>
    public static Font Title => Get("res://assets/fonts/RubikGlitch/RubikGlitch-Regular.ttf");

    /// <summary>Пункты «киношного» меню — моноширинный JetBrains Mono, похож на терминал.</summary>
    public static Font Menu => Variable("res://assets/fonts/JetBrainsMono/JetBrainsMono-Variable.ttf", 500);

    public static Font MenuBold => Variable("res://assets/fonts/JetBrainsMono/JetBrainsMono-Variable.ttf", 800);

    /// <summary>Основной текст интерфейса — PT Sans (замена Tahoma из Windows XP).</summary>
    public static Font Body => Get("res://assets/fonts/PTSans/PTSans-Regular.ttf");

    public static Font BodyBold => Get("res://assets/fonts/PTSans/PTSans-Bold.ttf");

    /// <summary>Моноширинный для таблиц, путей и кода в редакторе.</summary>
    public static Font Mono => Get("res://assets/fonts/PTMono/PTMono-Regular.ttf");

    public static Font Code => Variable("res://assets/fonts/JetBrainsMono/JetBrainsMono-Variable.ttf", 400);

    /// <summary>Пиксельный шрифт документов оригинала (int10h AcPlus AST PremiumExec). Без сглаживания, чтобы пиксели были чёткими.</summary>
    public static Font Pixel
    {
        get
        {
            const string path = "res://assets/fonts/AcPlus/AcPlus_AST_PremiumExec.ttf";
            if (Cache.TryGetValue(path, out var f)) return f;
            var file = GD.Load<FontFile>(path);
            file.Antialiasing = TextServer.FontAntialiasing.None;
            file.Hinting = TextServer.Hinting.None;
            file.SubpixelPositioning = TextServer.SubpixelPositioning.Disabled;
            Cache[path] = file;
            return file;
        }
    }

    private static Font Get(string path)
    {
        if (Cache.TryGetValue(path, out var f)) return f;
        f = GD.Load<FontFile>(path);
        Cache[path] = f;
        return f;
    }

    /// <summary>Вариативный шрифт с заданной толщиной (100–900).</summary>
    private static Font Variable(string path, int weight)
    {
        var key = path + "#" + weight;
        if (Cache.TryGetValue(key, out var f)) return f;
        var variation = new FontVariation
        {
            BaseFont = GD.Load<FontFile>(path),
            VariationOpentype = new Godot.Collections.Dictionary { { "wght", weight } },
        };
        Cache[key] = variation;
        return variation;
    }
}
