using GicEngine.Localization;
using GicEngine.Runtime;
using GicEngine.Save;
using GicEngine.Script;
using GoliathChildRE.Menu;
using GoliathChildRE.Os;
using GoliathChildRE.Ui;
using Godot;

namespace GoliathChildRE.Session;

/// <summary>
/// Игровая сессия: связывает исполнитель истории (StoryPlayer) с рабочим столом ОС (DesktopPresenter).
/// Отвечает за меню «Пуск» (пауза), сохранения (авто, F5/F9, ручные), откат назад, журнал реплик,
/// авточтение и пропуск, учёт времени игры и открытие глав и картинок галереи.
/// </summary>
public partial class GameSession : Control
{
    private readonly StoryProject _story;
    private DesktopPresenter _presenter;
    private StoryPlayer _player;
    private StartMenu _startMenu;
    private Control _overlay;
    private bool _auto;
    private bool _skipToggle;

    public GameSession(StoryProject story)
    {
        _story = story;
    }

    public GameSession() { }

    public StoryPlayer Player => _player;

    /// <summary>Рабочий стол ОС этой сессии (нужен инструментам разработчика).</summary>
    public DesktopPresenter Presenter => _presenter;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;

        _presenter = UiKit.Full(new DesktopPresenter { Project = _story });
        AddChild(_presenter);

        _player = new StoryPlayer(_story, _presenter)
        {
            LineShown = SaveManager.Instance.MarkLineSeen,
            WasLineSeen = SaveManager.Instance.IsLineSeen,
            ImageShown = SaveManager.Instance.MarkImageSeen,
        };
        _presenter.Lookup = _player.LookupVariable;
        _presenter.SkipActive = () => _skipToggle || Input.IsActionPressed("vn_skip");
        _presenter.AutoActive = () => _auto;
        _presenter.StartPressed += ToggleStartMenu;
        _presenter.AutoPressed += ToggleAuto;
        _presenter.SkipPressed += ToggleSkip;
        _presenter.BacklogPressed += ShowBacklog;
        _presenter.WindowClosedByPlayer += _player.OnWindowClosedByPlayer;
        _presenter.CheckpointReached += OnCheckpoint;
        _presenter.Ended += OnStoryEnded;

