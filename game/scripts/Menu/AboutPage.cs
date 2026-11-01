using GicEngine.Localization;
using GoliathChildRE.Ui;
using Godot;

namespace GoliathChildRE.Menu;

/// <summary>Об игре и авторы (ответ 38): ремейк, оригинал 2023 года, движок и лицензии сторонних ресурсов.</summary>
public partial class AboutPage : Control
{
    public override void _Ready()
    {
        var frame = PageFrame.Create(GicLocalization.T("menu.about"), out var content);
        AddChild(frame);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        content.Add(scroll);
        var box = UiKit.VBox(14);
        box.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.Add(box);

        box.Add(UiKit.Label("GoliathChild:RE", Fonts.Title, 72, Palette.Text));
        box.Add(UiKit.Label("v" + ProjectSettings.GetSetting("application/config/version").AsString(), Fonts.Mono, 28, Palette.TextMuted));
        box.Add(UiKit.Paragraph(GicLocalization.T("about.text"), Fonts.Body, 30, Palette.Text));

        Section(box, GicLocalization.T("about.remake"));
        Line(box, "sola creators", GicLocalization.T("about.studio"));
        Line(box, "handakote", GicLocalization.T("about.handa_remake"));
        Line(box, "arkomiko", GicLocalization.T("about.arkomiko_remake"));

        // Состав оригинала — как в титрах прототипа 2023 года.
        Section(box, GicLocalization.T("about.original"));
        Line(box, "handakote", "главный разработчик, режиссёр, художник, кодер");
        Line(box, "YtrogGrenada", "главный разработчик, сценарист, художник");
        Line(box, "Я.Д.Белковый", "программист-конструктор");
        Line(box, "Arkomiko", "музыкант");
        Line(box, "Tov. Hrom", "программист");
        Line(box, "Ed Dumbhead", "звукарь");
        Line(box, "Comandante Cartucho", "соавтор страницы itch.io");

        Section(box, GicLocalization.T("about.thirdparty"));
        box.Add(UiKit.Paragraph(GicLocalization.T("about.licenses"), Fonts.Body, 26, Palette.TextMuted));

        var links = UiKit.HBox(20);
        links.Add(UiKit.CinemaButton(GicLocalization.T("about.itch"), 30)).OnPressed(() => OS.ShellOpen("https://handakote.itch.io/goliathchild"));
        links.Add(UiKit.CinemaButton("Godot Engine", 30)).OnPressed(() => OS.ShellOpen("https://godotengine.org/license"));
        box.Add(UiKit.Spacer(10));
        box.Add(links);
    }

    private static void Section(VBoxContainer box, string title)
    {
        box.Add(UiKit.Spacer(18));
        box.Add(PageFrame.Section(title));
    }

    private static void Line(VBoxContainer box, string who, string role)
    {
        var row = UiKit.HBox(24);
        var name = UiKit.Label(who, Fonts.BodyBold, 30, Palette.TealBright);
        name.CustomMinimumSize = new Vector2(380, 0);
        row.Add(name);
        row.Add(UiKit.Paragraph(role, Fonts.Body, 30, Palette.Text));
        box.Add(row);
    }
}
