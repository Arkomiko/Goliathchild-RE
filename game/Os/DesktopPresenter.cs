using System;
using System.Collections.Generic;
using GicEngine.IO;
using GicEngine.Localization;
using GicEngine.Runtime;
using GicEngine.Script;
using GoliathChildRE.Ui;
using Godot;

namespace GoliathChildRE.Os;

/// <summary>
/// Презентер «рабочий стол виртуальной ОС» — подача основного сюжета Goliathchild.
/// Слои снизу вверх: фон (сцена), рабочий стол (иконки и панель задач), картинки show (BIOS, титры),
/// окна (документ, просмотр, папка, ошибки, диалоги), эффекты (глитч, вспышка), уведомления.
/// Используется и в игре, и в предпросмотре редактора GiC_OS.
/// </summary>
public partial class DesktopPresenter : Control, IStoryPresenter
{
    // Ассеты прототипа нарисованы под 1280×720, игра — 2560×1440: увеличиваем их ровно в 2 раза.
    private const float OldScale = 2f;
    private const int TaskbarHeight = 72;

    /// <summary>История нужна, чтобы превращать пути из сценария в настоящие пути к файлам.</summary>
    public StoryProject Project;

    /// <summary>Подстановка переменных в реплики (даёт исполнитель).</summary>
    public Func<string, string> Lookup;

    /// <summary>Включён ли сейчас пропуск (Ctrl или кнопка на панели задач).</summary>
    public Func<bool> SkipActive = () => false;

    /// <summary>Включено ли авточтение.</summary>
    public Func<bool> AutoActive = () => false;

    public event Action StartPressed;
    public event Action AutoPressed;
    public event Action SkipPressed;
    public event Action BacklogPressed;
    public event Action<string> WindowClosedByPlayer;
    public event Action<string, string> CheckpointReached;
    public event Action<string, int, string> ErrorReported;
    public event Action Ended;

    // ---- слои
    private Control _shake;
    private Control _sceneLayer;
    private ColorRect _sceneColor;
    private TextureRect _sceneImage;
    private Control _desktopLayer;
    private VBoxContainer _leftIcons;
    private VBoxContainer _rightIcons;
    private Control _taskbar;
    private Label _clock;
    private Button _autoButton;
    private Button _skipButton;
    private Control _imageLayer;
    private readonly Dictionary<string, TextureRect> _images = new();
    private Control _windowLayer;
    private ColorRect _glitch;
    private ShaderMaterial _glitchMaterial;
    private ColorRect _flash;
    private VBoxContainer _notifyBox;

    // ---- окна
    private OsWindow _doc;
    private Typewriter _typer;
    private OsWindow _viewer;
    private TextureRect _viewerImage;
    private Action _viewerClosed;
    private OsWindow _folder;
    private VBoxContainer _folderList;
    private Label _folderPath;
    private Control _modal;

    // ---- звук
    private readonly Dictionary<string, AudioStreamPlayer> _channels = new();
    private readonly Dictionary<string, string> _playing = new();
    private AudioStreamPlayer _voice;

    // ---- ожидания
    private Action _sayDone;
    private SayRequest _say;
    private MarkupResult _markup;
    private bool _sayShown;
    private double _autoLeft;
    private Action _waitDone;
    private double _waitLeft;
    private Action<string> _desktopAction;
    private DesktopState _desktop = new();
    private double _effectLeft;
    private double _effectTick;
    private Action _effectDone;
    private string _effectKind;
    private float _effectPower;
    private int _token;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;

        _shake = UiKit.Full(new Control { MouseFilter = MouseFilterEnum.Ignore });
        AddChild(_shake);

