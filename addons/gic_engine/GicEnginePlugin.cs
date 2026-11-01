#if TOOLS
using Godot;

namespace GicEngine;

/// <summary>
/// Плагин ядра GiC Engine для редактора Godot.
/// Само ядро работает и без плагина (оно используется во время игры).
/// Плагин нужен разработчикам: файлы сценариев .gics открываются прямо во встроенном текстовом редакторе Godot.
/// </summary>
[Tool]
public partial class GicEnginePlugin : EditorPlugin
{
    // Ключ настройки редактора со списком расширений, которые Godot считает текстовыми.
    private const string TextExtensionsSetting = "docks/filesystem/textfile_extensions";

    /// <summary>Вызывается, когда плагин включается: регистрируем расширение .gics.</summary>
    public override void _EnterTree()
    {
        var settings = EditorInterface.Singleton.GetEditorSettings();
        var current = settings.GetSetting(TextExtensionsSetting).AsString();

        // Добавляем gics только один раз, чтобы не дублировать расширение при каждом запуске редактора.
        if (System.Array.IndexOf(current.Split(','), "gics") < 0)
        {
            settings.SetSetting(TextExtensionsSetting, string.IsNullOrEmpty(current) ? "gics" : current + ",gics");
        }
    }
}
#endif
