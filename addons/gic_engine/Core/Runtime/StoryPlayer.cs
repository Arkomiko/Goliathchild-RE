using System;
using System.Collections.Generic;
using GicEngine.Script;
using Godot;

namespace GicEngine.Runtime;

/// <summary>
/// Исполнитель истории. Идёт по плоскому коду меток, меняет состояние (RuntimeState) и командует презентером.
/// Сам по себе не зависит от сцен Godot, поэтому одинаково работает в игре и в предпросмотре редактора GiC_OS.
/// </summary>
public sealed class StoryPlayer
{
    // Сколько снимков хранить для отката назад. 200 реплик — с запасом, а память занимает немного.
    private const int MaxHistory = 200;

    // Защита от бесконечного цикла без единой блокирующей команды (например, jump сама на себя).
    private const int MaxStepsWithoutWait = 100000;

    public StoryProject Project { get; }
    public IStoryPresenter Presenter { get; }
    public RuntimeState State { get; private set; }
    public bool Ended { get; private set; }

    /// <summary>Реплика показана игроку (ключ «файл:строка») — игра отмечает её как прочитанную.</summary>
    public Action<string> LineShown;

    /// <summary>Проверка, читал ли игрок реплику раньше (для пропуска прочитанного).</summary>
    public Func<string, bool> WasLineSeen;

    /// <summary>Картинка показана — игра открывает её в галерее.</summary>
    public Action<string> ImageShown;

    /// <summary>История реплик для окна «Журнал» (имя, текст).</summary>
    public readonly List<(string Name, string Text)> Backlog = new();

    private readonly List<RuntimeState> _history = new();
    private readonly GicExprApi _api = new();
    private bool _running;
    private bool _resumeRequested;
    private bool _waiting;
    // Номер «поколения»: после отката или загрузки старые колбэки презентера должны игнорироваться.
    private int _generation;

    public StoryPlayer(StoryProject project, IStoryPresenter presenter)
    {
        Project = project;
        Presenter = presenter;
        _api.Player = this;
    }

    /// <summary>Начать историю с метки (по умолчанию — со стартовой из story.json).</summary>
    public void Start(string label = null)
    {
        State = new RuntimeState { StoryId = Project.Meta.Id, Label = label ?? Project.Meta.Start, Ip = 0 };
        State.Variables["doc"] = "";
        // Переменные var вычисляются по порядку, поэтому одна может ссылаться на другую.
        foreach (var v in Project.Variables)
        {
            var value = GicExpressions.Evaluate(v.Expression, State.Variables, _api, out var error);
            if (error != null) Presenter.ReportError(v.File, v.Line, error);
            State.Variables[v.Name] = value;
        }
        ResetRun();
        Presenter.ApplyPresentation(State.Presentation);
        Run();
    }

    /// <summary>Продолжить с загруженного состояния (из файла сохранения).</summary>
    public void Restore(RuntimeState state)
    {
        State = state.Clone();
        ResetRun();
        Presenter.ApplyPresentation(State.Presentation);
        Run();
    }

    /// <summary>Можно ли откатиться на предыдущую реплику.</summary>
    public bool CanRollback => _history.Count > 1;

    /// <summary>Откат на предыдущую реплику (колесо мыши вверх).</summary>
    public void Rollback()
    {
        if (!CanRollback) return;
        // Последний снимок — текущая реплика, предпоследний — та, к которой возвращаемся.
        _history.RemoveAt(_history.Count - 1);
        var target = _history[^1];
        // Снимок будет записан заново, когда реплика выполнится повторно.
        _history.RemoveAt(_history.Count - 1);
        if (Backlog.Count > 0) Backlog.RemoveAt(Backlog.Count - 1);
        if (Backlog.Count > 0) Backlog.RemoveAt(Backlog.Count - 1);

        State = target.Clone();
        _generation++;
        Presenter.CancelPending();
        Presenter.ApplyPresentation(State.Presentation);
        _waiting = false;
        Run();
    }

