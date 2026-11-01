using System;
using GicEngine.Localization;
using GoliathChildRE.Ui;
using Godot;

namespace GoliathChildRE.Menu;

/// <summary>
/// Настройки (ответ 37): экран и графика, звук, текст, доступность (светочувствительность, дальтонизм), язык, управление.
/// Каждое изменение применяется и сохраняется сразу — кнопки «Применить» нет.
/// </summary>
public partial class SettingsPage : Control
{
    private SettingsManager S => SettingsManager.Instance;
    private Label _deviceLabel;

    public override void _Ready()
    {
        var frame = PageFrame.Create(GicLocalization.T("menu.settings"), out var content);
        AddChild(frame);

        var tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        content.Add(tabs);
        tabs.Add(BuildDisplay());
        tabs.Add(BuildAudio());
        tabs.Add(BuildText());
        tabs.Add(BuildAccess());
        tabs.Add(BuildLanguage());
        tabs.Add(BuildControls());
    }

    // ---------------------------------------------------------------- вкладки

    private Control BuildDisplay()
    {
        var box = Tab(GicLocalization.T("settings.display"));

        var modes = new[] { GicLocalization.T("settings.windowed"), GicLocalization.T("settings.borderless"), GicLocalization.T("settings.fullscreen") };
        Row(box, GicLocalization.T("settings.window_mode"), Options(modes, (int)S.WindowMode, i =>
        {
            S.WindowMode = (SettingsManager.WindowModeKind)i;
            S.Commit(true);
        }));

        var resolutions = Array.ConvertAll(SettingsManager.Resolutions, r => $"{r.X} × {r.Y}");
        var current = Array.IndexOf(SettingsManager.Resolutions, S.Resolution);
        Row(box, GicLocalization.T("settings.resolution"), Options(resolutions, Math.Max(current, 0), i =>
        {
            S.Resolution = SettingsManager.Resolutions[i];
            S.Commit(true);
        }));

        Row(box, GicLocalization.T("settings.vsync"), Check(S.VSync, v =>
        {
            S.VSync = v;
            S.Commit(true);
        }));

        var fpsValues = new[] { 0, 30, 60, 120, 144, 240 };
        var fpsNames = Array.ConvertAll(fpsValues, f => f == 0 ? GicLocalization.T("settings.unlimited") : f.ToString());
        Row(box, GicLocalization.T("settings.max_fps"), Options(fpsNames, Math.Max(Array.IndexOf(fpsValues, S.MaxFps), 0), i =>
        {
            S.MaxFps = fpsValues[i];
            S.Commit(true);
        }));

        Row(box, GicLocalization.T("settings.screen_effects"), Check(S.ScreenEffects, v =>
        {
            S.ScreenEffects = v;
            S.Commit();
        }));
        return Wrap(box);
    }

    private Control BuildAudio()
    {
        var box = Tab(GicLocalization.T("settings.audio"));
        Row(box, GicLocalization.T("settings.master"), Slider(S.MasterVolume, v => { S.MasterVolume = v; S.Commit(); }));
        Row(box, GicLocalization.T("settings.music"), Slider(S.MusicVolume, v => { S.MusicVolume = v; S.Commit(); }));
        Row(box, GicLocalization.T("settings.sfx"), Slider(S.SfxVolume, v => { S.SfxVolume = v; S.Commit(); }));
        Row(box, GicLocalization.T("settings.voice"), Slider(S.VoiceVolume, v => { S.VoiceVolume = v; S.Commit(); }));
        box.Add(UiKit.Paragraph(GicLocalization.T("settings.voice_hint"), Fonts.Body, 26, Palette.TextMuted));
        return Wrap(box);
    }

