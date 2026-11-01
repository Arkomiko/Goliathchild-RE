using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using GicEngine.Runtime;
using GicEngine.Save;
using Godot;

namespace GoliathChildRE;

/// <summary>
/// Сохранения в стиле Dishonored (ответ 33): без фиксированных слотов.
/// Автосохранения (несколько последних по кругу), одно быстрое (F5/F9) и сколько угодно ручных.
/// Плюс «постоянные» данные, общие для всех прохождений: прочитанные реплики, открытые картинки галереи и главы.
/// </summary>
public partial class SaveManager : Node
{
    public static SaveManager Instance { get; private set; }

    // Сколько автосохранений хранить. Старые перезаписываются по кругу.
    private const int AutoSaveCount = 3;
    private const string PersistentPath = "user://persistent.json";

    public string SavesDir { get; private set; }

    private readonly HashSet<string> _seenLines = new();
    private readonly HashSet<string> _seenImages = new();
    private readonly HashSet<string> _chapters = new();
    private bool _persistentDirty;

    public bool StoryCompleted { get; private set; }

    public override void _EnterTree()
    {
        Instance = this;
        SavesDir = ProjectSettings.GlobalizePath("user://saves");
        Directory.CreateDirectory(SavesDir);
        LoadPersistent();
    }

    /// <summary>Сохраняем постоянные данные при выходе из игры, чтобы не потерять прочитанное.</summary>
    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest || what == NotificationPredelete) FlushPersistent();
    }

    // ---------------------------------------------------------------- сохранения

    /// <summary>Все сохранения (по умолчанию — только для указанной истории), новые сверху.</summary>
    public List<SaveInfo> List(string storyId = null)
    {
        var result = new List<SaveInfo>();
        foreach (var path in Directory.GetFiles(SavesDir, "*" + SdromSave.Extension))
        {
            try
            {
                var info = SdromSave.ReadInfo(path);
                if (storyId == null || info.StoryId == storyId) result.Add(info);
            }
            catch (Exception e)
            {
                GD.PushWarning($"Сохранение {path} не прочитано: {e.Message}");
            }
        }
        result.Sort((a, b) => b.Created.CompareTo(a.Created));
        return result;
    }

    /// <summary>Самое свежее сохранение (для «Продолжить»).</summary>
    public SaveInfo Latest(string storyId)
    {
        var list = List(storyId);
        return list.Count > 0 ? list[0] : null;
    }

    /// <summary>
    /// Записать сохранение. Для ручного можно передать путь существующего файла — тогда он перезапишется.
    /// </summary>
    public SaveInfo Write(SaveKind kind, RuntimeState state, string storyTitle, byte[] thumbnail, string overwritePath = null)
    {
        var path = overwritePath ?? kind switch
        {
            SaveKind.Quick => Path.Combine(SavesDir, "quick" + SdromSave.Extension),
            SaveKind.Auto => NextAutoPath(),
            _ => Path.Combine(SavesDir, $"manual_{DateTime.Now:yyyyMMdd_HHmmss_fff}{SdromSave.Extension}"),
        };
        var info = new SaveInfo
        {
            Kind = kind,
            Title = string.IsNullOrEmpty(state.ChapterTitle) ? storyTitle : state.ChapterTitle,
            Chapter = state.ChapterTitle,
            LastText = Shorten(state.LastText, 160),
            Created = DateTime.Now,
            PlayTime = state.PlayTime,
            StoryId = state.StoryId,
            StoryTitle = storyTitle,
            GameVersion = ProjectSettings.GetSetting("application/config/version").AsString(),
            Thumbnail = thumbnail,
        };
        SdromSave.Write(path, info, state);
        info.FilePath = path;
        FlushPersistent();
        return info;
    }

    /// <summary>Загрузить прохождение из файла. Возвращает null, если файл повреждён.</summary>
    public RuntimeState Load(string path)
    {
        try
        {
            return SdromSave.ReadState(path);
        }
        catch (Exception e)
        {
            GD.PushError($"Не удалось загрузить {path}: {e.Message}");
            return null;
        }
    }

    public void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Свободный номер автосохранения или самый старый, если все заняты.</summary>
    private string NextAutoPath()
    {
        string oldest = null;
        var oldestTime = DateTime.MaxValue;
        for (var i = 1; i <= AutoSaveCount; i++)
        {
            var p = Path.Combine(SavesDir, $"auto_{i}{SdromSave.Extension}");
            if (!File.Exists(p)) return p;
            var t = File.GetLastWriteTime(p);
            if (t < oldestTime)
            {
                oldestTime = t;
                oldest = p;
            }
        }
        return oldest;
    }

    private static string Shorten(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max - 1) + "…").Replace('\n', ' ');

    // ---------------------------------------------------------------- постоянные данные

    public bool IsLineSeen(string key) => _seenLines.Contains(key);

    public void MarkLineSeen(string key)
    {
        if (_seenLines.Add(key)) _persistentDirty = true;
    }

    public bool IsImageSeen(string path) => _seenImages.Contains(path);

    public void MarkImageSeen(string path)
    {
        if (!string.IsNullOrEmpty(path) && _seenImages.Add(path)) _persistentDirty = true;
    }

    public bool IsChapterUnlocked(string id) => _chapters.Contains(id);

    public void UnlockChapter(string id)
    {
        if (_chapters.Add(id)) _persistentDirty = true;
        FlushPersistent();
    }

    public void MarkStoryCompleted()
    {
        StoryCompleted = true;
        _persistentDirty = true;
        FlushPersistent();
    }

    /// <summary>Записать постоянные данные, если они изменились.</summary>
    public void FlushPersistent()
    {
        if (!_persistentDirty) return;
        var o = new JsonObject
        {
            ["seen_lines"] = ToArray(_seenLines),
            ["seen_images"] = ToArray(_seenImages),
            ["chapters"] = ToArray(_chapters),
            ["completed"] = StoryCompleted,
        };
        File.WriteAllText(ProjectSettings.GlobalizePath(PersistentPath), o.ToJsonString());
        _persistentDirty = false;
    }

    private void LoadPersistent()
    {
        var path = ProjectSettings.GlobalizePath(PersistentPath);
        if (!File.Exists(path)) return;
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject o) return;
            Fill(o["seen_lines"], _seenLines);
            Fill(o["seen_images"], _seenImages);
            Fill(o["chapters"], _chapters);
            StoryCompleted = o["completed"]?.GetValue<bool>() ?? false;
        }
        catch (Exception e)
        {
            GD.PushWarning($"persistent.json не прочитан: {e.Message}");
        }
    }

    private static JsonArray ToArray(HashSet<string> set)
    {
        var a = new JsonArray();
        foreach (var s in set) a.Add(s);
        return a;
    }

    private static void Fill(JsonNode node, HashSet<string> set)
    {
        if (node is not JsonArray a) return;
        foreach (var n in a)
        {
            var s = n?.GetValue<string>();
            if (s != null) set.Add(s);
        }
    }
}