    /// <summary>Снимок текущего состояния для сохранения игры.</summary>
    public RuntimeState Snapshot() => State.Clone();

    /// <summary>
    /// Игрок сам закрыл окно просмотра или папку. Убираем их из состояния,
    /// иначе после загрузки сохранения закрытое окно появилось бы снова.
    /// </summary>
    public void OnWindowClosedByPlayer(string kind)
    {
        if (State == null) return;
        if (kind == "viewer") State.Presentation.Viewer = null;
        else if (kind == "folder") State.Presentation.Desktop.OpenFolder = null;
    }

    private void ResetRun()
    {
        Ended = false;
        _history.Clear();
        Backlog.Clear();
        _generation++;
        Presenter.CancelPending();
        _waiting = false;
    }

    // ---------------------------------------------------------------- основной цикл

    /// <summary>
    /// Запуск цикла. Если презентер вызвал колбэк прямо во время выполнения команды (например, в режиме пропуска),
    /// повторный вход не делаем, а просим текущий цикл продолжить — так нет рекурсии и переполнения стека.
    /// </summary>
    private void Run()
    {
        if (_running)
        {
            _resumeRequested = true;
            return;
        }
        _running = true;
        try
        {
            do
            {
                _resumeRequested = false;
                Loop();
            } while (_resumeRequested && !_waiting && !Ended);
        }
        finally
        {
            _running = false;
        }
    }

    /// <summary>Колбэк презентера: блокирующая команда завершилась.</summary>
    private Action Continue(Action beforeResume = null)
    {
        var gen = _generation;
        return () =>
        {
            if (gen != _generation || Ended) return;
            beforeResume?.Invoke();
            _waiting = false;
            Run();
        };
    }

    private void Loop()
    {
        var steps = 0;
        while (!_waiting && !Ended)
        {
            if (++steps > MaxStepsWithoutWait)
            {
                Fail(null, 0, $"Похоже на бесконечный цикл в метке «{State.Label}»: история остановлена");
                return;
            }
            if (!Project.Labels.TryGetValue(State.Label ?? "", out var label))
            {
                Fail(null, 0, $"Метка «{State.Label}» не найдена");
                return;
            }
            if (State.Ip == 0) State.VisitedLabels.Add(label.Name);

            if (State.Ip >= label.Code.Count)
            {
                // Конец метки: проваливаемся в следующую метку файла или возвращаемся из call.
                if (label.FallThrough != null)
                {
                    State.Label = label.FallThrough;
                    State.Ip = 0;
                }
                else
                {
                    DoReturn();
                }
                continue;
            }

            var instr = label.Code[State.Ip];
            switch (instr.Op)
            {
                case OpCode.Goto:
                    State.Ip = instr.Target;
                    break;
                case OpCode.JumpIfNot:
                    State.Ip = Truthy(instr.Condition, instr) ? State.Ip + 1 : instr.Target;
                    break;
                case OpCode.Menu:
                    ExecMenu(instr);
                    break;
                default:
                    Exec(instr.Command);
                    break;
            }
        }
    }

    // ---------------------------------------------------------------- команды

