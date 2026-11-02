using System.Collections.Generic;
using System.Text;
using GicEngine.IO;
using GicEngine.Script;
using GoliathChildRE.Ui;
using Godot;

namespace GoliathChildRE.Editor;

/// <summary>
/// Редактор историй GiC_OS — режим DEVELOP (ответы 51, 61–75).
/// Открывается отдельным окном из главного меню. По логике похож на редактор карт Portal 2: инструмент внутри игры,
/// которым фанаты собирают свои истории из готовых «кубиков» и своих ассетов.
///
/// Один и тот же сценарий показывается тремя способами: граф (Blueprint), таймлайн и текст .gics.
/// Источник истины — текст: граф и таймлайн пересобираются из него при каждом изменении.
/// Основной сюжет открывается только для чтения (ответ 81), свои истории живут в папке stories рядом с игрой.
/// </summary>
public partial class GicOsEditor : Window
{
    // ---- данные открытой истории
    private string _root;
    private StoryMeta _meta;
    private bool _readOnly;
    private readonly Dictionary<string, string> _texts = new();
    private readonly HashSet<string> _dirty = new();
    private StoryProject _project;
    private string _currentFile;
    private string _selectedLabel;
    private bool _advanced = true;

    // ---- интерфейс
    private Control _host;
    private Label _storyTitle;
    private Label _status;
    private Button _modeButton;
    private Tree _tree;
    private TabContainer _tabs;
    private EditorGraph _graph;
    private HBoxContainer _timeline;
    private CodeEdit _code;
    private OptionButton _fileSelect;
    private ItemList _diagnostics;
    private VBoxContainer _inspector;
    private Timer _reparse;
    private bool _suppressTextEvent;

    public override void _Ready()
    {
        Title = "GiC_OS — DEVELOP";
        Size = new Vector2I(1600, 960);
        MinSize = new Vector2I(1100, 680);
        Theme = EditorStyle.Build();
        CloseRequested += OnCloseRequested;
        WindowInput += OnWindowInput;

        AddChild(UiKit.Full(new ColorRect { Color = Palette.EdBg }));
        var column = UiKit.Full(UiKit.VBox(0));
        AddChild(column);

        column.Add(BuildTopBar());
        _host = new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        column.Add(_host);
        var statusBar = UiKit.Panel(UiKit.Box(Palette.EdPanel, Palette.EdLine, 0, 0, 12, 4));
        column.Add(statusBar);
        _status = statusBar.Add(UiKit.Label("", Fonts.Body, 15, Palette.EdMuted));

        _reparse = new Timer { WaitTime = 0.5, OneShot = true };
        _reparse.Timeout += Reparse;
        AddChild(_reparse);

        ShowProjects();
    }

    // =================================================================== верхняя панель

    private Control BuildTopBar()
    {
        var bar = UiKit.Panel(UiKit.Box(Palette.EdPanel, Palette.EdLine, 0, 0, 14, 8));
        var row = UiKit.HBox(12);
        bar.AddChild(row);

        row.Add(UiKit.Label("GiC_OS", Fonts.MenuBold, 24, Palette.EdAccent));
        var badge = UiKit.Panel(UiKit.Box(new Color("b71c1c"), default, 0, 4, 10, 2));
        badge.Add(UiKit.Label("DEVELOP", Fonts.MenuBold, 14, Colors.White));
        badge.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.Add(badge);
        row.Add(UiKit.Spacer(0, 16));

        var project = new MenuButton { Text = "Проект", Flat = false };
        var pm = project.GetPopup();
        pm.AddItem("Список проектов", 0);
        pm.AddItem("Сохранить  (Ctrl+S)", 1);
        pm.AddItem("Проверить историю", 2);
        pm.AddSeparator();
        pm.AddItem("Открыть папку истории", 3);
        pm.AddItem("Открыть папку stories", 4);
        pm.AddSeparator();
        pm.AddItem("Экспорт в отдельную игру…", 5);
        pm.IdPressed += OnProjectMenu;
        row.Add(project);

        var run = new MenuButton { Text = "Запуск", Flat = false };
        var rm = run.GetPopup();
        rm.AddItem("▶  С начала", 0);
        rm.AddItem("▶  С выбранной метки", 1);
        rm.AddItem("▶  С метки под курсором в тексте", 2);
        rm.IdPressed += OnRunMenu;
        row.Add(run);

        row.Add(UiKit.Expander());
        _storyTitle = row.Add(UiKit.Label("", Fonts.BodyBold, 18, Palette.EdText));
        _storyTitle.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

        // Простой режим — граф и таймлайн; продвинутый — ещё текст и список ошибок (ответ 61).
        _modeButton = row.Add(new Button { Text = "Режим: продвинутый" });
        _modeButton.Pressed += () =>
        {
            _advanced = !_advanced;
            ApplyMode();
        };
        return bar;
    }

