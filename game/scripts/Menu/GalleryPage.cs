using System.Collections.Generic;
using GicEngine.IO;
using GicEngine.Localization;
using GoliathChildRE.Ui;
using Godot;

namespace GoliathChildRE.Menu;

/// <summary>
/// Галерея (ответ 34): иллюстрации, фоны и спрайты открываются, когда игрок увидел их в истории.
/// Вкладка «Прототип 2023» — бонус (ответ 91): страницы из архивов ACT1–3.7z и скриншоты оригинала.
/// </summary>
public partial class GalleryPage : Control
{
    // Пути записаны так же, как в сценарии: по ним SaveManager помнит, что картинка уже показана.
    private const string OldImages = "old2023/images/";

    public override void _Ready()
    {
        var frame = PageFrame.Create(GicLocalization.T("menu.gallery"), out var content);
        AddChild(frame);

        var tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        content.Add(tabs);

        tabs.Add(BuildGrid(GicLocalization.T("gallery.illustrations"), ListPictures()));
        tabs.Add(BuildGrid(GicLocalization.T("gallery.backgrounds"), new List<(string, bool)>
        {
            (OldImages + "bg desktop.png", IsSeen(OldImages + "bg desktop.png")),
            (OldImages + "bios1.png", IsSeen(OldImages + "bios1.png")),
            (OldImages + "bios2.png", IsSeen(OldImages + "bios2.png")),
            (OldImages + "renatus.png", IsSeen(OldImages + "renatus.png")),
            (OldImages + "credit.png", IsSeen(OldImages + "credit.png")),
        }));

        // Спрайтов в прототипе нет (кроме глаз Ренатуса) — они появятся в ремейке (ответ 44).
        var sprites = UiKit.VBox(20);
        sprites.Name = GicLocalization.T("gallery.sprites");
        sprites.Add(UiKit.Paragraph(GicLocalization.T("gallery.sprites_soon"), Fonts.Body, 32, Palette.TextMuted));
        tabs.Add(sprites);

        tabs.Add(BuildGrid(GicLocalization.T("gallery.prototype"), ListBonus()));
    }

    /// <summary>Иллюстрации из окна просмотра оригинала: всё содержимое images/pic и отдельные картинки-документы.</summary>
    private static List<(string, bool)> ListPictures()
    {
        var result = new List<(string, bool)>();
        var names = new List<string>(ResourceLoader.ListDirectory("res://assets/old2023/images/pic"));
        names.Sort(System.StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (!name.EndsWith(".png")) continue;
            var path = OldImages + "pic/" + name;
            result.Add((path, IsSeen(path)));
        }
        foreach (var extra in new[] { "about_site.png", "description.png", "LIKE.png", "password_ACT_1.png", "password_ACT_2.png", "password_ACT_3.png" })
        {
            result.Add((OldImages + extra, IsSeen(OldImages + extra)));
        }
        return result;
    }

    /// <summary>
    /// Бонус: страницы из архивов открываются вместе с соответствующим актом, скриншоты оригинала — сразу.
    /// </summary>
    private static List<(string, bool)> ListBonus()
    {
        var result = new List<(string, bool)>();
        var saves = SaveManager.Instance;
        foreach (var (file, chapter) in new[]
                 {
                     ("1_1.png", "act1"), ("1_2.png", "act1"),
                     ("2_1.png", "act2"), ("2_2.png", "act2"), ("2_3.png", "act2"), ("2_4.png", "act2"),
                     ("3_1.png", "act3"), ("3_2.png", "act3"),
                 })
        {
            result.Add(("bonus/act_archives/" + file, saves.IsChapterUnlocked(chapter)));
        }
        for (var i = 1; i <= 5; i++) result.Add(($"bonus/screenshots_2023/screenshot_{i}.jpg", true));
        return result;
    }

    private static bool IsSeen(string path) => SaveManager.Instance.IsImageSeen(path);

    /// <summary>Сетка миниатюр. Закрытые картинки показываются тёмной плашкой со знаком вопроса.</summary>
    private Control BuildGrid(string title, List<(string Path, bool Unlocked)> items)
    {
        var scroll = new ScrollContainer { Name = title, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var grid = new GridContainer { Columns = 4, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 18);
        grid.AddThemeConstantOverride("v_separation", 18);
        scroll.Add(grid);

        foreach (var (path, unlocked) in items)
        {
            var cell = new Button { CustomMinimumSize = new Vector2(270, 270), FocusMode = FocusModeEnum.All };
            cell.AddThemeStyleboxOverride("normal", UiKit.Box(new Color(1, 1, 1, 0.04f), Palette.CinemaLine, 2, 6));
            cell.AddThemeStyleboxOverride("hover", UiKit.Box(new Color(0.17f, 0.6f, 0.64f, 0.15f), Palette.TealBright, 2, 6));
            cell.AddThemeStyleboxOverride("focus", UiKit.Box(new Color(0.17f, 0.6f, 0.64f, 0.15f), Palette.TealBright, 2, 6));
            cell.AddThemeStyleboxOverride("pressed", UiKit.Box(new Color(0.17f, 0.6f, 0.64f, 0.25f), Palette.TealBright, 2, 6));
            grid.Add(cell);

            if (!unlocked)
            {
                cell.Disabled = true;
                cell.Add(UiKit.Full(UiKit.Label("?", Fonts.MenuBold, 90, Palette.TextDim, HorizontalAlignment.Center))).VerticalAlignment = VerticalAlignment.Center;
                continue;
            }
            var resolved = "res://assets/" + path;
            var thumb = UiKit.Full(new TextureRect
            {
                Texture = GicAssets.LoadTexture(resolved),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
            });
            thumb.OffsetLeft = thumb.OffsetTop = 10;
            thumb.OffsetRight = thumb.OffsetBottom = -10;
            cell.Add(thumb);
            cell.Pressed += () => ImageOverlay.Show(GetTree().Root, resolved);
        }
        return scroll;
    }
}