    private void Exec(Command command)
    {
        switch (command)
        {
            case SayCommand say:
                ExecSay(say);
                break;

            case DocCommand doc:
                State.Variables["doc"] = doc.Title;
                State.Ip++;
                break;

            case SceneCommand scene:
                State.Presentation.Scene = scene.Path;
                if (scene.Path != null) ImageShown?.Invoke(scene.Path);
                Block(done => Presenter.SetScene(scene.Path, scene.Transition, scene.Time, done), scene.Transition != "none");
                break;

            case ShowCommand show:
            {
                var image = new ImageState { Tag = show.Tag, Path = show.Path, X = show.X, Y = show.Y };
                State.Presentation.Images.RemoveAll(i => i.Tag == show.Tag);
                State.Presentation.Images.Add(image);
                ImageShown?.Invoke(show.Path);
                Block(done => Presenter.ShowImage(image, show.Transition, show.Time, done), show.Transition != "none");
                break;
            }

            case HideCommand hide:
                State.Presentation.Images.RemoveAll(i => i.Tag == hide.Tag);
                Block(done => Presenter.HideImage(hide.Tag, hide.Transition, hide.Time, done), hide.Transition != "none");
                break;

            case ViewerCommand viewer:
                ExecViewer(viewer);
                break;

            case AudioCommand audio:
            {
                var a = new AudioState { Path = audio.Path, Volume = audio.Volume, Loop = audio.Loop };
                // В состояние попадают только зацикленные звуки: их надо восстановить после загрузки.
                if (audio.Loop) State.Presentation.Audio[audio.Channel] = a;
                Presenter.PlayAudio(audio.Channel, a, audio.Fade);
                State.Ip++;
                break;
            }

            case StopCommand stop:
                if (stop.Channel == "all") State.Presentation.Audio.Clear();
                else State.Presentation.Audio.Remove(stop.Channel);
                Presenter.StopAudio(stop.Channel, stop.Fade);
                State.Ip++;
                break;

            case PauseCommand pause:
                Block(done => Presenter.Wait(pause.Seconds, done), true);
                break;

            case EffectCommand effect:
                Block(done => Presenter.Effect(effect, done), true);
                break;

            case JumpCommand jump:
                if (jump.IsCall) State.CallStack.Add(new CallFrame { Label = State.Label, Ip = State.Ip + 1 });
                State.Label = jump.Target;
                State.Ip = 0;
                break;

            case ReturnCommand:
                DoReturn();
                break;

            case SetCommand set:
            {
                var value = GicExpressions.Evaluate(set.Expression, State.Variables, _api, out var error);
                if (error != null) Presenter.ReportError(set.File, set.Line, error);
                else State.Variables[set.Variable] = value;
                State.Ip++;
                break;
            }

            case InputCommand input:
                PushHistory();
                _waiting = true;
                Presenter.AskInput(Interpolate(input.Prompt), input.Default, input.MaxLength, text =>
                {
                    Continue(() =>
                    {
                        State.Variables[input.Variable] = text ?? "";
                        State.Ip++;
                    })();
                });
                break;

            case OsCommand os:
                ExecOs(os);
                break;

            case CheckpointCommand cp:
                State.ChapterTitle = cp.Title ?? cp.Name;
                // Сначала сдвигаемся, чтобы автосохранение продолжилось уже после контрольной точки.
                State.Ip++;
                Presenter.Checkpoint(cp.Name, cp.Title);
                break;

            case NotifyCommand notify:
                Presenter.Notify(Interpolate(notify.Text));
                State.Ip++;
                break;

            case EndCommand:
                EndStory();
                break;

            default:
                Fail(command.File, command.Line, $"Команда «{command.Kind}» пока не поддерживается");
                break;
        }
    }

    /// <summary>
    /// Выполнить команду с колбэком. Если wait == false — не ждём завершения (например, смена фона без перехода),
    /// иначе история стоит, пока презентер не вызовет done.
    /// </summary>
    private void Block(Action<Action> start, bool wait)
    {
        if (!wait)
        {
            start(null);
            State.Ip++;
            return;
        }
        _waiting = true;
        start(Continue(() => State.Ip++));
    }

    private void ExecSay(SayCommand say)
    {
        PushHistory();
        CharacterDef character = null;
        if (say.Speaker != null) Project.Characters.TryGetValue(say.Speaker, out character);

        var key = $"{say.File.GetFile()}:{say.Line}";
        var request = new SayRequest
        {
            Character = character,
            Name = character != null ? Interpolate(character.Name) : "",
            Text = say.Text,
            Seen = WasLineSeen?.Invoke(key) ?? false,
            Key = key,
        };
        State.LastText = TextMarkup.Parse(say.Text, LookupVariable).Plain;
        Backlog.Add((request.Name, State.LastText));
        LineShown?.Invoke(key);

        _waiting = true;
        Presenter.Say(request, Continue(() => State.Ip++));
    }

