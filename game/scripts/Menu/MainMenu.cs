using System;
using System.Collections.Generic;
using GicEngine.IO;
using GicEngine.Localization;
using GoliathChildRE.Ui;
using Godot;

namespace GoliathChildRE.Menu;

/// <summary>
/// Главное меню GoliathChild:RE в «киношном» стиле (ответ 31).
/// Слева — название и пункты: История, Сохранения, Галерея, (отступ) Своя история в GiC_OS, (отступ) Настройки, Об игре, Выход.
/// Справа открываются страницы пунктов, меню при этом остаётся на месте.
/// </summary>
public partial class MainMenu : Control
{
    private readonly string _startPage;
    private readonly Dictionary<string, Button> _items = new();
    private Control _pageHost;
    private Control _page;
    private string _pageId;
    private ColorRect _crt;
    private ColorRect _glitch;
    private ShaderMaterial _glitchMaterial;
    private AudioStreamPlayer _music;
    private double _nextGlitch = 4;
    private double _glitchLeft;

    public MainMenu(string startPage = null)
    {
        _startPage = startPage;
    }

    // Godot создаёт узлы и без аргументов (например, при перезагрузке сборки) — нужен пустой конструктор.
    public MainMenu() { }

    public override void _Ready()
    {
        Theme = UiKit.CinemaTheme();
        BuildBackground();
        BuildMenu();
        BuildMusic();

        if (_startPage != null) OpenPage(_startPage);
        else _items["story"].GrabFocus();
    }

    // ---------------------------------------------------------------- фон

    private void BuildBackground()
    {
        AddChild(UiKit.Full(new ColorRect { Color = Palette.CinemaBg }));

        // Фон зависит от прогресса сюжета (как в Portal 2) — см. MenuBackdrop.
        AddChild(UiKit.Full(new MenuBackdrop()));

        // Градиент слева, чтобы пункты меню читались поверх картинки.
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(0.02f, 0.03f, 0.04f, 0.96f));
        gradient.SetColor(1, new Color(0.02f, 0.03f, 0.04f, 0f));
        gradient.SetOffset(1, 1f);
        var shade = new TextureRect
        {
            Texture = new GradientTexture2D { Gradient = gradient, FillFrom = new Vector2(0.2f, 0), FillTo = new Vector2(1, 0), Width = 256, Height = 4 },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        // Полоса шириной 1500 пикселей на всю высоту окна.
        shade.AnchorBottom = 1;
        shade.OffsetRight = 1500;
        AddChild(shade);

        // Шум плёнки, сканлайны и виньетка (выключаются в настройках графики).
        _crt = UiKit.Full(new ColorRect
        {
            Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://addons/gic_engine/Effects/crt.gdshader") },
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = SettingsManager.Instance.ScreenEffects,
        });
        AddChild(_crt);
        SettingsManager.Instance.Changed += OnSettingsChanged;
    }

    private void OnSettingsChanged() => _crt.Visible = SettingsManager.Instance.ScreenEffects;

    // ---------------------------------------------------------------- меню

    private void BuildMenu()
    {
        var column = UiKit.VBox(0);
        column.Position = new Vector2(180, 150);
        column.Size = new Vector2(900, 1180);
        AddChild(column);

        // Название: GoliathChild + красный суффикс :RE.
        var title = UiKit.HBox(0);
        title.Add(UiKit.Label("GoliathChild", Fonts.Title, 150, Palette.Text));
        title.Add(UiKit.Label(":RE", Fonts.Title, 150, Palette.Blood));
        column.Add(title);
        column.Add(UiKit.Label(GicLocalization.T("menu.subtitle"), Fonts.Menu, 30, Palette.TextMuted));
        column.Add(UiKit.Spacer(110));

        // Пункты меню. Отступы между группами — визуальная группировка (ответ 36).
        AddItem(column, "story", "menu.story", () => OpenPage("story"));
        AddItem(column, "saves", "menu.saves", () => OpenPage("saves"));
        AddItem(column, "gallery", "menu.gallery", () => OpenPage("gallery"));
        column.Add(UiKit.Spacer(48));
        AddItem(column, "editor", "menu.editor", () => GameManager.Instance.OpenEditor(), Palette.TealBright);
        column.Add(UiKit.Spacer(48));
        AddItem(column, "settings", "menu.settings", () => OpenPage("settings"));
        AddItem(column, "about", "menu.about", () => OpenPage("about"));
        AddItem(column, "quit", "menu.quit", () => GameManager.Instance.QuitGame());

        // Версия в углу — по формату 0.этап.коммит.ММДДбуква (ответ 97).
        var version = UiKit.Label("v" + ProjectSettings.GetSetting("application/config/version").AsString(), Fonts.Menu, 24, Palette.TextDim);
        version.Position = new Vector2(180, 1370);
        AddChild(version);

        // Место для страниц справа.
        // Под названием, чтобы панель не закрывала «GoliathChild:RE».
        _pageHost = new Control { Position = new Vector2(1120, 390), Size = new Vector2(1300, 960) };
        AddChild(_pageHost);
    }

