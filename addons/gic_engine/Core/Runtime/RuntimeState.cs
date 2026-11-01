using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace GicEngine.Runtime;

// Состояние исполнения истории. Из него строятся сохранения (.sdrom_save) и снимки для отката назад.
// Поэтому здесь только простые данные без ссылок на узлы Godot: всё легко копируется и пишется в JSON.

/// <summary>Кадр стека вызовов: куда вернуться после return.</summary>
public sealed class CallFrame
{
    public string Label;
    public int Ip;

    public CallFrame Clone() => new() { Label = Label, Ip = Ip };
}

/// <summary>Что играет на звуковом канале (только зацикленные звуки — разовые не восстанавливаем).</summary>
public sealed class AudioState
{
    public string Path;
    public float Volume = 1f;
    public bool Loop = true;

    public AudioState Clone() => new() { Path = Path, Volume = Volume, Loop = Loop };
}

/// <summary>Показанная картинка (show метка "путь").</summary>
public sealed class ImageState
{
    public string Tag;
    public string Path;
    public float X = 0.5f;
    public float Y = 0.5f;

    public ImageState Clone() => new() { Tag = Tag, Path = Path, X = X, Y = Y };
}

/// <summary>Иконка на рабочем столе виртуальной ОС.</summary>
public sealed class DesktopIcon
{
    public string Id;
    public string Title;
    public string Image;
    // left или right — колонка иконок, как в оригинале (слева система, справа архивы).
    public string Side = "left";
    // Метка, на которую переходит история по клику (только когда история ждёт действия игрока: os wait).
    public string Action;
    // Картинка окна ошибки, которое показывается по клику (My PC, Recycle Bin в оригинале).
    public string Error;
    public bool Hidden;

    public DesktopIcon Clone() => (DesktopIcon)MemberwiseClone();
}

/// <summary>Файл в окне папки.</summary>
public sealed class DesktopFile
{
    public string Id;
    public string Title;
    public string Image;
    public string Action;

    public DesktopFile Clone() => (DesktopFile)MemberwiseClone();
}

/// <summary>Папка (окно проводника) с файлами.</summary>
public sealed class DesktopFolder
{
    public string Id;
    public string Title;
    public List<DesktopFile> Files = new();

    public DesktopFolder Clone()
    {
        var f = new DesktopFolder { Id = Id, Title = Title };
        foreach (var file in Files) f.Files.Add(file.Clone());
        return f;
    }
}

/// <summary>Состояние рабочего стола виртуальной ОС.</summary>
public sealed class DesktopState
{
    public bool Visible;
    public string Wallpaper;
    public List<DesktopIcon> Icons = new();
    public List<DesktopFolder> Folders = new();
    // Id открытой папки или null.
    public string OpenFolder;
    // Текст часов на панели задач. Пусто — показывать «внутриигровое» время по умолчанию.
    public string Clock;

    public DesktopIcon FindIcon(string id) => Icons.Find(i => i.Id == id);
    public DesktopFolder FindFolder(string id) => Folders.Find(f => f.Id == id);

    public DesktopState Clone()
    {
        var d = new DesktopState { Visible = Visible, Wallpaper = Wallpaper, OpenFolder = OpenFolder, Clock = Clock };
        foreach (var i in Icons) d.Icons.Add(i.Clone());
        foreach (var f in Folders) d.Folders.Add(f.Clone());
        return d;
    }
}

/// <summary>Всё, что видно и слышно на экране. По нему презентер полностью перерисовывает сцену после загрузки или отката.</summary>
public sealed class PresentationState
{
    public string Scene;
    public List<ImageState> Images = new();
    public string Viewer;
    public Dictionary<string, AudioState> Audio = new();
    public DesktopState Desktop = new();

    public PresentationState Clone()
    {
        var p = new PresentationState { Scene = Scene, Viewer = Viewer, Desktop = Desktop.Clone() };
        foreach (var i in Images) p.Images.Add(i.Clone());
        foreach (var kv in Audio) p.Audio[kv.Key] = kv.Value.Clone();
        return p;
    }
}

