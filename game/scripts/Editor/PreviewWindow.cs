using GicEngine.Runtime;
using GicEngine.Script;
using GoliathChildRE.Os;
using GoliathChildRE.Ui;
using Godot;

namespace GoliathChildRE.Editor;

/// <summary>
/// Предпросмотр истории из редактора GiC_OS («Запустить отсюда», ответ 70).
/// Отдельное окно с тем же рабочим столом ОС, что и в игре; сохранения в предпросмотре не пишутся.
/// Ошибки исполнения выводятся полосой сверху, чтобы автор сразу видел, что сломалось.
/// </summary>
public partial class PreviewWindow : Window
{
    private readonly StoryProject _project;
    private readonly string _label;
    private StoryPlayer _player;
    private VBoxContainer _errors;

    public PreviewWindow(StoryProject project, string label)
    {
        _project = project;
        _label = label;
    }

    public PreviewWindow() { }

    public override void _Ready()
    {
        Title = $"GiC_OS — предпросмотр: {_project.Meta.Title} ({_label ?? _project.Meta.Start})";
        Size = new Vector2I(1280, 720);
        MinSize = new Vector2I(640, 360);
        // Рабочий стол рассчитан на 2560×1440 — масштабируем его под размер окна, как в игре.
        ContentScaleSize = new Vector2I(2560, 1440);
        ContentScaleMode = ContentScaleModeEnum.CanvasItems;
        ContentScaleAspect = ContentScaleAspectEnum.Expand;
        CloseRequested += QueueFree;
        // У отдельного окна свой поток ввода: слушаем его через сигнал окна, а не через _Input.
        WindowInput += OnWindowInput;

        AddChild(UiKit.Full(new ColorRect { Color = Colors.Black }));
        var presenter = UiKit.Full(new DesktopPresenter { Project = _project });
        AddChild(presenter);

        _errors = UiKit.VBox(6);
        _errors.Position = new Vector2(20, 20);
        AddChild(_errors);

        _player = new StoryPlayer(_project, presenter);
        presenter.Lookup = _player.LookupVariable;
        presenter.SkipActive = () => Input.IsActionPressed("vn_skip");
        presenter.WindowClosedByPlayer += _player.OnWindowClosedByPlayer;
        presenter.ErrorReported += OnError;
        presenter.CheckpointReached += (name, title) => presenter.Notify($"Контрольная точка: {title ?? name} (в предпросмотре не сохраняется)");
        presenter.Ended += () => presenter.Notify("История закончилась. Закройте окно предпросмотра.");

        // Запуск после того, как презентер построил свои слои.
        Callable.From(() => _player.Start(_label)).CallDeferred();
    }

    private void OnError(string file, int line, string message)
    {
        var where = file != null ? $"{file.GetFile()}:{line} — " : "";
        var label = UiKit.Label("⚠ " + where + message, Fonts.Mono, 30, Colors.White);
        var panel = UiKit.Panel(UiKit.Box(new Color(0.7f, 0.1f, 0.15f, 0.9f), default, 0, 6, 16, 8));
        panel.AddChild(label);
        _errors.AddChild(panel);
    }

    private void OnWindowInput(InputEvent e)
    {
        if (e.IsActionPressed("vn_rollback"))
        {
            _player?.Rollback();
        }
        else if (e is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            QueueFree();
        }
    }
}
