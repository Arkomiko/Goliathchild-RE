using System.Collections.Generic;
using Godot;

namespace GicEngine.IO;

/// <summary>
/// Пути к папкам игры и чтение файлов.
/// Пользовательские истории и дополнительные языки лежат рядом с игрой (ответ 60),
/// но на Android, iOS и консолях папка игры только для чтения — там используем папку пользователя user://.
/// </summary>
public static class GicPaths
{
    private static string _gameDataDir;

    /// <summary>
    /// Папка «рядом с игрой». В редакторе Godot — корень проекта, в собранной игре — папка с exe.
    /// </summary>
    public static string GameDataDir
    {
        get
        {
            if (_gameDataDir != null) return _gameDataDir;

            if (OS.HasFeature("editor"))
            {
                _gameDataDir = ProjectSettings.GlobalizePath("res://");
            }
            else if (OS.HasFeature("android") || OS.HasFeature("ios") || OS.HasFeature("web"))
            {
                _gameDataDir = ProjectSettings.GlobalizePath("user://");
            }
            else
            {
                _gameDataDir = OS.GetExecutablePath().GetBaseDir();
            }
            _gameDataDir = _gameDataDir.TrimEnd('/', '\\');
            return _gameDataDir;
        }
    }

    /// <summary>Папка пользовательских историй (stories рядом с игрой). Создаётся при первом обращении.</summary>
    public static string StoriesDir => EnsureDir(GameDataDir.PathJoin("stories"));

    /// <summary>Папка с дополнительными файлами локализации рядом с игрой.</summary>
    public static string ExternalLanguageDir => GameDataDir.PathJoin("language");

    /// <summary>Создаёт папку, если её нет. Если создать нельзя (нет прав) — откатывается в user://.</summary>
    public static string EnsureDir(string path)
    {
        if (DirAccess.DirExistsAbsolute(path)) return path;
        if (DirAccess.MakeDirRecursiveAbsolute(path) == Error.Ok) return path;

        // Например, игра установлена в Program Files без прав на запись.
        var fallback = ProjectSettings.GlobalizePath("user://").PathJoin(path.GetFile());
        DirAccess.MakeDirRecursiveAbsolute(fallback);
        return fallback;
    }

    /// <summary>Читает текстовый файл целиком (UTF-8). Возвращает null, если файла нет.</summary>
    public static string ReadText(string path)
    {
        if (!FileAccess.FileExists(path)) return null;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        return f?.GetAsText();
    }

    /// <summary>Записывает текстовый файл в UTF-8. Возвращает true при успехе.</summary>
    public static bool WriteText(string path, string text)
    {
        EnsureDir(path.GetBaseDir());
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (f == null) return false;
        f.StoreString(text);
        return true;
    }

    /// <summary>Список файлов с расширением ext в папке dir (рекурсивно). Работает и для res://, и для обычных путей.</summary>
    public static List<string> ListFiles(string dir, string ext, bool recursive = true)
    {
        var result = new List<string>();
        Collect(dir, ext.TrimStart('.').ToLowerInvariant(), recursive, result);
        result.Sort(System.StringComparer.Ordinal);
        return result;
    }

    private static void Collect(string dir, string ext, bool recursive, List<string> result)
    {
        using var d = DirAccess.Open(dir);
        if (d == null) return;
        foreach (var file in d.GetFiles())
        {
            if (file.GetExtension().ToLowerInvariant() == ext) result.Add(dir.PathJoin(file));
        }
        if (!recursive) return;
        foreach (var sub in d.GetDirectories())
        {
            // Служебные папки пропускаем.
            if (sub.StartsWith(".")) continue;
            Collect(dir.PathJoin(sub), ext, true, result);
        }
    }

    /// <summary>Подпапки первого уровня (для списка историй).</summary>
    public static List<string> ListDirs(string dir)
    {
        var result = new List<string>();
        using var d = DirAccess.Open(dir);
        if (d == null) return result;
        foreach (var sub in d.GetDirectories())
        {
            if (!sub.StartsWith(".")) result.Add(dir.PathJoin(sub));
        }
        result.Sort(System.StringComparer.Ordinal);
        return result;
    }
}
