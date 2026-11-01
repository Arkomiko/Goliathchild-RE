using GicEngine.Save;
using GicEngine.Script;
using GoliathChildRE.Editor;
using GoliathChildRE.Menu;
using GoliathChildRE.Session;
using Godot;

namespace GoliathChildRE;

/// <summary>
/// Навигация по игре: главное меню, новая игра, загрузка, «Продолжить», редактор GiC_OS, выход.
/// Хранит основной сюжет (вшит в игру, ответ 56) и текущую игровую сессию.
/// </summary>
public partial class GameManager : Node
{
    public static GameManager Instance { get; private set; }

    /// <summary>Папка встроенного основного сюжета.</summary>
    public const string MainStoryRoot = "res://scenarios/goliathchild";

    private Main _main;
    private StoryProject _mainStory;
    private GicOsEditor _editor;

    public GameSession Session { get; private set; }

    /// <summary>Открытое окно редактора GiC_OS или null.</summary>
    public GicOsEditor Editor => IsInstanceValid(_editor) ? _editor : null;

    public override void _EnterTree()
    {
        Instance = this;
    }

    /// <summary>Основной сюжет. Загружается при первом обращении и кэшируется.</summary>
    public StoryProject MainStory
    {
        get
        {
            if (_mainStory != null) return _mainStory;
            _mainStory = StoryProject.Load(MainStoryRoot);
            foreach (var d in _mainStory.Diagnostics)
            {
                if (d.Severity == DiagnosticSeverity.Error) GD.PushError(d.ToString());
                else if (d.Severity == DiagnosticSeverity.Warning) GD.PushWarning(d.ToString());
            }
            return _mainStory;
        }
    }

    /// <summary>Main сообщает о себе после создания слоёв; сразу показываем главное меню.</summary>
    public void Attach(Main main)
    {
        _main = main;
        ShowMainMenu();
    }

    /// <summary>Главное меню. page — сразу открыть страницу (например, "saves").</summary>
    public void ShowMainMenu(string page = null)
    {
        Session = null;
        _main.ChangeScreen(() => new MainMenu(page));
    }

    /// <summary>Начать основной сюжет с начала или с главы (метки).</summary>
    public void NewGame(string label = null)
    {
        StartSession(MainStory, s => s.Start(label));
    }

    /// <summary>Загрузить сохранение.</summary>
    public void LoadGame(SaveInfo info)
    {
        var state = SaveManager.Instance.Load(info.FilePath);
        if (state == null) return;
        StartSession(MainStory, s => s.Restore(state));
    }

    /// <summary>«Продолжить» — самое свежее сохранение основного сюжета.</summary>
    public void ContinueLatest()
    {
        var latest = SaveManager.Instance.Latest(MainStory.Meta.Id);
        if (latest != null) LoadGame(latest);
    }

    private void StartSession(StoryProject story, System.Action<GameSession> begin)
    {
        _main.ChangeScreen(() =>
        {
            Session = new GameSession(story);
            // Запускаем историю после того, как сессия попала в дерево и построила интерфейс.
            Session.Ready += () => begin(Session);
            return Session;
        });
    }

    /// <summary>Открыть редактор GiC_OS отдельным окном (ответ 35). Повторный вызов просто выводит окно вперёд.</summary>
    public void OpenEditor()
    {
        if (IsInstanceValid(_editor))
        {
            _editor.GrabFocus();
            return;
        }
        _editor = new GicOsEditor();
        GetTree().Root.AddChild(_editor);
        _editor.PopupCentered();
    }

    public void QuitGame()
    {
        SaveManager.Instance.FlushPersistent();
        SettingsManager.Instance.Save();
        GetTree().Quit();
    }
}
