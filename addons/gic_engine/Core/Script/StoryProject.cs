using System.Collections.Generic;
using System.Text.Json;
using GicEngine.IO;
using Godot;

namespace GicEngine.Script;

/// <summary>Глава истории для меню «История → Выбор главы».</summary>
public sealed class ChapterInfo
{
    public string Id { get; set; }
    public string Title { get; set; }
    public string Label { get; set; }
}

/// <summary>Описание истории из файла story.json.</summary>
public sealed class StoryMeta
{
    public string Id { get; set; } = "story";
    public string Title { get; set; } = "Без названия";
    public string Author { get; set; } = "";
    public string Version { get; set; } = "0.1";
    public string Description { get; set; } = "";
    // Возрастная маркировка истории (ответ 78).
    public string Rating { get; set; } = "0+";
    // Метка, с которой начинается история.
    public string Start { get; set; } = "start";
    public string Language { get; set; } = "ru";
    public List<string> Languages { get; set; } = new() { "ru" };
    // Подача: "os" — рабочий стол виртуальной ОС, "classic" — классическая ВН (фон + спрайты + текстбокс).
    public string Presenter { get; set; } = "os";
    public List<ChapterInfo> Chapters { get; set; } = new();
    // Встроенный основной сюжет открывается в редакторе только для чтения (ответ 81).
    public bool ReadOnly { get; set; }
}

/// <summary>
/// История целиком: все файлы .gics из папки, персонажи, переменные и скомпилированные метки.
/// Используется и игрой (основной сюжет вшит в res://scenarios), и редактором GiC_OS (истории фанатов в stories/).
/// </summary>
public sealed class StoryProject
{
    /// <summary>Папка истории: res://scenarios/goliathchild или путь в stories/.</summary>
    public string Root;
    public StoryMeta Meta = new();
    public readonly List<ScriptDocument> Documents = new();
    public readonly Dictionary<string, LabelDef> Labels = new();
    // Встроенный «centered» есть всегда, поэтому его можно использовать без объявления.
    public readonly Dictionary<string, CharacterDef> Characters = new() { [CharacterDef.CenteredId] = CharacterDef.Centered() };
    public readonly List<VarDef> Variables = new();
    public readonly List<Diagnostic> Diagnostics = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    /// <summary>Загружает историю из папки: story.json и все файлы .gics внутри (включая подпапки).</summary>
    public static StoryProject Load(string root)
    {
        var meta = LoadMeta(root);
        var files = new List<(string, string)>();
        foreach (var path in GicPaths.ListFiles(root, "gics"))
        {
            files.Add((path, GicPaths.ReadText(path) ?? ""));
        }
        return FromSources(root, meta, files);
    }

    /// <summary>
    /// Собирает историю из уже прочитанных текстов. Редактор вызывает это при каждом изменении текста,
    /// чтобы граф и список ошибок обновлялись без сохранения файла.
    /// </summary>
    public static StoryProject FromSources(string root, StoryMeta meta, IEnumerable<(string path, string text)> files)
    {
        var project = new StoryProject { Root = root, Meta = meta ?? new StoryMeta() };
        foreach (var (path, text) in files)
        {
            var doc = GicsParser.Parse(path, text);
            project.Documents.Add(doc);
            project.Diagnostics.AddRange(doc.Diagnostics);
        }
        project.Link();
        return project;
    }

    /// <summary>Читает story.json. Если файла нет — история всё равно открывается с настройками по умолчанию.</summary>
    public static StoryMeta LoadMeta(string root)
    {
        var text = GicPaths.ReadText(root.PathJoin("story.json"));
        if (string.IsNullOrEmpty(text)) return new StoryMeta { Id = root.GetFile(), Title = root.GetFile() };
        try
        {
            return JsonSerializer.Deserialize<StoryMeta>(text, JsonOptions) ?? new StoryMeta();
        }
        catch (JsonException e)
        {
            GD.PushWarning($"story.json в {root} не прочитан: {e.Message}");
            return new StoryMeta { Id = root.GetFile(), Title = root.GetFile() };
        }
    }

    /// <summary>Сохраняет story.json (используется редактором при создании и изменении истории).</summary>
    public static bool SaveMeta(string root, StoryMeta meta) =>
        GicPaths.WriteText(root.PathJoin("story.json"), JsonSerializer.Serialize(meta, JsonOptions));

    /// <summary>Связывает документы в одну историю: собирает метки и персонажей, компилирует код, ищет ошибки.</summary>
    private void Link()
    {
        foreach (var doc in Documents)
        {
            for (var i = 0; i < doc.Labels.Count; i++)
            {
                var label = doc.Labels[i];
                // Провал выполнения в следующую метку того же файла — как в Ren'Py.
                label.FallThrough = i + 1 < doc.Labels.Count ? doc.Labels[i + 1].Name : null;
                if (Labels.TryGetValue(label.Name, out var existing))
                {
                    Warn(DiagnosticSeverity.Error, label.File, label.Line,
                        $"Метка «{label.Name}» уже есть в {existing.File.GetFile()}:{existing.Line}");
                    continue;
                }
                Compiler.Compile(label);
                Labels[label.Name] = label;
            }
            foreach (var ch in doc.Characters)
            {
                // Замена встроенного centered своим объявлением — не повтор, предупреждать не о чем.
                if (Characters.TryGetValue(ch.Id, out var known) && known.Line > 0)
                {
                    Warn(DiagnosticSeverity.Warning, ch.File, ch.Line, $"Персонаж «{ch.Id}» объявлен повторно");
                }
                Characters[ch.Id] = ch;
            }
            Variables.AddRange(doc.Variables);
        }
        Validate();
    }

