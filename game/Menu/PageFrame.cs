using GoliathChildRE.Ui;
using Godot;

namespace GoliathChildRE.Menu;

/// <summary>Общая рамка страниц меню: панель с заголовком и областью содержимого.</summary>
public static class PageFrame
{
    /// <summary>Создаёт панель с заголовком и возвращает контейнер для содержимого.</summary>
    public static PanelContainer Create(string title, out VBoxContainer content)
    {
        var panel = UiKit.Full(new PanelContainer());
        var root = UiKit.VBox(28);
        panel.AddChild(root);

        var header = UiKit.HBox(18);
        header.Add(UiKit.Label("//", Fonts.MenuBold, 44, Palette.Blood));
        header.Add(UiKit.Label(title.ToUpperInvariant(), Fonts.MenuBold, 44, Palette.Text));
        root.Add(header);
        root.Add(new ColorRect { Color = Palette.CinemaLine, CustomMinimumSize = new Vector2(0, 2) });

        content = UiKit.VBox(20);
        content.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        root.Add(content);
        return panel;
    }

    /// <summary>Подзаголовок раздела внутри страницы.</summary>
    public static Label Section(string text) => UiKit.Label(text.ToUpperInvariant(), Fonts.Menu, 28, Palette.TextMuted);
}