/// <summary>Полное состояние прохождения.</summary>
public sealed class RuntimeState
{
    public string StoryId;
    public string Label;
    public int Ip;
    public List<CallFrame> CallStack = new();
    // Значения переменных: bool, long, double или string.
    public Dictionary<string, object> Variables = new();
    public HashSet<string> VisitedLabels = new();
    public PresentationState Presentation = new();
    public double PlayTime;
    // Название главы/документа для списка сохранений.
    public string ChapterTitle = "";
    // Последняя реплика — показывается в карточке сохранения.
    public string LastText = "";

    /// <summary>Глубокая копия: снимок для отката назад не должен меняться вместе с текущим состоянием.</summary>
    public RuntimeState Clone()
    {
        var s = new RuntimeState
        {
            StoryId = StoryId,
            Label = Label,
            Ip = Ip,
            Variables = new Dictionary<string, object>(Variables),
            VisitedLabels = new HashSet<string>(VisitedLabels),
            Presentation = Presentation.Clone(),
            PlayTime = PlayTime,
            ChapterTitle = ChapterTitle,
            LastText = LastText,
        };
        foreach (var f in CallStack) s.CallStack.Add(f.Clone());
        return s;
    }

    // ---------------------------------------------------------------- JSON

    /// <summary>Сериализация в JSON для файла сохранения. Пишем вручную, чтобы типы переменных не терялись.</summary>
    public JsonObject ToJson()
    {
        var vars = new JsonObject();
        foreach (var kv in Variables) vars[kv.Key] = ValueToJson(kv.Value);

        var stack = new JsonArray();
        foreach (var f in CallStack) stack.Add(new JsonObject { ["label"] = f.Label, ["ip"] = f.Ip });

        var visited = new JsonArray();
        foreach (var v in VisitedLabels) visited.Add(v);

        return new JsonObject
        {
            ["story"] = StoryId,
            ["label"] = Label,
            ["ip"] = Ip,
            ["stack"] = stack,
            ["vars"] = vars,
            ["visited"] = visited,
            ["presentation"] = PresentationToJson(Presentation),
            ["playtime"] = PlayTime,
            ["chapter"] = ChapterTitle,
            ["last_text"] = LastText,
        };
    }

    /// <summary>Восстановление из JSON. Отсутствующие поля получают значения по умолчанию — так старые сохранения читаются новыми версиями.</summary>
    public static RuntimeState FromJson(JsonObject o)
    {
        var s = new RuntimeState
        {
            StoryId = Str(o, "story"),
            Label = Str(o, "label"),
            Ip = (int)Num(o, "ip"),
            PlayTime = Num(o, "playtime"),
            ChapterTitle = Str(o, "chapter") ?? "",
            LastText = Str(o, "last_text") ?? "",
        };
        if (o["stack"] is JsonArray stack)
        {
            foreach (var n in stack)
            {
                if (n is JsonObject f) s.CallStack.Add(new CallFrame { Label = Str(f, "label"), Ip = (int)Num(f, "ip") });
            }
        }
        if (o["vars"] is JsonObject vars)
        {
            foreach (var kv in vars) s.Variables[kv.Key] = JsonToValue(kv.Value);
        }
        if (o["visited"] is JsonArray visited)
        {
            foreach (var n in visited) s.VisitedLabels.Add(n?.GetValue<string>());
        }
        if (o["presentation"] is JsonObject p) s.Presentation = PresentationFromJson(p);
        return s;
    }

    private static JsonObject PresentationToJson(PresentationState p)
    {
        var images = new JsonArray();
        foreach (var i in p.Images)
        {
            images.Add(new JsonObject { ["tag"] = i.Tag, ["path"] = i.Path, ["x"] = i.X, ["y"] = i.Y });
        }
        var audio = new JsonObject();
        foreach (var kv in p.Audio)
        {
            audio[kv.Key] = new JsonObject { ["path"] = kv.Value.Path, ["volume"] = kv.Value.Volume, ["loop"] = kv.Value.Loop };
        }
        var d = p.Desktop;
        var icons = new JsonArray();
        foreach (var i in d.Icons)
        {
            icons.Add(new JsonObject
            {
                ["id"] = i.Id, ["title"] = i.Title, ["image"] = i.Image, ["side"] = i.Side,
                ["action"] = i.Action, ["error"] = i.Error, ["hidden"] = i.Hidden,
            });
        }
        var folders = new JsonArray();
        foreach (var f in d.Folders)
        {
            var files = new JsonArray();
            foreach (var file in f.Files)
            {
                files.Add(new JsonObject { ["id"] = file.Id, ["title"] = file.Title, ["image"] = file.Image, ["action"] = file.Action });
            }
            folders.Add(new JsonObject { ["id"] = f.Id, ["title"] = f.Title, ["files"] = files });
        }
        return new JsonObject
        {
            ["scene"] = p.Scene,
            ["images"] = images,
            ["viewer"] = p.Viewer,
            ["audio"] = audio,
            ["desktop"] = new JsonObject
            {
                ["visible"] = d.Visible,
                ["wallpaper"] = d.Wallpaper,
                ["icons"] = icons,
                ["folders"] = folders,
                ["open_folder"] = d.OpenFolder,
                ["clock"] = d.Clock,
            },
        };
    }