    private void ExecMenu(Instruction instr)
    {
        PushHistory();
        // Показываем только варианты, условие которых выполнено.
        var visible = new List<MenuChoice>();
        foreach (var choice in instr.Choices)
        {
            if (choice.Condition == null || Truthy(choice.Condition, instr)) visible.Add(choice);
        }
        if (visible.Count == 0)
        {
            Fail(instr.File, instr.Line, "В меню нет ни одного доступного варианта");
            return;
        }
        var texts = visible.ConvertAll(c => Interpolate(c.Text));
        _waiting = true;
        Presenter.AskMenu(Interpolate(instr.Prompt ?? ""), texts, index =>
        {
            Continue(() =>
            {
                index = Math.Clamp(index, 0, visible.Count - 1);
                Backlog.Add(("▸", texts[index]));
                State.Ip = visible[index].Target;
            })();
        });
    }

    private void ExecViewer(ViewerCommand viewer)
    {
        if (viewer.Hide)
        {
            State.Presentation.Viewer = null;
            Presenter.HideViewer();
            State.Ip++;
            return;
        }
        State.Presentation.Viewer = viewer.Path;
        ImageShown?.Invoke(viewer.Path);

        if (!viewer.Wait)
        {
            // Окно закрыто игроком — убираем его и из состояния, чтобы оно не вернулось после загрузки.
            var gen = _generation;
            Presenter.ShowViewer(viewer.Path, () =>
            {
                if (gen == _generation && State.Presentation.Viewer == viewer.Path) State.Presentation.Viewer = null;
            });
            State.Ip++;
            return;
        }
        _waiting = true;
        Presenter.ShowViewer(viewer.Path, Continue(() =>
        {
            State.Presentation.Viewer = null;
            State.Ip++;
        }));
    }

