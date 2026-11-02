using GicEngine.Localization;
using GoliathChildRE.Ui;
using Godot;

namespace GoliathChildRE.Menu;

/// <summary>
/// Подменю «История» (ответ 32): продолжить, новая игра, загрузить, выбор открытой главы.
/// </summary>
public partial class StoryPage : Control
{
    private readonly MainMenu _menu;

    public StoryPage(MainMenu menu)
    {
        _menu = menu;
    }

    public StoryPage() { }

    public override void _Ready()
    {
        var frame = PageFrame.Create(GicLocalization.T("menu.story"), out var content);
        AddChild(frame);

        var story = GameManager.Instance.MainStory;
        var latest = SaveManager.Instance.Latest(story.Meta.Id);

        // «Продолжить» — самое свежее сохранение любого типа (авто, быстрое или ручное).
        var cont = content.Add(UiKit.CinemaButton(GicLocalization.T("story.continue"), 44));
        cont.Disabled = latest == null;
        cont.Pressed += () => GameManager.Instance.ContinueLatest();
        if (latest != null)
        {
            var info = $"{latest.Title} · {latest.Created:dd.MM.yyyy HH:mm}";
            content.Add(UiKit.Label("    " + info, Fonts.Body, 28, Palette.TextMuted));
        }

        content.Add(UiKit.CinemaButton(GicLocalization.T("story.new"), 44)).OnPressed(() => GameManager.Instance.NewGame());
        content.Add(UiKit.CinemaButton(GicLocalization.T("story.load"), 44)).OnPressed(() => _menu?.OpenPage("saves"));

        content.Add(UiKit.Spacer(20));
        content.Add(PageFrame.Section(GicLocalization.T("story.chapters")));

        // Главы открываются контрольными точками по мере прохождения. Пролог открыт всегда.
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 20);
        grid.AddThemeConstantOverride("v_separation", 20);
        content.Add(grid);
        foreach (var chapter in story.Meta.Chapters)
        {
            var unlocked = chapter.Id == "prologue" || SaveManager.Instance.IsChapterUnlocked(chapter.Id);
            // Закрытая глава помечена крестиком: эмодзи замка есть не во всех шрифтах.
            var b = UiKit.CinemaButton(unlocked ? chapter.Title : "×  " + chapter.Title, 36);
            b.CustomMinimumSize = new Vector2(560, 0);
            b.Disabled = !unlocked;
            var label = chapter.Label;
            b.Pressed += () => GameManager.Instance.NewGame(label);
            grid.Add(b);
        }

        content.Add(UiKit.Expander());
        content.Add(UiKit.Paragraph(story.Meta.Description, Fonts.Body, 28, Palette.TextMuted));

        cont.CallDeferred(Control.MethodName.GrabFocus);
        if (latest == null) grid.GetChild<Button>(0).CallDeferred(Control.MethodName.GrabFocus);
    }
}
