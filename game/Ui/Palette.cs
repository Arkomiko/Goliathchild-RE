using Godot;

namespace GoliathChildRE.Ui;

/// <summary>
/// Цвета игры в одном месте. Бирюза — цвет рабочего стола оригинала 2023 года, красный — акцент хоррора.
/// </summary>
public static class Palette
{
    // ---- «киношное» меню
    public static readonly Color CinemaBg = new("07090c");
    public static readonly Color CinemaPanel = new(0.04f, 0.055f, 0.07f, 0.86f);
    public static readonly Color CinemaLine = new(0.17f, 0.6f, 0.64f, 0.35f);
    public static readonly Color Text = new("e6e8ea");
    public static readonly Color TextMuted = new("8a949c");
    public static readonly Color TextDim = new("4d565d");
    public static readonly Color Teal = new("2c99a3");
    public static readonly Color TealBright = new("5fd4dd");
    public static readonly Color Blood = new("e0304e");

    // ---- виртуальная ОС (близко к Windows XP, но в бирюзово-чернильной гамме)
    public static readonly Color OsDesktop = new("2c99a3");
    public static readonly Color OsTitleA = new("0b3d52");
    public static readonly Color OsTitleB = new("1f7f8c");
    public static readonly Color OsTitleInactiveA = new("5b6e75");
    public static readonly Color OsTitleInactiveB = new("8fa3a8");
    public static readonly Color OsFrame = new("0d4a5e");
    public static readonly Color OsFace = new("ece9d8");
    public static readonly Color OsFaceDark = new("c9c4ad");
    public static readonly Color OsPaper = new("ffffff");
    public static readonly Color OsInk = new("101010");
    public static readonly Color OsTaskbarA = new("0a3442");
    public static readonly Color OsTaskbarB = new("17606f");
    public static readonly Color OsStartA = new("8c1c2e");
    public static readonly Color OsStartB = new("c8344a");
    public static readonly Color OsSelect = new("1f6f9c");

    // ---- редактор GiC_OS (тёмный, как Blueprint)
    public static readonly Color EdBg = new("16181d");
    public static readonly Color EdPanel = new("1f2228");
    public static readonly Color EdPanelLight = new("2a2e36");
    public static readonly Color EdLine = new("33373f");
    public static readonly Color EdText = new("d7dae0");
    public static readonly Color EdMuted = new("8b919c");
    public static readonly Color EdAccent = new("3aa7b2");
    public static readonly Color EdGrid = new("23262c");
}
