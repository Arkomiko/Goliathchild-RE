using System.Collections.Generic;
using System.Text.Json;
using GicEngine.IO;
using Godot;

namespace GicEngine.Localization;

/// <summary>Язык, найденный в папке language.</summary>
public sealed class LanguageInfo
{
    public string Locale;
    public string Name;
    public string Path;
}

/// <summary>
/// Локализация интерфейса (ответ 7): русский и английский встроены, остальные языки подключаются
/// файлами language/*.json — и из игры (res://language), и из папки language рядом с игрой.
/// Перевод отдаётся в TranslationServer Godot, поэтому надписи с ключами (например "menu.story")
/// переводятся сами и обновляются на лету при смене языка.
///
/// Формат файла:
/// { "locale": "en", "name": "English", "strings": { "menu.story": "Story", ... } }
/// </summary>
public partial class GicLocalization : Node
{
    public static GicLocalization Instance { get; private set; }

    /// <summary>Найденные языки в порядке загрузки (встроенные первыми).</summary>
    public readonly List<LanguageInfo> Languages = new();

    private readonly Dictionary<string, Translation> _translations = new();

    public override void _EnterTree()
    {
        // Загружаем переводы раньше остальных синглтонов, чтобы меню сразу появилось на нужном языке.
        Instance = this;
        LoadAll();
    }

    /// <summary>Перечитать все файлы языков (например, после того как игрок положил новый перевод).</summary>
    public void LoadAll()
    {
        foreach (var t in _translations.Values) TranslationServer.RemoveTranslation(t);
        _translations.Clear();
        Languages.Clear();

        foreach (var path in GicPaths.ListFiles("res://language", "json", false)) LoadFile(path);
        // Внешние файлы загружаются вторыми и могут переопределить строки встроенного перевода.
        if (DirAccess.DirExistsAbsolute(GicPaths.ExternalLanguageDir))
        {
            foreach (var path in GicPaths.ListFiles(GicPaths.ExternalLanguageDir, "json", false)) LoadFile(path);
        }
    }

    private void LoadFile(string path)
    {
        var text = GicPaths.ReadText(path);
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            using var json = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = json.RootElement;
            var locale = root.GetProperty("locale").GetString();
            var name = root.TryGetProperty("name", out var n) ? n.GetString() : locale;
            if (string.IsNullOrEmpty(locale)) return;

            if (!_translations.TryGetValue(locale, out var translation))
            {
                translation = new Translation { Locale = locale };
                _translations[locale] = translation;
                TranslationServer.AddTranslation(translation);
                Languages.Add(new LanguageInfo { Locale = locale, Name = name, Path = path });
            }
            if (root.TryGetProperty("strings", out var strings))
            {
                foreach (var kv in strings.EnumerateObject())
                {
                    translation.AddMessage(kv.Name, kv.Value.GetString());
                }
            }
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"Файл языка {path} не прочитан: {e.Message}");
        }
    }

    /// <summary>Включить язык. Все надписи-ключи в интерфейсе обновятся автоматически.</summary>
    public void SetLanguage(string locale)
    {
        TranslationServer.SetLocale(locale);
    }

    /// <summary>Перевод строки по ключу. Если ключа нет — возвращается сам ключ, чтобы пропуск был заметен.</summary>
    public static string T(string key) => TranslationServer.Translate(key);

    /// <summary>Перевод с подстановкой {0}, {1}…</summary>
    public static string T(string key, params object[] args) => string.Format(T(key), args);
}