    private void AddItem(VBoxContainer column, string id, string key, Action action, Color? color = null)
    {
        var normal = UiKit.Empty(0, 10);
        var b = UiKit.Button(GicLocalization.T(key), Fonts.Menu, 54, color ?? Palette.Text, Palette.TealBright, normal, normal);
        b.Alignment = HorizontalAlignment.Left;
        b.Pressed += action;
        // Маркер ▸ появляется у выделенного пункта — так видно, где фокус с клавиатуры или геймпада.
        var text = b.Text;
        b.MouseEntered += () => b.GrabFocus();
        b.FocusEntered += () => b.Text = "▸ " + text;
        b.FocusExited += () => b.Text = _pageId == id ? "▸ " + text : text;
        _items[id] = b;
        column.Add(b);
    }

    // ---------------------------------------------------------------- страницы

    /// <summary>Открыть страницу справа. Повторное нажатие того же пункта закрывает её.</summary>
    public void OpenPage(string id)
    {
        if (_pageId == id)
        {
            ClosePage();
            return;
        }
        _page?.QueueFree();
        _pageId = id;
        _page = id switch
        {
            "story" => new StoryPage(this),
            "saves" => new SavesPage(null),
            "gallery" => new GalleryPage(),
            "settings" => new SettingsPage(),
            "about" => new AboutPage(),
            _ => null,
        };
        if (_page == null) return;
        _pageHost.AddChild(UiKit.Full(_page));

        // Мягкое появление страницы.
        _page.Modulate = new Color(1, 1, 1, 0);
        _page.Position = new Vector2(40, 0);
        var t = CreateTween().SetParallel();
        t.TweenProperty(_page, "modulate:a", 1f, 0.25f);
        t.TweenProperty(_page, "position:x", 0f, 0.25f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);

        foreach (var kv in _items) kv.Value.AddThemeColorOverride("font_color", kv.Key == id ? Palette.Blood : (kv.Key == "editor" ? Palette.TealBright : Palette.Text));
    }

    public void ClosePage()
    {
        _page?.QueueFree();
        _page = null;
        var last = _pageId;
        _pageId = null;
        foreach (var kv in _items) kv.Value.AddThemeColorOverride("font_color", kv.Key == "editor" ? Palette.TealBright : Palette.Text);
        if (last != null && _items.TryGetValue(last, out var b)) b.GrabFocus();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("ui_cancel") && _page != null)
        {
            ClosePage();
            GetViewport().SetInputAsHandled();
        }
    }

    // ---------------------------------------------------------------- музыка и глитч

    private void BuildMusic()
    {
        _music = new AudioStreamPlayer
        {
            Stream = GicAssets.LoadAudio("res://assets/old2023/audio/OST/Ninshiki.ogg", true),
            Bus = "Music",
            VolumeDb = -6,
        };
        AddChild(_music);
        _music.Play();

        _glitchMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://addons/gic_engine/Effects/glitch.gdshader") };
        _glitch = UiKit.Full(new ColorRect { Material = _glitchMaterial, MouseFilter = MouseFilterEnum.Ignore, Visible = false });
        AddChild(_glitch);
    }

    /// <summary>Редкий короткий глитч раз в несколько секунд. В режиме светочувствительности выключен.</summary>
    public override void _Process(double delta)
    {
        if (SettingsManager.Instance.Photosensitive || !SettingsManager.Instance.ScreenEffects)
        {
            _glitch.Visible = false;
            return;
        }
        if (_glitchLeft > 0)
        {
            _glitchLeft -= delta;
            _glitchMaterial.SetShaderParameter("seed", GD.Randf() * 100f);
            if (_glitchLeft <= 0) _glitch.Visible = false;
            return;
        }
        _nextGlitch -= delta;
        if (_nextGlitch <= 0)
        {
            _glitch.Visible = true;
            _glitchMaterial.SetShaderParameter("strength", 0.25f);
            _glitchLeft = 0.12 + GD.Randf() * 0.1;
            _nextGlitch = 6 + GD.Randf() * 8;
        }
    }
}