    /// <summary>Проверка истории целиком: переходы на несуществующие метки, неизвестные персонажи, пропавшие файлы.</summary>
    private void Validate()
    {
        if (Labels.Count > 0 && !Labels.ContainsKey(Meta.Start))
        {
            Warn(DiagnosticSeverity.Error, Root.PathJoin("story.json"), 1, $"Нет стартовой метки «{Meta.Start}»");
        }
        foreach (var chapter in Meta.Chapters)
        {
            if (!Labels.ContainsKey(chapter.Label))
            {
                Warn(DiagnosticSeverity.Warning, Root.PathJoin("story.json"), 1, $"Глава «{chapter.Title}» ссылается на несуществующую метку «{chapter.Label}»");
            }
        }

        foreach (var label in Labels.Values)
        {
            foreach (var instr in label.Code)
            {
                if (instr.Op != OpCode.Exec) continue;
                switch (instr.Command)
                {
                    case JumpCommand j when j.Target != null && !Labels.ContainsKey(j.Target):
                        Warn(DiagnosticSeverity.Error, j.File, j.Line, $"Метка «{j.Target}» не найдена");
                        break;
                    case SayCommand s when s.Speaker != null && !Characters.ContainsKey(s.Speaker):
                        Warn(DiagnosticSeverity.Error, s.File, s.Line, $"Персонаж «{s.Speaker}» не объявлен (character {s.Speaker} \"Имя\")");
                        break;
                    case SceneCommand sc when sc.Path != null:
                        CheckAsset(sc.Path, sc);
                        break;
                    case ShowCommand sh when sh.Path != null:
                        CheckAsset(sh.Path, sh);
                        break;
                    case ViewerCommand v when v.Path != null:
                        CheckAsset(v.Path, v);
                        break;
                    case AudioCommand a when a.Path != null:
                        CheckAsset(a.Path, a);
                        break;
                    case OsCommand os:
                        foreach (var key in new[] { "image", "error", "wallpaper" })
                        {
                            var p = os.Opt(key);
                            if (p != null) CheckAsset(p, os);
                        }
                        var action = os.Opt("action");
                        if (action != null && !Labels.ContainsKey(action))
                        {
                            Warn(DiagnosticSeverity.Error, os.File, os.Line, $"Действие иконки ведёт на несуществующую метку «{action}»");
                        }
                        break;
                }
            }
        }
    }

    private void CheckAsset(string path, Command cmd)
    {
        // Цвет вместо файла (scene "#ffffff") проверять на диске не нужно.
        if (CharacterDef.IsColorPath(path)) return;
        if (!AssetExists(path)) Warn(DiagnosticSeverity.Warning, cmd.File, cmd.Line, $"Файл не найден: {path}");
    }

    private void Warn(DiagnosticSeverity severity, string file, int line, string message) =>
        Diagnostics.Add(new Diagnostic(severity, file, line, message));

    // ---------------------------------------------------------------- ассеты

    /// <summary>
    /// Превращает путь из сценария в настоящий путь к файлу.
    /// Сначала ищем в папке assets самой истории, потом во встроенных ассетах игры (res://assets).
    /// Так фанаты могут пользоваться готовыми фонами, иконками и звуками, как кубиками в редакторе Portal 2.
    /// </summary>
    public string ResolveAsset(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (path.StartsWith("res://") || path.StartsWith("user://") || System.IO.Path.IsPathRooted(path)) return path;

        var own = Root.PathJoin("assets").PathJoin(path);
        if (FileExists(own)) return own;
        return "res://assets/".PathJoin(path);
    }

    /// <summary>Есть ли файл ассета (с учётом поиска по папкам).</summary>
    public bool AssetExists(string path) => FileExists(ResolveAsset(path));

    /// <summary>
    /// Проверка существования файла. В собранной игре картинки и звуки из res:// лежат в импортированном виде,
    /// поэтому для них нужен ResourceLoader.Exists, а не FileAccess.FileExists.
    /// </summary>
    public static bool FileExists(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (path.StartsWith("res://")) return ResourceLoader.Exists(path) || FileAccess.FileExists(path);
        return FileAccess.FileExists(path);
    }

    /// <summary>Найти метку, в которой находится строка файла (для «Запустить отсюда» в редакторе).</summary>
    public LabelDef FindLabelAt(string file, int line)
    {
        LabelDef best = null;
        foreach (var label in Labels.Values)
        {
            if (label.File != file || label.Line > line) continue;
            if (best == null || label.Line > best.Line) best = label;
        }
        return best;
    }
}