    private static PresentationState PresentationFromJson(JsonObject o)
    {
        var p = new PresentationState { Scene = Str(o, "scene"), Viewer = Str(o, "viewer") };
        if (o["images"] is JsonArray images)
        {
            foreach (var n in images)
            {
                if (n is JsonObject i)
                {
                    p.Images.Add(new ImageState { Tag = Str(i, "tag"), Path = Str(i, "path"), X = (float)Num(i, "x", 0.5), Y = (float)Num(i, "y", 0.5) });
                }
            }
        }
        if (o["audio"] is JsonObject audio)
        {
            foreach (var kv in audio)
            {
                if (kv.Value is JsonObject a)
                {
                    p.Audio[kv.Key] = new AudioState { Path = Str(a, "path"), Volume = (float)Num(a, "volume", 1), Loop = Bool(a, "loop", true) };
                }
            }
        }
        if (o["desktop"] is JsonObject d)
        {
            var ds = p.Desktop;
            ds.Visible = Bool(d, "visible", false);
            ds.Wallpaper = Str(d, "wallpaper");
            ds.OpenFolder = Str(d, "open_folder");
            ds.Clock = Str(d, "clock");
            if (d["icons"] is JsonArray icons)
            {
                foreach (var n in icons)
                {
                    if (n is not JsonObject i) continue;
                    ds.Icons.Add(new DesktopIcon
                    {
                        Id = Str(i, "id"), Title = Str(i, "title"), Image = Str(i, "image"), Side = Str(i, "side") ?? "left",
                        Action = Str(i, "action"), Error = Str(i, "error"), Hidden = Bool(i, "hidden", false),
                    });
                }
            }
            if (d["folders"] is JsonArray folders)
            {
                foreach (var n in folders)
                {
                    if (n is not JsonObject f) continue;
                    var folder = new DesktopFolder { Id = Str(f, "id"), Title = Str(f, "title") };
                    if (f["files"] is JsonArray files)
                    {
                        foreach (var fn in files)
                        {
                            if (fn is JsonObject file)
                            {
                                folder.Files.Add(new DesktopFile { Id = Str(file, "id"), Title = Str(file, "title"), Image = Str(file, "image"), Action = Str(file, "action") });
                            }
                        }
                    }
                    ds.Folders.Add(folder);
                }
            }
        }
        return p;
    }

    /// <summary>Значение переменной → JSON. Тип сохраняется естественно: true/false, целое, дробное, строка.</summary>
    public static JsonNode ValueToJson(object v) => v switch
    {
        null => null,
        bool b => JsonValue.Create(b),
        long l => JsonValue.Create(l),
        int i => JsonValue.Create((long)i),
        double d => JsonValue.Create(d),
        float f => JsonValue.Create((double)f),
        _ => JsonValue.Create(v.ToString()),
    };

    /// <summary>JSON → значение переменной. Числа без дробной части становятся long, остальные double.</summary>
    public static object JsonToValue(JsonNode n)
    {
        if (n is not JsonValue v) return null;
        if (v.TryGetValue(out bool b)) return b;
        if (v.TryGetValue(out long l)) return l;
        if (v.TryGetValue(out double d)) return d;
        if (v.TryGetValue(out string s)) return s;
        return v.ToString();
    }

    private static string Str(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue(out string s) ? s : null;

    private static double Num(JsonObject o, string key, double fallback = 0) =>
        o[key] is JsonValue v && v.TryGetValue(out double d) ? d : fallback;

    private static bool Bool(JsonObject o, string key, bool fallback) =>
        o[key] is JsonValue v && v.TryGetValue(out bool b) ? b : fallback;
}