    private Control BuildText()
    {
        var box = Tab(GicLocalization.T("settings.text"));

        // Пример текста показывает выбранную скорость вживую.
        var preview = new RichTextLabel
        {
            Text = GicLocalization.T("settings.text_preview"),
            FitContent = true,
            CustomMinimumSize = new Vector2(0, 90),
            VisibleCharactersBehavior = TextServer.VisibleCharactersBehavior.CharsAfterShaping,
        };
        preview.AddThemeFontOverride("normal_font", Fonts.Pixel);
        preview.AddThemeFontSizeOverride("normal_font_size", 32);
        preview.AddThemeColorOverride("default_color", Palette.Text);

        var speedLabel = UiKit.Label("", Fonts.Mono, 28, Palette.TextMuted);
        void UpdateSpeedLabel() => speedLabel.Text = S.TextSpeed >= SettingsManager.InstantTextSpeed
            ? GicLocalization.T("settings.instant")
            : $"{S.TextSpeed:0} {GicLocalization.T("settings.cps")}";
        UpdateSpeedLabel();

        var speed = Slider(S.TextSpeed / SettingsManager.InstantTextSpeed, v =>
        {
            S.TextSpeed = Mathf.Max(5, v * SettingsManager.InstantTextSpeed);
            UpdateSpeedLabel();
            preview.VisibleCharacters = 0;
            S.Commit();
        });
        var speedRow = UiKit.HBox(20);
        speed.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        speedRow.Add(speed);
        speedRow.Add(speedLabel);
        speedRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        Row(box, GicLocalization.T("settings.text_speed"), speedRow);
        box.Add(preview);

        // Печать примера в том же темпе, что и в игре.
        double shown = 0;
        var timer = new Timer { WaitTime = 1.0 / 60, Autostart = true };
        timer.Timeout += () =>
        {
            var total = preview.GetTotalCharacterCount();
            if (S.TextSpeed >= SettingsManager.InstantTextSpeed) shown = total;
            else shown = preview.VisibleCharacters >= total ? -S.TextSpeed : preview.VisibleCharacters + S.TextSpeed / 60.0;
            preview.VisibleCharacters = shown < 0 ? 0 : (int)Math.Min(shown, total);
            if (shown < 0) shown = 0;
        };
        box.Add(timer);

        Row(box, GicLocalization.T("settings.auto_delay"), Slider(S.AutoDelay / 5f, v => { S.AutoDelay = Mathf.Max(0.2f, v * 5f); S.Commit(); }));
        Row(box, GicLocalization.T("settings.skip_unseen"), Check(S.SkipUnseen, v => { S.SkipUnseen = v; S.Commit(); }));
        return Wrap(box);
    }

    private Control BuildAccess()
    {
        var box = Tab(GicLocalization.T("settings.access"));
        Row(box, GicLocalization.T("settings.photosensitive"), Check(S.Photosensitive, v => { S.Photosensitive = v; S.Commit(); }));
        box.Add(UiKit.Paragraph(GicLocalization.T("settings.photosensitive_hint"), Fonts.Body, 26, Palette.TextMuted));
        box.Add(UiKit.Spacer(16));

        var modes = new[]
        {
            GicLocalization.T("settings.cb_off"), GicLocalization.T("settings.cb_protanopia"),
            GicLocalization.T("settings.cb_deuteranopia"), GicLocalization.T("settings.cb_tritanopia"),
        };
        Row(box, GicLocalization.T("settings.colorblind"), Options(modes, (int)S.Colorblind, i =>
        {
            S.Colorblind = (SettingsManager.ColorblindKind)i;
            S.Commit();
        }));
        Row(box, GicLocalization.T("settings.colorblind_strength"), Slider(S.ColorblindStrength, v => { S.ColorblindStrength = v; S.Commit(); }));

        // Полоса цветов, чтобы сразу увидеть действие фильтра.
        var swatches = UiKit.HBox(8);
        foreach (var c in new[] { "e0304e", "e8862b", "e6d23a", "4caf50", "2c99a3", "3b6fd6", "8e44ad" })
        {
            swatches.Add(new ColorRect { Color = new Color(c), CustomMinimumSize = new Vector2(120, 60) });
        }
        box.Add(swatches);
        return Wrap(box);
    }

