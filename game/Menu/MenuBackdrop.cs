using System;
using GicEngine.IO;
using GoliathChildRE.Ui;
using Godot;

namespace GoliathChildRE.Menu;

/// <summary>
/// Фон главного меню, который меняется по мере прохождения сюжета — как в Portal 2.
/// Чем дальше игрок продвинулся, тем ближе фон к тому, что он уже видел в игре.
/// Прогресс берётся из постоянных данных SaveManager (открытые главы и флаг прохождения),
/// поэтому фон не зависит от того, какое сохранение загружено.
/// </summary>
public partial class MenuBackdrop : Control
{
    /// <summary>
    /// Папка с фонами: stage_0 … stage_5 (png, jpg или webp). Чтобы поменять фон ступени,
    /// достаточно заменить файл — код трогать не нужно. Подробности — в README.md этой папки.
    /// </summary>
    private const string Folder = "res://assets/menu/backgrounds/";
    private static readonly string[] Extensions = { "png", "jpg", "webp" };

    // Яркость картинки каждой ступени поверх тёмного фона: меню должно читаться на любом кадре.
    // Порядок ступеней — как в CurrentStage(); новые главы добавляются в конец перед финалом.
    private static readonly float[] Brightness =
    {
        0.40f, // 0. Ничего не начато: жёсткий диск из посылки.
        0.45f, // 1. Пролог: кто-то держит этот диск в руках.
        0.16f, // 2. Акт I: стена base64 со страницы «о сайте» presentdays.org.
        0.45f, // 3. Акт II: заплаканная Алиса.
        0.45f, // 4. Акт III: силуэт Ренатуса и «bellum omnium contra omnes».
        0.45f, // 5. Игра пройдена: сад из финала.
    };

    private TextureRect _ambient;
    private TextureRect _picture;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        var index = CurrentStage();
        var brightness = Brightness[index];
        var texture = LoadStage(index);
        var size = texture?.GetSize() ?? Vector2.One;
        bool wide = size.X / size.Y > 1.5f;

        // Нижний слой — та же картинка на весь экран, сильно затемнённая: задаёт цвет и настроение.
        // Для широких картинок (16:9) он и есть основной фон, поэтому темнится меньше.
        _ambient = UiKit.Full(new TextureRect
        {
            Texture = texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            Modulate = Dim(wide ? brightness : brightness * 0.25f),
            MouseFilter = MouseFilterEnum.Ignore,
        });
        AddChild(_ambient);

        // Квадратные и вертикальные иллюстрации целиком показываются справа, на всю высоту:
        // если растянуть их на весь экран, обрежется главное. Слева их перекрывает градиент меню.
        if (!wide)
        {
            _picture = new TextureRect
            {
                Texture = texture,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspect,
                Modulate = Dim(brightness),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _picture.AnchorLeft = 1;
            _picture.AnchorRight = 1;
            _picture.AnchorBottom = 1;
            _picture.OffsetLeft = -1440f * size.X / size.Y;
            AddChild(_picture);

            // Растушёвка левого края картинки: мягкая тёмная полоса (прозрачно → темно → прозрачно)
            // поверх стыка, чтобы ни картинка, ни сама полоса не давали резкой линии.
            var gradient = new Gradient();
            gradient.SetColor(0, new Color(0.02f, 0.03f, 0.04f, 0f));
            gradient.SetColor(1, new Color(0.02f, 0.03f, 0.04f, 0f));
            gradient.AddPoint(0.45f, new Color(0.02f, 0.03f, 0.04f, 1f));
            var fade = new TextureRect
            {
                Texture = new GradientTexture2D { Gradient = gradient, FillTo = new Vector2(1, 0), Width = 256, Height = 4 },
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            fade.AnchorLeft = 1;
            fade.AnchorRight = 1;
            fade.AnchorBottom = 1;
            fade.OffsetLeft = _picture.OffsetLeft - 420;
            fade.OffsetRight = _picture.OffsetLeft + 520;
            AddChild(fade);
        }

        // Медленное «дыхание» кадра, чтобы фон был живым.
        Resized += OnResized;
        OnResized();
        var zoom = CreateTween().SetLoops();
        zoom.TweenProperty(this, "scale", new Vector2(1.06f, 1.06f), 24).SetTrans(Tween.TransitionType.Sine);
        zoom.TweenProperty(this, "scale", Vector2.One, 24).SetTrans(Tween.TransitionType.Sine);
    }

    private void OnResized() => PivotOffset = Size / 2;

    /// <summary>
    /// Номер ступени по постоянному прогрессу. Для проверки можно задать вручную: «-- --menu-stage=N» (только в отладке).
    /// </summary>
    public static int CurrentStage()
    {
        if (OS.IsDebugBuild())
            foreach (var arg in OS.GetCmdlineUserArgs())
                if (arg.StartsWith("--menu-stage=") && int.TryParse(arg.Substring(13), out var forced))
                    return Math.Clamp(forced, 0, Brightness.Length - 1);

        var saves = SaveManager.Instance;
        if (saves.StoryCompleted) return 5;
        if (saves.IsChapterUnlocked("act3")) return 4;
        if (saves.IsChapterUnlocked("act2")) return 3;
        if (saves.IsChapterUnlocked("act1")) return 2;
        if (saves.IsChapterUnlocked("prologue")) return 1;
        return 0;
    }

    /// <summary>Картинка ступени из папки фонов; если файла нет — ступень пониже, чтобы меню не осталось пустым.</summary>
    private static Texture2D LoadStage(int index)
    {
        for (var i = index; i >= 0; i--)
            foreach (var ext in Extensions)
            {
                var path = $"{Folder}stage_{i}.{ext}";
                if (ResourceLoader.Exists(path)) return GicAssets.LoadTexture(path);
            }
        return null;
    }

    private static Color Dim(float v) => new(v, v * 1.05f, v * 1.08f);
}