        BuildScene();
        BuildDesktop();
        _imageLayer = _shake.Add(UiKit.Full(new Control { MouseFilter = MouseFilterEnum.Ignore }));
        _windowLayer = _shake.Add(UiKit.Full(new Control { MouseFilter = MouseFilterEnum.Ignore }));
        BuildDocument();
        BuildViewer();
        BuildFolder();
        BuildEffects();
        BuildAudio();
    }

    // =================================================================== построение

    private void BuildScene()
    {
        _sceneLayer = _shake.Add(UiKit.Full(new Control { MouseFilter = MouseFilterEnum.Ignore }));
        _sceneColor = _sceneLayer.Add(UiKit.Full(new ColorRect { Color = Colors.Black, MouseFilter = MouseFilterEnum.Ignore }));
        _sceneImage = _sceneLayer.Add(UiKit.Full(new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = MouseFilterEnum.Ignore,
        }));
    }

    private void BuildDesktop()
    {
        _desktopLayer = _shake.Add(UiKit.Full(new Control { MouseFilter = MouseFilterEnum.Ignore, Visible = false }));

        // Колонки иконок: слева система, справа архивы — как в оригинале.
        _leftIcons = UiKit.VBox(18);
        _leftIcons.Position = new Vector2(16, 16);
        _desktopLayer.AddChild(_leftIcons);
        _rightIcons = UiKit.VBox(18);
        _rightIcons.AnchorLeft = _rightIcons.AnchorRight = 1;
        _rightIcons.OffsetLeft = -200;
        _rightIcons.OffsetTop = 16;
        _rightIcons.GrowHorizontal = GrowDirection.Begin;
        _desktopLayer.AddChild(_rightIcons);

        // Панель задач XP-стиля в бирюзовой гамме.
        var bar = UiKit.Panel(OsStyle.Gradient(Palette.OsTaskbarB, Palette.OsTaskbarA));
        bar.AnchorTop = bar.AnchorBottom = 1;
        bar.AnchorRight = 1;
        bar.OffsetTop = -TaskbarHeight;
        bar.MouseFilter = MouseFilterEnum.Stop;
        _desktopLayer.AddChild(bar);
        _taskbar = bar;

        var row = UiKit.HBox(12);
        bar.AddChild(row);

        var startNormal = OsStyle.Gradient(Palette.OsStartB, Palette.OsStartA, 0, true);
        startNormal.ContentMarginLeft = 26;
        startNormal.ContentMarginRight = 36;
        var startHover = OsStyle.Gradient(Palette.OsStartB.Lightened(0.15f), Palette.OsStartA.Lightened(0.1f), 0, true);
        startHover.ContentMarginLeft = 26;
        startHover.ContentMarginRight = 36;
        var start = UiKit.Button("◉  START", Fonts.BodyBold, 34, Colors.White, Colors.White, startNormal, startHover);
        start.FocusMode = FocusModeEnum.None;
        start.Pressed += () => StartPressed?.Invoke();
        row.Add(start);
        row.Add(UiKit.Expander());

        // Трей: авточтение, пропуск, журнал, язык, часы.
        _autoButton = TrayButton(GicLocalization.T("tray.auto"), () => AutoPressed?.Invoke());
        _skipButton = TrayButton(GicLocalization.T("tray.skip"), () => SkipPressed?.Invoke());
        row.Add(_autoButton);
        row.Add(_skipButton);
        row.Add(TrayButton(GicLocalization.T("tray.log"), () => BacklogPressed?.Invoke()));

        var tray = UiKit.Panel(UiKit.Box(new Color(0, 0, 0, 0.25f), new Color(1, 1, 1, 0.15f), 1, 0, 22, 0));
        row.Add(tray);
        var trayRow = UiKit.HBox(22);
        tray.AddChild(trayRow);
        var lang = UiKit.Label(SettingsManager.Instance.Language.ToUpperInvariant(), Fonts.BodyBold, 28, Colors.White);
        lang.VerticalAlignment = VerticalAlignment.Center;
        trayRow.Add(lang);
        _clock = UiKit.Label("--:--", Fonts.Body, 30, Colors.White);
        _clock.VerticalAlignment = VerticalAlignment.Center;
        _clock.SizeFlagsVertical = SizeFlags.ExpandFill;
        trayRow.Add(_clock);
    }

    private static Button TrayButton(string text, Action action)
    {
        var normal = UiKit.Box(new Color(1, 1, 1, 0.06f), new Color(1, 1, 1, 0.18f), 1, 4, 18, 6);
        var hover = UiKit.Box(new Color(1, 1, 1, 0.16f), new Color(1, 1, 1, 0.4f), 1, 4, 18, 6);
        var b = UiKit.Button(text, Fonts.Body, 26, new Color(1, 1, 1, 0.85f), Colors.White, normal, hover);
        b.ToggleMode = false;
        b.FocusMode = FocusModeEnum.None;
        b.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        b.Pressed += action;
        return b;
    }

    /// <summary>Окно-документ: здесь печатаются реплики. Размер и место — как в прототипе, увеличенные в 2 раза.</summary>
    private void BuildDocument()
    {
        _doc = new OsWindow("", false, OsStyle.Paper()) { Position = new Vector2(240, 140), Size = new Vector2(900, 1060), Visible = false };
        _typer = new Typewriter { SizeFlagsVertical = SizeFlags.ExpandFill };
        _typer.AddThemeFontOverride("normal_font", Fonts.Pixel);
        _typer.AddThemeFontOverride("bold_font", Fonts.Pixel);
        _typer.AddThemeFontOverride("italics_font", Fonts.Pixel);
        _typer.AddThemeFontSizeOverride("normal_font_size", 48);
        _typer.AddThemeFontSizeOverride("bold_font_size", 48);
        _typer.AddThemeFontSizeOverride("italics_font_size", 48);
        _typer.AddThemeColorOverride("default_color", Palette.OsInk);
        _typer.AddThemeConstantOverride("line_separation", 6);
        _typer.TextureFilter = TextureFilterEnum.Nearest;
        _typer.TypingDone += OnTyperFinished;
        _typer.WaitingForClick += StopVoice;
        _typer.Resumed += StartVoice;
        _doc.Body.AddChild(_typer);
        _windowLayer.AddChild(_doc);
    }

    /// <summary>Окно просмотра картинки (imageviewer в оригинале).</summary>
    private void BuildViewer()
    {
        _viewer = new OsWindow("", true, UiKit.Box(new Color("1b1b1b"), default, 0, 0, 10, 10)) { Visible = false };
        _viewerImage = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(1000, 1000),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _viewer.Body.AddChild(_viewerImage);
        _viewer.CloseRequested += CloseViewerByPlayer;
        _windowLayer.AddChild(_viewer);
    }

    /// <summary>Окно папки (архива): адресная строка и список файлов.</summary>
    private void BuildFolder()
    {
        _folder = new OsWindow("", true, OsStyle.Face(0)) { Visible = false, Size = new Vector2(1660, 980), Position = new Vector2(450, 170) };
        var column = UiKit.VBox(0);
        _folder.Body.AddChild(column);

        var address = UiKit.Panel(UiKit.Box(Palette.OsFace, Palette.OsFaceDark, 1, 0, 20, 12));
        column.Add(address);
        var addressRow = UiKit.HBox(16);
        address.AddChild(addressRow);
        addressRow.Add(UiKit.Label(GicLocalization.T("os.address"), Fonts.Body, 28, new Color("555555")));
        _folderPath = UiKit.Label("", Fonts.Mono, 28, Palette.OsInk);
        _folderPath.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        addressRow.Add(UiKit.Panel(UiKit.Box(Colors.White, new Color("7f9db9"), 2, 0, 14, 6))).AddChild(_folderPath);

        var area = UiKit.Panel(UiKit.Box(Colors.White, default, 0, 0, 24, 20));
        area.SizeFlagsVertical = SizeFlags.ExpandFill;
        column.Add(area);
        _folderList = UiKit.VBox(8);
        area.AddChild(_folderList);

        _folder.CloseRequested += () =>
        {
            _folder.Visible = false;
            _desktop.OpenFolder = null;
            WindowClosedByPlayer?.Invoke("folder");
        };
        _windowLayer.AddChild(_folder);
    }

    private void BuildEffects()
    {
        _flash = UiKit.Full(new ColorRect { Color = new Color(1, 1, 1, 0), MouseFilter = MouseFilterEnum.Ignore, Visible = false });
        AddChild(_flash);
        _glitchMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://addons/gic_engine/Effects/glitch.gdshader") };
        _glitch = UiKit.Full(new ColorRect { Material = _glitchMaterial, MouseFilter = MouseFilterEnum.Ignore, Visible = false });
        AddChild(_glitch);

        _notifyBox = UiKit.VBox(12);
        _notifyBox.AnchorLeft = _notifyBox.AnchorRight = _notifyBox.AnchorTop = _notifyBox.AnchorBottom = 1;
        _notifyBox.OffsetLeft = -820;
        _notifyBox.OffsetRight = -30;
        _notifyBox.OffsetTop = -TaskbarHeight - 40;
        _notifyBox.OffsetBottom = -TaskbarHeight - 20;
        _notifyBox.GrowVertical = GrowDirection.Begin;
        _notifyBox.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_notifyBox);
    }

    private void BuildAudio()
    {
        // Канал → шина: музыка в Music, эмбиент и звуки в SFX, озвучка в Voice.
        foreach (var (channel, bus) in new[] { ("music", "Music"), ("ambient", "SFX"), ("sound", "SFX"), ("voice", "Voice") })
        {
            var p = new AudioStreamPlayer { Bus = bus };
            AddChild(p);
            _channels[channel] = p;
        }
        // Отдельный плеер для «голоса» персонажа во время печати.
        _voice = new AudioStreamPlayer { Bus = "Voice" };
        AddChild(_voice);
    }

    // =================================================================== IStoryPresenter

    public void ApplyPresentation(PresentationState state)
    {
        // Сцена.
        SetSceneTexture(state.Scene);
        // Картинки show.
        foreach (var tr in _images.Values) tr.QueueFree();
        _images.Clear();
        foreach (var image in state.Images) PlaceImage(image).Modulate = Colors.White;
        // Окно просмотра (без колбэка: если игрок его закроет, сообщим через WindowClosedByPlayer).
        if (state.Viewer != null) OpenViewer(state.Viewer, null);
        else _viewer.Visible = false;
        // Звук: зацикленные каналы восстанавливаются, остальные затихают.
        foreach (var channel in new[] { "music", "ambient", "sound", "voice" })
        {
            if (state.Audio.TryGetValue(channel, out var a))
            {
                if (!_playing.TryGetValue(channel, out var current) || current != a.Path) PlayAudio(channel, a, 0.5f);
            }
            else StopAudio(channel, 0.3f);
        }
        RefreshDesktop(state.Desktop);
        _doc.Visible = false;
        _typer.ClearText();
    }

    public void CancelPending()
    {
        _token++;
        _sayDone = null;
        _say = null;
        _sayShown = false;
        _waitDone = null;
        _desktopAction = null;
        _viewerClosed = null;
        _typer.Stop();
        StopVoice();
        CloseModal();
        if (_effectDone != null || _effectLeft > 0) EndEffect(false);
    }

    public void Say(SayRequest request, Action done)
    {
        _say = request;
        _sayDone = done;
        _sayShown = false;
        _markup = TextMarkup.Parse(request.Text, Lookup, OldScale);

        _doc.Visible = true;
        _doc.Title = request.Name;
        var speed = SettingsManager.Instance.TextSpeed;
        _typer.Speed = speed >= SettingsManager.InstantTextSpeed ? float.PositiveInfinity : speed;
        _typer.Start(_markup);
        StartVoice();

        // В режиме пропуска прочитанное показываем сразу.
        if (CanSkip()) _typer.RevealAll();
    }

    public void SetScene(string path, string transition, float time, Action done)
    {
        if (transition == "none" || time <= 0)
        {
            SetSceneTexture(path);
            done?.Invoke();
            return;
        }
        var token = _token;
        if (transition == "fade")
        {
            // Через чёрный: затемнить, сменить, проявить.
            _flash.Color = new Color(0, 0, 0, 0);
            _flash.Visible = true;
            var t = CreateTween();
            t.TweenProperty(_flash, "color:a", 1f, time / 2);
            t.TweenCallback(Callable.From(() => SetSceneTexture(path)));
            t.TweenProperty(_flash, "color:a", 0f, time / 2);
            t.TweenCallback(Callable.From(() =>
            {
                _flash.Visible = false;
                if (token == _token) done?.Invoke();
            }));
            return;
        }
        // dissolve: новая картинка проявляется поверх старой.
        var next = new TextureRect
        {
            Texture = LoadTexture(path),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0),
        };
        _sceneLayer.AddChild(UiKit.Full(next));
        var tween = CreateTween();
        tween.TweenProperty(next, "modulate:a", 1f, time);
        tween.TweenCallback(Callable.From(() =>
        {
            SetSceneTexture(path);
            next.QueueFree();
            if (token == _token) done?.Invoke();
        }));
    }

    public void ShowImage(ImageState image, string transition, float time, Action done)
    {
        var rect = PlaceImage(image);
        if (transition == "none" || time <= 0)
        {
            done?.Invoke();
            return;
        }
        rect.Modulate = new Color(1, 1, 1, 0);
        var token = _token;
        var t = CreateTween();
        t.TweenProperty(rect, "modulate:a", 1f, time);
        t.TweenCallback(Callable.From(() =>
        {
            if (token == _token) done?.Invoke();
        }));
    }

    public void HideImage(string tag, string transition, float time, Action done)
    {
        if (!_images.TryGetValue(tag, out var rect))
        {
            done?.Invoke();
            return;
        }
        _images.Remove(tag);
        if (transition == "none" || time <= 0)
        {
            rect.QueueFree();
            done?.Invoke();
            return;
        }
        var token = _token;
        var t = CreateTween();
        t.TweenProperty(rect, "modulate:a", 0f, time);
        t.TweenCallback(Callable.From(() =>
        {
            rect.QueueFree();
            if (token == _token) done?.Invoke();
        }));
    }

    public void ShowViewer(string path, Action closed) => OpenViewer(path, closed);

    public void HideViewer()
    {
        _viewer.Visible = false;
        _viewerClosed = null;
    }

    public void PlayAudio(string channel, AudioState audio, float fadeIn)
    {
        var stream = GicAssets.LoadAudio(Resolve(audio.Path), audio.Loop);
        if (stream == null) return;

        // Разовые звуки могут накладываться друг на друга — каждому свой плеер.
        if (channel == "sound" && !audio.Loop)
        {
            var once = new AudioStreamPlayer { Stream = stream, Bus = "SFX", VolumeDb = Mathf.LinearToDb(audio.Volume) };
            AddChild(once);
            once.Finished += once.QueueFree;
            once.Play();
            return;
        }
        if (!_channels.TryGetValue(channel, out var player)) return;
        var target = Mathf.LinearToDb(Mathf.Max(audio.Volume, 0.001f));
        player.Stream = stream;
        player.VolumeDb = fadeIn > 0 ? -60 : target;
        player.Play();
        _playing[channel] = audio.Path;
        if (fadeIn > 0) CreateTween().TweenProperty(player, "volume_db", target, fadeIn);
    }

    public void StopAudio(string channel, float fadeOut)
    {
        if (channel == "all")
        {
            foreach (var c in _channels.Keys) StopAudio(c, fadeOut);
            return;
        }
        if (!_channels.TryGetValue(channel, out var player)) return;
        _playing.Remove(channel);
        if (fadeOut <= 0 || !player.Playing)
        {
            player.Stop();
            return;
        }
        var stream = player.Stream;
        var t = CreateTween();
        t.TweenProperty(player, "volume_db", -60f, fadeOut);
        // Останавливаем, только если за время затухания на канале не запустили новый звук.
        t.TweenCallback(Callable.From(() =>
        {
            if (player.Stream == stream && !_playing.ContainsKey(channel)) player.Stop();
        }));
    }

    public void Effect(EffectCommand effect, Action done)
    {
        if (_effectDone != null) EndEffect(true);
        var photo = SettingsManager.Instance.Photosensitive;
        _effectKind = effect.Effect;
        _effectDone = done;
        _effectLeft = Mathf.Max(effect.Time, 0.01f);
        _effectTick = 0;

        // В пропуске эффекты не тормозят чтение.
        if (CanSkipEffects())
        {
            EndEffect(true);
            return;
        }

        switch (effect.Effect)
        {
            case "glitch":
                // В режиме светочувствительности глитч слабее и «дёргается» в 8 раз реже.
                _effectPower = photo ? Mathf.Min(effect.Strength * 0.25f, 0.08f) : effect.Strength;
                _glitchMaterial.SetShaderParameter("strength", _effectPower);
                _glitch.Visible = true;
                break;
            case "shake":
                _effectPower = photo ? effect.Strength * 0.25f : effect.Strength;
                break;
            case "flash":
                var color = new Color(effect.Color ?? "#ffffff");
                color.A = photo ? 0.25f : 1f;
                _flash.Color = color;
                _flash.Visible = true;
                CreateTween().TweenProperty(_flash, "color:a", 0f, _effectLeft * (photo ? 2 : 1));
                break;
        }
    }

    public void Wait(float seconds, Action done)
    {
        _waitDone = done;
        _waitLeft = seconds;
        if (SkipActive() && seconds >= 0) FinishWait();
    }

    public void AskInput(string prompt, string defaultText, int maxLength, Action<string> done)
    {
        var token = _token;
        var window = new OsWindow(prompt, true) { Size = new Vector2(1000, 0) };
        var column = UiKit.VBox(26);
        window.Body.AddChild(column);
        column.Add(UiKit.Label(prompt, Fonts.Body, 34, Palette.OsInk));
        var field = OsStyle.Field();
        field.Text = defaultText ?? "";
        field.MaxLength = maxLength;
        column.Add(field);
        var buttons = UiKit.HBox(20);
        buttons.Alignment = BoxContainer.AlignmentMode.End;
        column.Add(buttons);

        void Submit(string text)
        {
            if (token != _token) return;
            CloseModal();
            done(text);
        }

        buttons.Add(OsStyle.DialogButton(GicLocalization.T("os.ok"))).OnPressed(() => Submit(field.Text));
        buttons.Add(OsStyle.DialogButton(GicLocalization.T("os.cancel"))).OnPressed(() => Submit(""));
        field.TextSubmitted += Submit;
        window.CloseRequested += () => Submit("");
        ShowModal(window);
        field.CallDeferred(Control.MethodName.GrabFocus);
    }

    public void AskMenu(string prompt, List<string> options, Action<int> done)
    {
        var token = _token;
        var window = new OsWindow(string.IsNullOrEmpty(prompt) ? GicLocalization.T("os.choice") : prompt, false) { Size = new Vector2(1200, 0) };
        var column = UiKit.VBox(18);
        window.Body.AddChild(column);
        if (!string.IsNullOrEmpty(prompt)) column.Add(UiKit.Paragraph(prompt, Fonts.Body, 34, Palette.OsInk));
        Button first = null;
        for (var i = 0; i < options.Count; i++)
        {
            var index = i;
            var b = OsStyle.DialogButton(options[i]);
            b.Alignment = HorizontalAlignment.Left;
            b.Pressed += () =>
            {
                if (token != _token) return;
                CloseModal();
                done(index);
            };
            column.Add(b);
            first ??= b;
        }
        ShowModal(window);
        first?.CallDeferred(Control.MethodName.GrabFocus);
    }

    public void RefreshDesktop(DesktopState desktop)
    {
        _desktop = desktop;
        _desktopLayer.Visible = desktop.Visible;
        if (!string.IsNullOrEmpty(desktop.Wallpaper) && _sceneImage.Texture == null) SetSceneTexture(desktop.Wallpaper);
        else if (desktop.Visible && _sceneImage.Texture == null) _sceneColor.Color = Palette.OsDesktop;

        RebuildIcons();
        RebuildFolder();
    }

    public void ShowError(string image, string title, string message)
    {
        OsWindow window;
        if (image != null)
        {
            // Картинка ошибки из оригинала уже нарисована вместе с рамкой — показываем как есть, только с крестиком.
            var tex = LoadTexture(image);
            window = new OsWindow(title ?? GicLocalization.T("os.error"), true, UiKit.Box(Palette.OsFace, default, 0, 0, 0, 0));
            var rect = new TextureRect
            {
                Texture = tex,
                CustomMinimumSize = tex != null ? tex.GetSize() * AssetScale(image) : new Vector2(800, 400),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                TextureFilter = TextureFilterEnum.Nearest,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            window.Body.AddChild(rect);
        }
        else
        {
            window = new OsWindow(title ?? GicLocalization.T("os.error"), true) { Size = new Vector2(900, 0) };
            var column = UiKit.VBox(26);
            window.Body.AddChild(column);
            column.Add(UiKit.Paragraph(message ?? "", Fonts.Body, 32, Palette.OsInk));
            var ok = OsStyle.DialogButton(GicLocalization.T("os.ok"));
            ok.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
            ok.Pressed += window.QueueFree;
            column.Add(ok);
        }
        window.CloseRequested += window.QueueFree;
        _windowLayer.AddChild(window);
        CenterLater(window);
    }

    public void WaitForDesktopAction(Action<string> action)
    {
        _desktopAction = action;
        // На рабочем столе в ожидании действия документ не нужен — как в оригинале после jump game.
        _doc.Visible = false;
        _typer.ClearText();
    }

    public void Notify(string text)
    {
        // Всплывающая подсказка у трея в стиле «балуна» Windows XP.
        var panel = UiKit.Panel(UiKit.Box(new Color("ffffe1"), new Color("6b6b4f"), 2, 10, 26, 18));
        panel.MouseFilter = MouseFilterEnum.Ignore;
        panel.AddChild(UiKit.Paragraph(text, Fonts.Body, 30, Palette.OsInk));
        _notifyBox.AddChild(panel);
        var t = CreateTween();
        t.TweenInterval(4.0);
        t.TweenProperty(panel, "modulate:a", 0f, 0.6f);
        t.TweenCallback(Callable.From(panel.QueueFree));
    }

    public void Checkpoint(string name, string title) => CheckpointReached?.Invoke(name, title);

    public void ReportError(string file, int line, string message)
    {
        var where = file != null ? $"{file.GetFile()}:{line}: " : "";
        GD.PushError("GiC: " + where + message);
        ErrorReported?.Invoke(file, line, message);
    }

    public void StoryEnded()
    {
        StopVoice();
        Ended?.Invoke();
    }

    // =================================================================== ввод и время

    public override void _UnhandledInput(InputEvent e)
    {
        if (!IsVisibleInTree()) return;
        if (e.IsActionPressed("vn_advance") && _modal == null)
        {
            Advance();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>«Далее»: дочитать реплику, закончить паузу или перейти к следующей реплике.</summary>
    public void Advance()
    {
        if (_effectDone != null) return;
        if (_waitDone != null)
        {
            FinishWait();
            return;
        }
        if (_sayDone == null) return;
        if (_typer.IsTyping)
        {
            _typer.Advance();
            return;
        }
        CompleteSay();
    }

    public override void _Process(double delta)
    {
        _clock.Text = string.IsNullOrEmpty(_desktop.Clock) ? DateTime.Now.ToString("HH:mm") : _desktop.Clock;
        _autoButton.Modulate = AutoActive() ? Palette.TealBright : Colors.White;
        _skipButton.Modulate = SkipActive() ? Palette.TealBright : Colors.White;

        // Пауза по времени.
        if (_waitDone != null)
        {
            if (_waitLeft >= 0)
            {
                _waitLeft -= delta;
                if (_waitLeft <= 0 || SkipActive()) FinishWait();
            }
        }

        // Реплика напечатана: пропуск или авточтение двигают историю сами.
        if (_sayDone != null && _sayShown)
        {
            if (CanSkip())
            {
                CompleteSay();
            }
            else if (AutoActive())
            {
                _autoLeft -= delta;
                if (_autoLeft <= 0) CompleteSay();
            }
        }
        else if (_sayDone != null && _typer.IsTyping && CanSkip())
        {
            _typer.RevealAll();
        }

        UpdateEffect(delta);
    }

    private void UpdateEffect(double delta)
    {
        if (_effectDone == null && _effectLeft <= 0) return;
        _effectLeft -= delta;
        _effectTick -= delta;
        var photo = SettingsManager.Instance.Photosensitive;
        if (_effectTick <= 0)
        {
            // Частота обновления: каждый кадр, а в режиме светочувствительности — 8 раз в секунду.
            _effectTick = photo ? 0.125 : 0;
            if (_effectKind == "glitch") _glitchMaterial.SetShaderParameter("seed", GD.Randf() * 1000f);
            if (_effectKind == "shake") _shake.Position = new Vector2((GD.Randf() - 0.5f) * 2, (GD.Randf() - 0.5f) * 2) * _effectPower;
        }
        if (_effectLeft <= 0) EndEffect(true);
    }

    private void EndEffect(bool callDone)
    {
        _effectLeft = 0;
        _glitch.Visible = false;
        _shake.Position = Vector2.Zero;
        var done = _effectDone;
        _effectDone = null;
        if (callDone) done?.Invoke();
    }

    private void FinishWait()
    {
        var done = _waitDone;
        _waitDone = null;
        done?.Invoke();
    }

    private void OnTyperFinished()
    {
        StopVoice();
        _sayShown = true;
        _autoLeft = SettingsManager.Instance.AutoDelay + (_markup?.CharCount ?? 0) * 0.02;
        // {nw}: не ждём клика, сразу идём дальше.
        if (_markup is { NoWait: true }) CompleteSay();
    }

    private void CompleteSay()
    {
        var done = _sayDone;
        _sayDone = null;
        _say = null;
        _sayShown = false;
        StopVoice();
        done?.Invoke();
    }

    private bool CanSkip() =>
        SkipActive() && _say != null && (_say.Seen || SettingsManager.Instance.SkipUnseen);

    private bool CanSkipEffects() => SkipActive();

    // =================================================================== голос

    private void StartVoice()
    {
        var path = _say?.Character?.Voice;
        if (path == null || !_typer.IsTyping) return;
        var stream = GicAssets.LoadAudio(Resolve(path), true);
        if (stream == null) return;
        _voice.Stream = stream;
        _voice.VolumeDb = Mathf.LinearToDb(Mathf.Max(_say.Character.VoiceVolume, 0.001f));
        _voice.Play();
    }

    private void StopVoice() => _voice?.Stop();

    // =================================================================== рабочий стол

    private void RebuildIcons()
    {
        foreach (var child in _leftIcons.GetChildren()) child.QueueFree();
        foreach (var child in _rightIcons.GetChildren()) child.QueueFree();
        foreach (var icon in _desktop.Icons)
        {
            if (icon.Hidden) continue;
            var button = new TextureButton
            {
                TextureNormal = LoadTexture(icon.Image),
                IgnoreTextureSize = true,
                StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
                TextureFilter = TextureFilterEnum.Nearest,
                TooltipText = icon.Title,
                FocusMode = FocusModeEnum.All,
            };
            var size = button.TextureNormal != null ? button.TextureNormal.GetSize() * AssetScale(icon.Image) : new Vector2(180, 220);
            button.CustomMinimumSize = size;
            // Подсветка при наведении — как выделение иконки в Windows.
            button.MouseEntered += () => button.Modulate = new Color(0.75f, 0.9f, 1f);
            button.MouseExited += () => button.Modulate = Colors.White;
            var captured = icon;
            button.Pressed += () => OnIconPressed(captured.Error, captured.Action);
            (icon.Side == "right" ? _rightIcons : _leftIcons).AddChild(button);
        }
    }

    private void RebuildFolder()
    {
        var folder = _desktop.OpenFolder != null ? _desktop.FindFolder(_desktop.OpenFolder) : null;
        if (folder == null)
        {
            _folder.Visible = false;
            return;
        }
        _folder.Title = folder.Title;
        _folderPath.Text = "DESKTOP\\" + folder.Title.ToUpperInvariant();
        foreach (var child in _folderList.GetChildren()) child.QueueFree();
        foreach (var file in folder.Files)
        {
            BaseButton button;
            var tex = LoadTexture(file.Image);
            if (tex != null)
            {
                // Строка файла из оригинала — готовая картинка с именем и путём.
                button = new TextureButton
                {
                    TextureNormal = tex,
                    IgnoreTextureSize = true,
                    StretchMode = TextureButton.StretchModeEnum.KeepAspect,
                    CustomMinimumSize = tex.GetSize() * AssetScale(file.Image),
                    TextureFilter = TextureFilterEnum.Nearest,
                };
            }
            else
            {
                var normal = UiKit.Empty(12, 8);
                var hover = UiKit.Box(new Color(0.12f, 0.43f, 0.61f, 0.25f), default, 0, 0, 12, 8);
                var b = UiKit.Button("▤  " + file.Title, Fonts.Mono, 32, Palette.OsInk, Palette.OsInk, normal, hover);
                b.Alignment = HorizontalAlignment.Left;
                button = b;
            }
            button.MouseEntered += () => button.Modulate = new Color(0.8f, 0.9f, 1f);
            button.MouseExited += () => button.Modulate = Colors.White;
            var action = file.Action;
            button.Pressed += () => OnIconPressed(null, action);
            _folderList.AddChild(button);
        }
        if (!_folder.Visible)
        {
            _folder.Visible = true;
            _folder.MoveToFront();
        }
    }

    /// <summary>Нажать иконку или файл программно (для инструментов разработчика и автотестов).</summary>
    public void SimulateDesktopAction(string action) => OnIconPressed(null, action);

    /// <summary>Клик по иконке или файлу: окно ошибки или переход истории к метке (если история ждёт действия).</summary>
    private void OnIconPressed(string error, string action)
    {
        if (error != null)
        {
            ShowError(error, null, null);
            return;
        }
        if (action == null || _desktopAction == null) return;
        var callback = _desktopAction;
        _desktopAction = null;
        callback(action);
    }

    // =================================================================== помощники

    private void OpenViewer(string path, Action closed)
    {
        var tex = LoadTexture(path);
        _viewerImage.Texture = tex;
        var size = tex != null ? tex.GetSize() * AssetScale(path) : new Vector2(1000, 1000);
        _viewerImage.CustomMinimumSize = size;
        _viewer.Title = path.GetFile();
        _viewer.Size = Vector2.Zero;
        _viewer.ResetSize();
        // Место окна как у imageviewer в прототипе: справа, по центру свободной высоты.
        _viewer.Position = new Vector2(1234, 150);
        _viewer.Visible = true;
        _viewer.MoveToFront();
        _viewerClosed = closed;
    }

    private void CloseViewerByPlayer()
    {
        _viewer.Visible = false;
        var closed = _viewerClosed;
        _viewerClosed = null;
        if (closed != null) closed();
        else WindowClosedByPlayer?.Invoke("viewer");
    }

    private void SetSceneTexture(string path)
    {
        _sceneImage.Texture = path != null ? LoadTexture(path) : null;
        _sceneColor.Color = Colors.Black;
    }

    private TextureRect PlaceImage(ImageState image)
    {
        if (_images.TryGetValue(image.Tag, out var old)) old.QueueFree();
        var tex = LoadTexture(image.Path);
        var size = tex != null ? tex.GetSize() * AssetScale(image.Path) : Vector2.Zero;
        var rect = new TextureRect
        {
            Texture = tex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Size = size,
            // x/y — доли экрана для центра картинки.
            Position = new Vector2(image.X * 2560 - size.X / 2, image.Y * 1440 - size.Y / 2),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _imageLayer.AddChild(rect);
        _images[image.Tag] = rect;
        return rect;
    }

    private void ShowModal(OsWindow window)
    {
        CloseModal();
        // Полупрозрачная подложка блокирует клики по рабочему столу, пока открыт диалог.
        var dim = UiKit.Full(new ColorRect { Color = new Color(0, 0, 0, 0.35f), MouseFilter = MouseFilterEnum.Stop });
        _windowLayer.AddChild(dim);
        dim.AddChild(window);
        _modal = dim;
        CenterLater(window);
    }

    private void CloseModal()
    {
        _modal?.QueueFree();
        _modal = null;
    }

    /// <summary>Центрировать окно, когда контейнеры посчитают его размер.</summary>
    private void CenterLater(Control window)
    {
        Callable.From(() =>
        {
            if (!IsInstanceValid(window)) return;
            window.ResetSize();
            window.Position = (new Vector2(2560, 1440 - TaskbarHeight) - window.Size) / 2;
        }).CallDeferred();
    }

    private string Resolve(string path) => Project != null ? Project.ResolveAsset(path) : path;

    private Texture2D LoadTexture(string path) => string.IsNullOrEmpty(path) ? null : GicAssets.LoadTexture(Resolve(path));

    /// <summary>Множитель размера: ассеты прототипа 2023 года увеличиваются в 2 раза, новые — как есть.</summary>
    private static float AssetScale(string path) => path != null && path.Contains("old2023/") ? OldScale : 1f;
}