        _startMenu = new StartMenu(this) { Visible = false };
        AddChild(_startMenu);
    }

    /// <summary>Начать историю (с начала или с главы).</summary>
    public void Start(string label) => _player.Start(label);

    /// <summary>Продолжить с загруженного состояния.</summary>
    public void Restore(RuntimeState state) => _player.Restore(state);

    public override void _Process(double delta)
    {
        // Время игры не идёт, пока открыто меню «Пуск» или другое окно поверх игры.
        if (_player?.State != null && !_startMenu.Visible && _overlay == null) _player.State.PlayTime += delta;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_player == null) return;
        if (e.IsActionPressed("vn_menu"))
        {
            if (_overlay != null) CloseOverlay();
            else ToggleStartMenu();
            GetViewport().SetInputAsHandled();
            return;
        }
        // Остальные клавиши работают только во время чтения, без открытых меню.
        if (_startMenu.Visible || _overlay != null) return;

        if (e.IsActionPressed("vn_rollback"))
        {
            _player.Rollback();
            GetViewport().SetInputAsHandled();
        }
        else if (e.IsActionPressed("vn_auto"))
        {
            ToggleAuto();
            GetViewport().SetInputAsHandled();
        }
        else if (e.IsActionPressed("vn_backlog"))
        {
            ShowBacklog();
            GetViewport().SetInputAsHandled();
        }
        else if (e.IsActionPressed("quick_save"))
        {
            QuickSave();
            GetViewport().SetInputAsHandled();
        }
        else if (e.IsActionPressed("quick_load"))
        {
            QuickLoad();
            GetViewport().SetInputAsHandled();
        }
    }

    // ---------------------------------------------------------------- режимы чтения

    public void ToggleAuto()
    {
        _auto = !_auto;
        _presenter.Notify(GicLocalization.T(_auto ? "session.auto_on" : "session.auto_off"));
    }

    public void ToggleSkip()
    {
        _skipToggle = !_skipToggle;
        _presenter.Notify(GicLocalization.T(_skipToggle ? "session.skip_on" : "session.skip_off"));
    }

    // ---------------------------------------------------------------- сохранения

    /// <summary>Миниатюра текущего кадра для карточки сохранения (без меню «Пуск» на картинке).</summary>
    private byte[] CaptureThumbnail()
    {
        var image = GetViewport().GetTexture().GetImage();
        if (image == null) return null;
        image.Resize(480, 270, Image.Interpolation.Bilinear);
        return image.SavePngToBuffer();
    }

    private SaveInfo Save(SaveKind kind, string overwrite = null)
    {
        var info = SaveManager.Instance.Write(kind, _player.Snapshot(), _story.Meta.Title, CaptureThumbnail(), overwrite);
        return info;
    }

    /// <summary>
    /// Ручное сохранение (новое или поверх выбранного). Вызывается из списка сохранений в игре.
    /// Миниатюра снимается без меню на экране: прячем его, ждём один отрисованный кадр, снимаем и возвращаем меню.
    /// </summary>
    public async void SaveManual(string overwritePath, System.Action after = null)
    {
        var menuVisible = _startMenu.Visible;
        var overlay = _overlay;
        _startMenu.Visible = false;
        if (overlay != null) overlay.Visible = false;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Save(SaveKind.Manual, overwritePath);
        _startMenu.Visible = menuVisible;
        if (IsInstanceValid(overlay)) overlay.Visible = true;
        _presenter.Notify(GicLocalization.T("session.saved"));
        after?.Invoke();
    }

    public void QuickSave()
    {
        Save(SaveKind.Quick);
        _presenter.Notify(GicLocalization.T("session.quicksaved"));
    }

    public void QuickLoad()
    {
        var quick = SaveManager.Instance.List(_story.Meta.Id).Find(s => s.Kind == SaveKind.Quick);
        if (quick == null)
        {
            _presenter.Notify(GicLocalization.T("session.no_quicksave"));
            return;
        }
        LoadSave(quick);
    }

    public void LoadSave(SaveInfo info)
    {
        var state = SaveManager.Instance.Load(info.FilePath);
        if (state == null)
        {
            _presenter.Notify(GicLocalization.T("session.load_failed"));
            return;
        }
        CloseOverlay();
        _startMenu.Visible = false;
        _player.Restore(state);
    }

    /// <summary>Контрольная точка: автосохранение и открытие главы в меню «История».</summary>
    private void OnCheckpoint(string name, string title)
    {
        if (!string.IsNullOrEmpty(name)) SaveManager.Instance.UnlockChapter(name);
        // Миниатюру снимаем в следующем кадре, когда экран уже обновился.
        Callable.From(() =>
        {
            if (!IsInstanceValid(this)) return;
            Save(SaveKind.Auto);
            _presenter.Notify(GicLocalization.T("session.autosaved"));
        }).CallDeferred();
    }

    private void OnStoryEnded()
    {
        SaveManager.Instance.MarkStoryCompleted();
        GameManager.Instance.ShowMainMenu();
    }

    // ---------------------------------------------------------------- меню и окна поверх игры

    public void ToggleStartMenu()
    {
        if (_overlay != null) CloseOverlay();
        _startMenu.Visible = !_startMenu.Visible;
        if (_startMenu.Visible) _startMenu.FocusFirst();
    }

    /// <summary>Показать страницу (сохранения, настройки) поверх игры.</summary>
    public void ShowOverlay(Control page)
    {
        CloseOverlay();
        _startMenu.Visible = false;
        var layer = UiKit.Full(new Control());
        layer.Theme = UiKit.CinemaTheme();
        layer.AddChild(UiKit.Full(new ColorRect { Color = new Color(0, 0, 0, 0.7f) }));
        var host = new Control { Position = new Vector2(330, 120), Size = new Vector2(1900, 1150) };
        layer.AddChild(host);
        host.AddChild(UiKit.Full(page));
        var close = UiKit.CinemaButton("×  " + GicLocalization.T("session.close"), 30);
        close.Position = new Vector2(330, 1290);
        close.Pressed += CloseOverlay;
        layer.AddChild(close);
        AddChild(layer);
        _overlay = layer;
    }

    public void CloseOverlay()
    {
        _overlay?.QueueFree();
        _overlay = null;
    }

    public void ShowSaves() => ShowOverlay(new SavesPage(this));

    public void ShowSettings() => ShowOverlay(new SettingsPage());

    /// <summary>Журнал реплик: всё прочитанное в этой сессии, новые внизу.</summary>
    public void ShowBacklog()
    {
        var frame = PageFrame.Create(GicLocalization.T("session.backlog"), out var content);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        content.Add(scroll);
        var list = UiKit.VBox(22);
        list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.Add(list);
        foreach (var (name, text) in _player.Backlog)
        {
            if (!string.IsNullOrEmpty(name)) list.Add(UiKit.Label(name, Fonts.BodyBold, 28, Palette.TealBright));
            list.Add(UiKit.Paragraph(text, Fonts.Body, 32, Palette.Text));
        }
        var page = new Control();
        page.AddChild(frame);
        ShowOverlay(page);
        // Прокрутка к последней реплике после того, как список посчитает высоту.
        Callable.From(() => scroll.ScrollVertical = (int)scroll.GetVScrollBar().MaxValue).CallDeferred();
    }

    public void ExitToMenu()
    {
        GameManager.Instance.ShowMainMenu();
    }
}