    private Control BuildLanguage()
    {
        var box = Tab(GicLocalization.T("settings.language"));
        var languages = GicLocalization.Instance.Languages;
        var names = languages.ConvertAll(l => l.Name).ToArray();
        var current = Math.Max(languages.FindIndex(l => l.Locale == S.Language), 0);
        Row(box, GicLocalization.T("settings.language"), Options(names, current, i =>
        {
            S.Language = languages[i].Locale;
            S.Commit();
            // Надписи меню собраны уже переведёнными — пересоздаём меню на новом языке.
            if (GameManager.Instance.Session == null) GameManager.Instance.ShowMainMenu("settings");
        }));
        box.Add(UiKit.Paragraph(GicLocalization.T("settings.language_hint"), Fonts.Body, 26, Palette.TextMuted));
        return Wrap(box);
    }

    private Control BuildControls()
    {
        var box = Tab(GicLocalization.T("settings.controls"));
        _deviceLabel = UiKit.Label("", Fonts.BodyBold, 32, Palette.TealBright);
        box.Add(_deviceLabel);
        UpdateDevice();
        InputDeviceManager.Instance.DeviceChanged += OnDeviceChanged;

        // Таблица клавиш. Переназначение появится позже вместе с поддержкой консолей.
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 40);
        grid.AddThemeConstantOverride("v_separation", 12);
        foreach (var (key, kb, pad) in new[]
                 {
                     ("controls.advance", "ЛКМ / Enter / Space", "A"),
                     ("controls.rollback", "Колесо вверх / PageUp", "LB"),
                     ("controls.skip", "Ctrl (удерживать)", "RB"),
                     ("controls.auto", "A", "Y"),
                     ("controls.backlog", "H", "X"),
                     ("controls.menu", "Esc", "Start"),
                     ("controls.quicksave", "F5", "—"),
                     ("controls.quickload", "F9", "—"),
                 })
        {
            grid.Add(UiKit.Label(GicLocalization.T(key), Fonts.Body, 28, Palette.Text));
            grid.Add(UiKit.Label(kb, Fonts.Mono, 26, Palette.TextMuted));
            grid.Add(UiKit.Label(pad, Fonts.Mono, 26, Palette.TextMuted));
        }
        box.Add(grid);
        return Wrap(box);
    }

    private void OnDeviceChanged(int kind) => UpdateDevice();

    private void UpdateDevice()
    {
        if (!IsInstanceValid(_deviceLabel)) return;
        var d = InputDeviceManager.Instance;
        var kind = GicLocalization.T("device." + d.Current.ToString().ToLowerInvariant());
        _deviceLabel.Text = GicLocalization.T("settings.device") + ": " + kind + (d.CurrentPadName != "" ? $" ({d.CurrentPadName})" : "");
    }

    // ---------------------------------------------------------------- элементы

    private static VBoxContainer Tab(string title)
    {
        var box = UiKit.VBox(22);
        box.Name = title;
        box.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return box;
    }

    /// <summary>Оборачивает вкладку в прокрутку, чтобы длинные списки помещались на маленьких экранах.</summary>
    private static Control Wrap(VBoxContainer box)
    {
        var scroll = new ScrollContainer { Name = box.Name, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        box.Name = "Content";
        scroll.Add(box);
        return scroll;
    }

    /// <summary>Строка «подпись — элемент управления».</summary>
    private static void Row(VBoxContainer box, string label, Control control)
    {
        var row = UiKit.HBox(30);
        var l = UiKit.Label(label, Fonts.Body, 32, Palette.Text);
        l.CustomMinimumSize = new Vector2(440, 0);
        row.Add(l);
        control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.Add(control);
        box.Add(row);
    }

    private static OptionButton Options(string[] items, int selected, Action<int> changed)
    {
        var o = new OptionButton();
        o.AddThemeFontSizeOverride("font_size", 30);
        foreach (var item in items) o.AddItem(item);
        o.Selected = Math.Clamp(selected, 0, items.Length - 1);
        o.ItemSelected += i => changed((int)i);
        return o;
    }

    private static CheckBox Check(bool value, Action<bool> changed)
    {
        var c = new CheckBox { ButtonPressed = value, Text = "" };
        c.AddThemeConstantOverride("icon_max_width", 48);
        c.Toggled += v => changed(v);
        return c;
    }

    private static HSlider Slider(float value, Action<float> changed)
    {
        var s = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.01, Value = value, CustomMinimumSize = new Vector2(0, 48) };
        s.ValueChanged += v => changed((float)v);
        return s;
    }
}