    private void ApplyMode()
    {
        _modeButton.Text = _advanced ? "Режим: продвинутый" : "Режим: простой";
        if (_tabs == null) return;
        _tabs.SetTabHidden(2, !_advanced);
        _diagnostics.GetParent<Control>().Visible = _advanced;
        if (!_advanced && _tabs.CurrentTab == 2) _tabs.CurrentTab = 0;
    }

    // =================================================================== экран проектов

    /// <summary>Список историй: встроенный основной сюжет (только чтение) и истории фанатов из папки stories.</summary>
    private void ShowProjects()
    {
        ClearHost();
        _storyTitle.Text = "";
        _status.Text = "Папка историй: " + GicPaths.StoriesDir;

        var center = UiKit.Full(new CenterContainer());
        _host.AddChild(center);
        var panel = UiKit.Panel(UiKit.Box(Palette.EdPanel, Palette.EdLine, 1, 8, 32, 28));
        panel.CustomMinimumSize = new Vector2(860, 0);
        center.AddChild(panel);
        var box = UiKit.VBox(14);
        panel.AddChild(box);

        box.Add(UiKit.Label("Проекты GiC_OS", Fonts.BodyBold, 30, Colors.White));
        box.Add(UiKit.Paragraph("Своя история в виртуальной ОС Goliathchild: рабочий стол, документы, архивы с паролями, " +
                                "ветвления и концовки. Встроенные фоны, иконки и звуки игры можно использовать как готовые кубики.",
            Fonts.Body, 16, Palette.EdMuted));

        var list = new ItemList { CustomMinimumSize = new Vector2(0, 300), SelectMode = ItemList.SelectModeEnum.Single };
        box.Add(list);
        var roots = new List<string> { GameManager.MainStoryRoot };
        list.AddItem("★  Goliathchild — основной сюжет (только чтение)");
        foreach (var dir in GicPaths.ListDirs(GicPaths.StoriesDir))
        {
            var meta = StoryProject.LoadMeta(dir);
            roots.Add(dir);
            list.AddItem($"✎  {meta.Title}   ·   {dir.GetFile()}");
        }
        list.Select(roots.Count > 1 ? 1 : 0);
        list.ItemActivated += i => OpenProject(roots[(int)i]);

        var buttons = UiKit.HBox(10);
        box.Add(buttons);
        buttons.Add(new Button { Text = "Открыть" }).OnPressed(() =>
        {
            var selected = list.GetSelectedItems();
            if (selected.Length > 0) OpenProject(roots[selected[0]]);
        });

        box.Add(UiKit.Spacer(8));
        box.Add(UiKit.Label("Новая история", Fonts.BodyBold, 20, Colors.White));
        var createRow = UiKit.HBox(10);
        box.Add(createRow);
        var name = createRow.Add(new LineEdit { PlaceholderText = "Название истории", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        createRow.Add(new Button { Text = "Создать" }).OnPressed(() => CreateProject(name.Text));
        name.TextSubmitted += CreateProject;
    }

    /// <summary>Создать историю из шаблона: папка в stories, story.json и main.gics с примером.</summary>
    private void CreateProject(string title)
    {
        title = string.IsNullOrWhiteSpace(title) ? "Моя история" : title.Trim();
        var id = MakeId(title);
        var root = GicPaths.StoriesDir.PathJoin(id);
        var n = 2;
        while (DirAccess.DirExistsAbsolute(root)) root = GicPaths.StoriesDir.PathJoin($"{id}_{n++}");
        GicPaths.EnsureDir(root);
        GicPaths.EnsureDir(root.PathJoin("assets"));

        StoryProject.SaveMeta(root, new StoryMeta
        {
            Id = root.GetFile(),
            Title = title,
            Author = "",
            Description = "История, созданная в GiC_OS.",
            Chapters = new List<ChapterInfo> { new() { Id = "start", Title = "Начало", Label = "start" } },
        });
        GicPaths.WriteText(root.PathJoin("main.gics"), Template);
        OpenProject(root);
    }

    /// <summary>Имя папки из названия: латиница, цифры и подчёркивания (кириллица транслитерируется).</summary>
    private static string MakeId(string title)
    {
        const string ru = "абвгдеёжзийклмнопрстуфхцчшщъыьэюя";
        string[] lat = { "a", "b", "v", "g", "d", "e", "e", "zh", "z", "i", "y", "k", "l", "m", "n", "o", "p", "r", "s", "t", "u", "f", "h", "c", "ch", "sh", "sch", "", "y", "", "e", "yu", "ya" };
        var sb = new StringBuilder();
        foreach (var c in title.ToLowerInvariant())
        {
            var i = ru.IndexOf(c);
            if (i >= 0) sb.Append(lat[i]);
            else if (char.IsAsciiLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
        }
        var id = sb.ToString().Trim('_');
        return id.Length == 0 ? "story" : id;
    }

    // Шаблон новой истории: рабочий стол, документ, выбор и окно картинки — сразу видно, как всё устроено.
    private const string Template = """
# Моя история для GiC_OS.
# Строки с # — комментарии. Подробности о формате: docs/GICS_FORMAT.md
# Встроенные ассеты игры доступны по путям вида "old2023/images/...", свои кладите в папку assets этой истории.

character me "[doc]" color=#2c99a3

var opened = false

label start:
    scene "old2023/images/os/bg desktop.png"
    os desktop show
    os icon note "readme.txt" image="old2023/images/os/icons/txt.png" side=left action=readme
    os icon box "secret.zip" image="old2023/images/os/icons/act1Z.png" side=right action=secret
    jump wait

label wait:
    os wait

label readme:
    doc "readme.txt"
    glitch 0.2 0.15
    me "Привет! Это моя первая история в GiC_OS.{p=0.4}Пароль от архива — «не бойтесь»."
    jump wait

label secret:
    if opened:
        jump inside
    input answer "Введите пароль"
    if normalize(answer) == "не бойтесь":
        set opened = true
        jump inside
    "Неверный пароль!"
    jump wait

label inside:
    viewer "old2023/images/illustrations/1.png" wait
    menu "Что дальше?":
        "Вернуться на рабочий стол":
            jump wait
        "Закончить историю":
            end
""";

    // =================================================================== открытие истории

    /// <summary>Открыть историю из папки (основной сюжет — только для чтения).</summary>
    public void OpenProject(string root)
    {
        _root = root;
        _meta = StoryProject.LoadMeta(root);
        _readOnly = root.StartsWith("res://") || _meta.ReadOnly;
        _texts.Clear();
        _dirty.Clear();
        foreach (var path in GicPaths.ListFiles(root, "gics")) _texts[path] = GicPaths.ReadText(path) ?? "";
        _selectedLabel = null;
        BuildWorkspace();
        Reparse();
        _storyTitle.Text = _meta.Title + (_readOnly ? "   [только чтение]" : "");
        if (_project.Labels.ContainsKey(_meta.Start)) SelectLabel(_meta.Start);
    }

    /// <summary>Переключить вкладку рабочей области: 0 — граф, 1 — таймлайн, 2 — текст.</summary>
    public void ShowTab(int index)
    {
        if (_tabs != null) _tabs.CurrentTab = Mathf.Clamp(index, 0, _tabs.GetTabCount() - 1);
    }

    private void BuildWorkspace()
    {
        ClearHost();
        var split = UiKit.Full(new HSplitContainer());
        _host.AddChild(split);

        // Слева — дерево: файлы и метки, персонажи.
        _tree = new Tree { CustomMinimumSize = new Vector2(280, 0), HideRoot = true };
        _tree.ItemSelected += OnTreeSelected;
        split.Add(_tree);

        var right = new HSplitContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        split.Add(right);

        var centerSplit = new VSplitContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        right.Add(centerSplit);

        _tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        centerSplit.Add(_tabs);

        _graph = new EditorGraph { Name = "Граф" };
        _graph.LabelSelected += SelectLabel;
        _graph.LabelActivated += label =>
        {
            SelectLabel(label);
            ShowLabelInText(label);
        };
        _tabs.Add(_graph);

        var timelineScroll = new ScrollContainer { Name = "Таймлайн", VerticalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _timeline = UiKit.HBox(8);
        _timeline.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        timelineScroll.Add(UiKit.Margin(12, 12, 12, 12)).Add(_timeline);
        _tabs.Add(timelineScroll);

        var textTab = UiKit.VBox(6);
        textTab.Name = "Текст";
        var textBar = UiKit.HBox(8);
        textTab.Add(textBar);
        _fileSelect = textBar.Add(new OptionButton { CustomMinimumSize = new Vector2(320, 0) });
        _fileSelect.ItemSelected += i => OpenFile(_fileSelect.GetItemMetadata((int)i).AsString());
        textBar.Add(new Button { Text = "Сохранить" }).OnPressed(SaveAll);
        textBar.Add(new Button { Text = "+ Файл" }).OnPressed(AddFile);
        textBar.Add(UiKit.Expander());
        textBar.Add(UiKit.Label("Ctrl+S — сохранить, F5 — запустить с метки под курсором", Fonts.Body, 14, Palette.EdMuted));
        _code = new CodeEdit
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            GuttersDrawLineNumbers = true,
            HighlightCurrentLine = true,
            IndentUseSpaces = true,
            IndentSize = 4,
            AutoBraceCompletionEnabled = true,
            SyntaxHighlighter = EditorStyle.Highlighter(),
        };
        _code.TextChanged += OnCodeChanged;
        textTab.Add(_code);
        _tabs.Add(textTab);

        // Снизу — проверка истории: ошибки и предупреждения, двойной клик ведёт к строке.
        var diagPanel = UiKit.VBox(4);
        diagPanel.CustomMinimumSize = new Vector2(0, 150);
        diagPanel.Add(UiKit.Label("Проверка истории", Fonts.BodyBold, 15, Palette.EdMuted));
        _diagnostics = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _diagnostics.ItemActivated += OnDiagnosticActivated;
        diagPanel.Add(_diagnostics);
        centerSplit.Add(diagPanel);

        // Справа — инспектор выбранной метки или команды.
        var inspectorScroll = new ScrollContainer { CustomMinimumSize = new Vector2(320, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _inspector = UiKit.VBox(8);
        _inspector.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        inspectorScroll.Add(UiKit.Margin(12, 10, 12, 10)).Add(_inspector);
        right.Add(inspectorScroll);

        ApplyMode();
    }

    private void ClearHost()
    {
        foreach (var child in _host.GetChildren()) child.QueueFree();
        _tabs = null;
    }

    // =================================================================== разбор и обновление

    /// <summary>Перечитать историю из текстов в памяти и обновить граф, дерево, ошибки и инспектор.</summary>
    private void Reparse()
    {
        if (_root == null) return;
        var files = new List<(string, string)>();
        foreach (var kv in _texts) files.Add((kv.Key, kv.Value));
        _project = StoryProject.FromSources(_root, _meta, files);

        _graph.Build(_project);
        RebuildTree();
        RebuildDiagnostics();
        RebuildFileSelect();
        if (_selectedLabel != null && _project.Labels.ContainsKey(_selectedLabel)) SelectLabel(_selectedLabel);
        UpdateStatus();
    }

    private void RebuildTree()
    {
        _tree.Clear();
        var root = _tree.CreateItem();
        var files = _tree.CreateItem(root);
        files.SetText(0, "Сценарий");
        files.SetSelectable(0, false);
        foreach (var doc in _project.Documents)
        {
            var file = _tree.CreateItem(files);
            file.SetText(0, "📄 " + doc.Path.GetFile() + (_dirty.Contains(doc.Path) ? " *" : ""));
            file.SetMetadata(0, "file:" + doc.Path);
            foreach (var label in doc.Labels)
            {
                var item = _tree.CreateItem(file);
                item.SetText(0, "◆ " + label.Name);
                item.SetMetadata(0, "label:" + label.Name);
            }
        }
        var chars = _tree.CreateItem(root);
        chars.SetText(0, $"Персонажи ({_project.Characters.Count})");
        chars.SetSelectable(0, false);
        chars.Collapsed = true;
        foreach (var ch in _project.Characters.Values)
        {
            var item = _tree.CreateItem(chars);
            item.SetText(0, $"☺ {ch.Id} — {ch.Name}");
            item.SetMetadata(0, "char:" + ch.Id);
        }
        var vars = _tree.CreateItem(root);
        vars.SetText(0, $"Переменные ({_project.Variables.Count})");
        vars.SetSelectable(0, false);
        vars.Collapsed = true;
        foreach (var v in _project.Variables)
        {
            var item = _tree.CreateItem(vars);
            item.SetText(0, $"{v.Name} = {v.Expression}");
            item.SetMetadata(0, $"line:{v.File}|{v.Line}");
        }
    }

    private void RebuildDiagnostics()
    {
        _diagnostics.Clear();
        foreach (var d in _project.Diagnostics)
        {
            var i = _diagnostics.AddItem(d.ToString());
            _diagnostics.SetItemMetadata(i, $"{d.File}|{d.Line}");
            _diagnostics.SetItemCustomFgColor(i, d.Severity switch
            {
                DiagnosticSeverity.Error => new Color("ff6b6b"),
                DiagnosticSeverity.Warning => new Color("ffd166"),
                _ => Palette.EdMuted,
            });
        }
        if (_project.Diagnostics.Count == 0) _diagnostics.AddItem("✔ Ошибок не найдено");
    }

    private void RebuildFileSelect()
    {
        _fileSelect.Clear();
        var i = 0;
        foreach (var path in _texts.Keys)
        {
            _fileSelect.AddItem(path.GetFile() + (_dirty.Contains(path) ? " *" : ""));
            _fileSelect.SetItemMetadata(i, path);
            if (path == _currentFile) _fileSelect.Selected = i;
            i++;
        }
        if (_currentFile == null && _texts.Count > 0)
        {
            foreach (var path in _texts.Keys)
            {
                OpenFile(path);
                break;
            }
        }
    }

    private void UpdateStatus()
    {
        var errors = 0;
        var warnings = 0;
        foreach (var d in _project.Diagnostics)
        {
            if (d.Severity == DiagnosticSeverity.Error) errors++;
            else if (d.Severity == DiagnosticSeverity.Warning) warnings++;
        }
        _status.Text = $"{_root}   ·   меток: {_project.Labels.Count}   ·   персонажей: {_project.Characters.Count}   ·   " +
                       $"ошибок: {errors}, предупреждений: {warnings}" + (_dirty.Count > 0 ? $"   ·   не сохранено файлов: {_dirty.Count}" : "");
    }

    // =================================================================== выбор и навигация

    private void SelectLabel(string name)
    {
        if (_project == null || !_project.Labels.TryGetValue(name, out var label)) return;
        _selectedLabel = name;
        BuildTimeline(label);
        ShowLabelInspector(label);
    }

    private void OnTreeSelected()
    {
        var meta = _tree.GetSelected()?.GetMetadata(0).AsString() ?? "";
        if (meta.StartsWith("label:"))
        {
            var name = meta.Substring(6);
            SelectLabel(name);
            _graph.Focus(name);
            if (_tabs.CurrentTab == 2) ShowLabelInText(name);
        }
        else if (meta.StartsWith("file:"))
        {
            OpenFile(meta.Substring(5));
            if (_advanced) _tabs.CurrentTab = 2;
        }
        else if (meta.StartsWith("char:") && _project.Characters.TryGetValue(meta.Substring(5), out var ch))
        {
            ShowCharacterInspector(ch);
        }
        else if (meta.StartsWith("line:"))
        {
            var parts = meta.Substring(5).Split('|');
            GoToLine(parts[0], int.Parse(parts[1]));
        }
    }

    private void OnDiagnosticActivated(long index)
    {
        var meta = _diagnostics.GetItemMetadata((int)index).AsString();
        if (string.IsNullOrEmpty(meta)) return;
        var parts = meta.Split('|');
        GoToLine(parts[0], int.Parse(parts[1]));
    }

    private void ShowLabelInText(string name)
    {
        if (_project.Labels.TryGetValue(name, out var label)) GoToLine(label.File, label.Line);
    }

    /// <summary>Открыть файл в текстовой вкладке и поставить курсор на строку.</summary>
    private void GoToLine(string file, int line)
    {
        if (!_texts.ContainsKey(file)) return;
        _advanced = true;
        ApplyMode();
        OpenFile(file);
        _tabs.CurrentTab = 2;
        _code.SetCaretLine(Mathf.Max(line - 1, 0));
        _code.SetCaretColumn(0);
        _code.CenterViewportToCaret();
        _code.GrabFocus();
    }

    private void OpenFile(string path)
    {
        if (path == null || !_texts.ContainsKey(path)) return;
        if (_currentFile != null && _currentFile != path) _texts[_currentFile] = _code.Text;
        _currentFile = path;
        _suppressTextEvent = true;
        _code.Text = _texts[path];
        _code.Editable = !_readOnly;
        _suppressTextEvent = false;
        for (var i = 0; i < _fileSelect.ItemCount; i++)
        {
            if (_fileSelect.GetItemMetadata(i).AsString() == path) _fileSelect.Selected = i;
        }
    }

    private void OnCodeChanged()
    {
        if (_suppressTextEvent || _currentFile == null) return;
        _texts[_currentFile] = _code.Text;
        _dirty.Add(_currentFile);
        // Перечитываем не на каждую букву, а через полсекунды после остановки набора.
        _reparse.Start();
    }

    // =================================================================== таймлайн и инспектор

    /// <summary>Таймлайн метки: команды по порядку цветными блоками. Клик по блоку — подробности и строка в тексте.</summary>
    private void BuildTimeline(LabelDef label)
    {
        foreach (var child in _timeline.GetChildren()) child.QueueFree();
        var head = UiKit.Panel(UiKit.Box(new Color("2e7d32"), default, 0, 6, 12, 10));
        head.CustomMinimumSize = new Vector2(150, 0);
        head.Add(UiKit.Label("◆ " + label.Name, Fonts.BodyBold, 16, Colors.White));
        _timeline.Add(head);
        foreach (var cmd in label.Body) _timeline.Add(TimelineBlock(cmd));
    }

    private Control TimelineBlock(Command cmd)
    {
        var color = EditorStyle.KindColor(cmd.Kind);
        var normal = UiKit.Box(color.Darkened(0.35f), color, 2, 6, 10, 8);
        var hover = UiKit.Box(color.Darkened(0.15f), Colors.White, 2, 6, 10, 8);
        var button = UiKit.Button("", Fonts.Body, 14, Palette.EdText, Colors.White, normal, hover);
        button.CustomMinimumSize = new Vector2(230, 150);
        var box = UiKit.VBox(4);
        box.MouseFilter = Control.MouseFilterEnum.Ignore;
        button.Add(UiKit.Margin(10, 8, 10, 8)).Add(box);
        button.GetChild<Control>(0).MouseFilter = Control.MouseFilterEnum.Ignore;
        box.Add(UiKit.Label(cmd.Kind.ToUpperInvariant(), Fonts.MenuBold, 14, Colors.White)).MouseFilter = Control.MouseFilterEnum.Ignore;
        var preview = UiKit.Paragraph(Describe(cmd), Fonts.Body, 14, Palette.EdText);
        preview.MouseFilter = Control.MouseFilterEnum.Ignore;
        preview.MaxLinesVisible = 5;
        preview.CustomMinimumSize = new Vector2(205, 0);
        box.Add(preview);
        button.Pressed += () =>
        {
            ShowCommandInspector(cmd);
            if (_advanced) GoToLine(cmd.File, cmd.Line);
        };
        return button;
    }

    /// <summary>Короткое описание команды для таймлайна и инспектора.</summary>
    private static string Describe(Command cmd) => cmd switch
    {
        SayCommand s => (s.Speaker != null ? s.Speaker + ": " : "") + GicEngine.Runtime.TextMarkup.Parse(s.Text, null).Plain.Replace('\n', ' '),
        DocCommand d => d.Title,
        SceneCommand sc => sc.Path ?? "(нет фона)",
        ShowCommand sh => $"{sh.Tag}: {sh.Path}",
        HideCommand h => h.Tag,
        ViewerCommand v => v.Hide ? "закрыть окно" : v.Path + (v.Wait ? " (ждать)" : ""),
        AudioCommand a => $"{a.Path} · {a.Volume:0.##}",
        StopCommand st => st.Channel,
        PauseCommand p => p.Seconds < 0 ? "до клика" : $"{p.Seconds} с",
        EffectCommand e => $"{e.Strength} · {e.Time} с",
        JumpCommand j => "→ " + j.Target,
        SetCommand set => $"{set.Variable} = {set.Expression}",
        IfCommand i => string.Join(" / ", i.Branches.ConvertAll(b => b.Condition)) + (i.Else != null ? " / иначе" : ""),
        MenuCommand m => (m.Prompt ?? "") + "\n" + string.Join("\n", m.Options.ConvertAll(o => "• " + o.Text)),
        InputCommand inp => $"{inp.Variable} ← «{inp.Prompt}»",
        OsCommand os => os.Action + " " + string.Join(" ", os.Args),
        CheckpointCommand cp => cp.Title ?? cp.Name,
        NotifyCommand n => n.Text,
        _ => "",
    };

    private void ShowLabelInspector(LabelDef label)
    {
        ClearInspector();
        InspectorTitle("Метка", label.Name);
        InspectorRow("Файл", $"{label.File.GetFile()}:{label.Line}");
        InspectorRow("Команд", label.Code.Count.ToString());
        var outgoing = new List<string>();
        foreach (var instr in label.Code)
        {
            if (instr.Command is JumpCommand j) outgoing.Add((j.IsCall ? "call " : "jump ") + j.Target);
            if (instr.Command is OsCommand os && os.Opt("action") != null) outgoing.Add("клик → " + os.Opt("action"));
        }
        if (label.FallThrough != null && !EditorGraph.EndsHard(label)) outgoing.Add("дальше → " + label.FallThrough);
        InspectorRow("Переходы", outgoing.Count > 0 ? string.Join("\n", outgoing) : "—");
        var incoming = new List<string>();
        foreach (var other in _project.Labels.Values)
        {
            foreach (var instr in other.Code)
            {
                if (instr.Command is JumpCommand j && j.Target == label.Name) incoming.Add(other.Name);
                else if (instr.Command is OsCommand os && os.Opt("action") == label.Name) incoming.Add(other.Name + " (клик)");
            }
        }
        InspectorRow("Сюда ведут", incoming.Count > 0 ? string.Join("\n", incoming) : "—");
        _inspector.Add(UiKit.Spacer(6));
        var name = label.Name;
        _inspector.Add(new Button { Text = "▶ Запустить отсюда" }).OnPressed(() => RunFrom(name));
        _inspector.Add(new Button { Text = "Открыть в тексте" }).OnPressed(() => ShowLabelInText(name));
    }

    private void ShowCommandInspector(Command cmd)
    {
        ClearInspector();
        InspectorTitle("Команда", cmd.Kind);
        InspectorRow("Строка", $"{cmd.File.GetFile()}:{cmd.Line}");
        InspectorRow("Содержимое", Describe(cmd));
        if (cmd is SayCommand s && s.Speaker != null && _project.Characters.TryGetValue(s.Speaker, out var ch))
        {
            InspectorRow("Персонаж", $"{ch.Id} — {ch.Name}");
            if (ch.Voice != null) InspectorRow("Голос", ch.Voice);
        }
        _inspector.Add(new Button { Text = "Открыть в тексте" }).OnPressed(() => GoToLine(cmd.File, cmd.Line));
    }

    private void ShowCharacterInspector(CharacterDef ch)
    {
        ClearInspector();
        InspectorTitle("Персонаж", ch.Id);
        InspectorRow("Имя", ch.Name);
        InspectorRow("Цвет", ch.Color);
        InspectorRow("Голос", ch.Voice ?? "—");
        InspectorRow("Спрайты", ch.Sprites.Count > 0 ? string.Join("\n", ch.Sprites.Keys) : "—");
        if (ch.Info != null) InspectorRow("Заметки", ch.Info);
        _inspector.Add(new Button { Text = "Открыть в тексте" }).OnPressed(() => GoToLine(ch.File, ch.Line));
    }

    private void ClearInspector()
    {
        foreach (var child in _inspector.GetChildren()) child.QueueFree();
    }

    private void InspectorTitle(string kind, string name)
    {
        _inspector.Add(UiKit.Label(kind.ToUpperInvariant(), Fonts.MenuBold, 13, Palette.EdMuted));
        _inspector.Add(UiKit.Label(name, Fonts.BodyBold, 22, Colors.White));
        _inspector.Add(new ColorRect { Color = Palette.EdLine, CustomMinimumSize = new Vector2(0, 1) });
    }

    private void InspectorRow(string key, string value)
    {
        _inspector.Add(UiKit.Label(key, Fonts.Body, 14, Palette.EdMuted));
        _inspector.Add(UiKit.Paragraph(value, Fonts.Body, 16, Palette.EdText));
    }

    // =================================================================== команды меню

    private void OnProjectMenu(long id)
    {
        switch (id)
        {
            case 0:
                ShowProjects();
                break;
            case 1:
                SaveAll();
                break;
            case 2:
                Reparse();
                if (_advanced && _diagnostics != null) _diagnostics.GrabFocus();
                break;
            case 3:
                if (_root != null) OS.ShellOpen(_root.StartsWith("res://") ? ProjectSettings.GlobalizePath(_root) : _root);
                break;
            case 4:
                OS.ShellOpen(GicPaths.StoriesDir);
                break;
            case 5:
                Notify("Экспорт в отдельную игру появится в следующих версиях (нужны шаблоны экспорта Godot).");
                break;
        }
    }

    private void OnRunMenu(long id)
    {
        if (_project == null) return;
        switch (id)
        {
            case 0:
                RunFrom(null);
                break;
            case 1:
                RunFrom(_selectedLabel);
                break;
            case 2:
                RunFromCaret();
                break;
        }
    }

    private void RunFromCaret()
    {
        if (_currentFile == null) return;
        var label = _project.FindLabelAt(_currentFile, _code.GetCaretLine() + 1);
        RunFrom(label?.Name);
    }

    /// <summary>Открыть предпросмотр истории в отдельном окне (с несохранённым текстом).</summary>
    public PreviewWindow RunFrom(string label)
    {
        if (_currentFile != null) _texts[_currentFile] = _code.Text;
        Reparse();
        var preview = new PreviewWindow(_project, label);
        GetTree().Root.AddChild(preview);
        preview.PopupCentered();
        return preview;
    }

    private void SaveAll()
    {
        if (_readOnly)
        {
            Notify("Основной сюжет открыт только для чтения. Создайте свою историю на экране проектов.");
            return;
        }
        if (_currentFile != null) _texts[_currentFile] = _code.Text;
        foreach (var path in _dirty) GicPaths.WriteText(path, _texts[path]);
        _dirty.Clear();
        Reparse();
        Notify("Сохранено");
    }

    private void AddFile()
    {
        if (_readOnly || _root == null) return;
        var n = 1;
        string path;
        do path = _root.PathJoin($"scene_{n++}.gics");
        while (_texts.ContainsKey(path) || FileAccess.FileExists(path));
        _texts[path] = $"# Новый файл сценария\n\nlabel scene_{n - 1}:\n    \"...\"\n    return\n";
        _dirty.Add(path);
        Reparse();
        OpenFile(path);
    }

    private void Notify(string text)
    {
        _status.Text = text;
        GetTree().CreateTimer(4).Timeout += UpdateStatusSafe;
    }

    private void UpdateStatusSafe()
    {
        if (IsInstanceValid(this) && _project != null) UpdateStatus();
    }

    // =================================================================== окно

    private void OnWindowInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } key) return;
        if (key.CtrlPressed && key.Keycode == Key.S)
        {
            SaveAll();
            SetInputAsHandled();
        }
        else if (key.Keycode == Key.F5)
        {
            RunFromCaret();
            SetInputAsHandled();
        }
    }

    /// <summary>Закрытие окна: несохранённые изменения не теряем — сохраняем свою историю автоматически.</summary>
    private void OnCloseRequested()
    {
        if (!_readOnly && _dirty.Count > 0) SaveAll();
        QueueFree();
    }
}