    /// <summary>Команды виртуальной ОС. Состояние рабочего стола хранится в истории, презентер только рисует.</summary>
    private void ExecOs(OsCommand os)
    {
        var desktop = State.Presentation.Desktop;
        switch (os.Action)
        {
            case "desktop":
                // os desktop show|hide [wallpaper="путь"]
                desktop.Visible = os.Arg(0, "show") != "hide";
                desktop.Wallpaper = os.Opt("wallpaper", desktop.Wallpaper);
                break;

            case "icon":
            {
                // os icon id ["Заголовок"] [image=] [side=] [action=] [error=] [hidden=true]
                // Повторная команда с тем же id меняет только указанные поля — так архив «разблокируется».
                var id = os.Arg(0);
                var icon = desktop.FindIcon(id);
                if (icon == null)
                {
                    icon = new DesktopIcon { Id = id };
                    desktop.Icons.Add(icon);
                }
                icon.Title = os.Arg(1, icon.Title ?? id);
                icon.Image = os.Opt("image", icon.Image);
                icon.Side = os.Opt("side", icon.Side);
                if (os.Options.ContainsKey("action")) icon.Action = NullIfNone(os.Opt("action"));
                if (os.Options.ContainsKey("error")) icon.Error = NullIfNone(os.Opt("error"));
                if (os.Options.ContainsKey("hidden")) icon.Hidden = os.Opt("hidden") == "true";
                break;
            }

            case "remove":
                desktop.Icons.RemoveAll(i => i.Id == os.Arg(0));
                break;

            case "folder":
            {
                // os folder id "Заголовок" — создать папку (или переименовать существующую).
                var folder = desktop.FindFolder(os.Arg(0));
                if (folder == null)
                {
                    folder = new DesktopFolder { Id = os.Arg(0) };
                    desktop.Folders.Add(folder);
                }
                folder.Title = os.Arg(1, folder.Title ?? folder.Id);
                break;
            }

            case "file":
            {
                // os file папка id "Название" [image=] [action=]
                var folder = desktop.FindFolder(os.Arg(0));
                if (folder == null)
                {
                    Presenter.ReportError(os.File, os.Line, $"Папка «{os.Arg(0)}» не создана (os folder {os.Arg(0)} \"...\")");
                    break;
                }
                var file = folder.Files.Find(f => f.Id == os.Arg(1));
                if (file == null)
                {
                    file = new DesktopFile { Id = os.Arg(1) };
                    folder.Files.Add(file);
                }
                file.Title = os.Arg(2, file.Title ?? file.Id);
                file.Image = os.Opt("image", file.Image);
                file.Action = os.Opt("action", file.Action);
                break;
            }

            case "open":
                desktop.OpenFolder = os.Arg(0);
                break;

            case "close":
                desktop.OpenFolder = null;
                break;

            case "clock":
                desktop.Clock = Interpolate(os.Arg(0, ""));
                break;

            case "error":
                // os error "картинка"  или  os error "Заголовок" "Текст"
                if (os.Args.Count >= 2) Presenter.ShowError(null, Interpolate(os.Arg(0)), Interpolate(os.Arg(1)));
                else Presenter.ShowError(os.Arg(0), null, null);
                State.Ip++;
                return;

            case "wait":
                // Ждём клика по иконке или файлу. Действие задаёт, к какой метке перейти.
                Presenter.RefreshDesktop(desktop);
                _waiting = true;
                var gen = _generation;
                Presenter.WaitForDesktopAction(target =>
                {
                    if (gen != _generation || Ended) return;
                    if (!Project.Labels.ContainsKey(target ?? ""))
                    {
                        Presenter.ReportError(os.File, os.Line, $"Действие ведёт на несуществующую метку «{target}»");
                        return;
                    }
                    State.Label = target;
                    State.Ip = 0;
                    _waiting = false;
                    Run();
                });
                return;

            default:
                Presenter.ReportError(os.File, os.Line, $"Неизвестное действие ОС «{os.Action}»");
                break;
        }
        Presenter.RefreshDesktop(desktop);
        State.Ip++;
    }

    private void DoReturn()
    {
        if (State.CallStack.Count == 0)
        {
            EndStory();
            return;
        }
        var frame = State.CallStack[^1];
        State.CallStack.RemoveAt(State.CallStack.Count - 1);
        State.Label = frame.Label;
        State.Ip = frame.Ip;
    }

    private void EndStory()
    {
        Ended = true;
        _waiting = true;
        Presenter.StoryEnded();
    }

    private void Fail(string file, int line, string message)
    {
        Presenter.ReportError(file, line, message);
        EndStory();
    }

    // ---------------------------------------------------------------- помощники

    private void PushHistory()
    {
        _history.Add(State.Clone());
        if (_history.Count > MaxHistory) _history.RemoveAt(0);
    }

    private bool Truthy(string expression, Instruction instr)
    {
        var value = GicExpressions.Evaluate(expression, State.Variables, _api, out var error);
        if (error != null) Presenter.ReportError(instr.File, instr.Line, error);
        return GicExpressions.IsTruthy(value);
    }

    /// <summary>Значение переменной для подстановки [имя] в тексте.</summary>
    public string LookupVariable(string name) =>
        State.Variables.TryGetValue(name, out var v) ? GicExpressions.Format(v) : "";

    /// <summary>Подстановка переменных в строку (имена персонажей, подсказки ввода, варианты меню).</summary>
    public string Interpolate(string text) =>
        string.IsNullOrEmpty(text) || text.IndexOf('[') < 0 ? text ?? "" : TextMarkup.Parse(text, LookupVariable).Plain;

    private static string NullIfNone(string s) => s == "none" || s == "" ? null : s;
}
