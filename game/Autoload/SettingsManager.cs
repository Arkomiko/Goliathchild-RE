using GicEngine.Localization;
using Godot;

namespace GoliathChildRE;

/// <summary>
/// Настройки игры (ответ 37): экран, графика, звук, скорость текста, доступность и язык.
/// Хранятся в user://settings.cfg и применяются сразу при изменении.
/// </summary>
public partial class SettingsManager : Node
{
    public static SettingsManager Instance { get; private set; }

    /// <summary>Сигнал для экранов, которым нужно перестроиться (фильтр для дальтоников, глитч и т. п.).</summary>
    [Signal]
    public delegate void ChangedEventHandler();

    private const string FilePath = "user://settings.cfg";

    /// <summary>Режимы окна из ответа 37.</summary>
    public enum WindowModeKind
    {
        Windowed = 0,
        Borderless = 1,
        Fullscreen = 2,
    }

    /// <summary>Режимы для дальтоников: фильтр усиливает различие цветов, которые плохо различаются.</summary>
    public enum ColorblindKind
    {
        Off = 0,
        Protanopia = 1,
        Deuteranopia = 2,
        Tritanopia = 3,
    }

    /// <summary>Разрешения окна. 2560×1440 — базовое разрешение игры (ответ 42).</summary>
    public static readonly Vector2I[] Resolutions =
    {
        new(1280, 720), new(1600, 900), new(1920, 1080), new(2560, 1440), new(3840, 2160),
    };

    /// <summary>Скорость текста, при которой он появляется мгновенно.</summary>
    public const float InstantTextSpeed = 150f;

    // ---- экран и графика
    public Vector2I Resolution = new(1600, 900);
    public WindowModeKind WindowMode = WindowModeKind.Windowed;
    public bool VSync = true;
    // 0 — без ограничения.
    public int MaxFps;
    // Шум, сканлайны и виньетка в меню.
    public bool ScreenEffects = true;

    // ---- звук (0..1)
    public float MasterVolume = 1f;
    public float MusicVolume = 0.8f;
    public float SfxVolume = 0.9f;
    public float VoiceVolume = 0.8f;

    // ---- текст
    // Символов в секунду.
    public float TextSpeed = 45f;
    // Пауза в автоматическом режиме после того, как реплика напечатана (секунды).
    public float AutoDelay = 1.5f;
    // Пропуск по Ctrl пропускает и непрочитанные реплики.
    public bool SkipUnseen;

    // ---- доступность
    // Режим светочувствительности: глитч и вспышки становятся мягкими и редкими.
    public bool Photosensitive;
    public ColorblindKind Colorblind = ColorblindKind.Off;
    public float ColorblindStrength = 1f;

    // ---- язык
    public string Language = "ru";

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        CreateAudioBuses();
        Load();
        ApplyAll();
    }

    /// <summary>
    /// Шины звука Music, SFX и Voice создаются кодом, а не файлом default_bus_layout.tres:
    /// так их нельзя случайно сломать в редакторе, и регуляторы громкости всегда работают.
    /// </summary>
    private static void CreateAudioBuses()
    {
        foreach (var name in new[] { "Music", "SFX", "Voice" })
        {
            if (AudioServer.GetBusIndex(name) >= 0) continue;
            var index = AudioServer.BusCount;
            AudioServer.AddBus(index);
            AudioServer.SetBusName(index, name);
            AudioServer.SetBusSend(index, "Master");
        }
    }

    // ---------------------------------------------------------------- применение

    public void ApplyAll()
    {
        ApplyDisplay();
        ApplyAudio();
        ApplyLanguage();
        EmitSignal(SignalName.Changed);
    }

    /// <summary>Режим окна, разрешение, вертикальная синхронизация и ограничение FPS.</summary>
    public void ApplyDisplay()
    {
        // На телефонах и консолях окно всегда во весь экран — настройки окна там не применяются.
        if (!OS.HasFeature("pc")) return;

        switch (WindowMode)
        {
            case WindowModeKind.Fullscreen:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
                break;
            case WindowModeKind.Borderless:
                // Окно без рамки размером с экран: быстро переключается и удобно на втором мониторе.
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true);
                var screen = DisplayServer.WindowGetCurrentScreen();
                DisplayServer.WindowSetPosition(DisplayServer.ScreenGetPosition(screen));
                DisplayServer.WindowSetSize(DisplayServer.ScreenGetSize(screen));
                break;
            default:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, false);
                DisplayServer.WindowSetSize(Resolution);
                CenterWindow();
                break;
        }
        DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = MaxFps;
    }

    private static void CenterWindow()
    {
        var screen = DisplayServer.WindowGetCurrentScreen();
        var screenPos = DisplayServer.ScreenGetPosition(screen);
        var screenSize = DisplayServer.ScreenGetSize(screen);
        var size = DisplayServer.WindowGetSize();
        DisplayServer.WindowSetPosition(screenPos + (screenSize - size) / 2);
    }

    /// <summary>Громкость шин. Ноль громкости — полное выключение шины (без «шёпота» на -80 дБ).</summary>
    public void ApplyAudio()
    {
        SetBus("Master", MasterVolume);
        SetBus("Music", MusicVolume);
        SetBus("SFX", SfxVolume);
        SetBus("Voice", VoiceVolume);
    }

    private static void SetBus(string name, float volume)
    {
        var index = AudioServer.GetBusIndex(name);
        if (index < 0) return;
        AudioServer.SetBusMute(index, volume <= 0.001f);
        AudioServer.SetBusVolumeDb(index, Mathf.LinearToDb(Mathf.Max(volume, 0.001f)));
    }

    public void ApplyLanguage()
    {
        GicLocalization.Instance?.SetLanguage(Language);
    }

    // ---------------------------------------------------------------- файл

    public void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("display", "resolution", Resolution);
        cfg.SetValue("display", "window_mode", (int)WindowMode);
        cfg.SetValue("display", "vsync", VSync);
        cfg.SetValue("display", "max_fps", MaxFps);
        cfg.SetValue("display", "screen_effects", ScreenEffects);
        cfg.SetValue("audio", "master", MasterVolume);
        cfg.SetValue("audio", "music", MusicVolume);
        cfg.SetValue("audio", "sfx", SfxVolume);
        cfg.SetValue("audio", "voice", VoiceVolume);
        cfg.SetValue("text", "speed", TextSpeed);
        cfg.SetValue("text", "auto_delay", AutoDelay);
        cfg.SetValue("text", "skip_unseen", SkipUnseen);
        cfg.SetValue("access", "photosensitive", Photosensitive);
        cfg.SetValue("access", "colorblind", (int)Colorblind);
        cfg.SetValue("access", "colorblind_strength", ColorblindStrength);
        cfg.SetValue("general", "language", Language);
        cfg.Save(FilePath);
    }

    private void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(FilePath) != Error.Ok)
        {
            // Первый запуск: язык системы, если он есть среди переводов, иначе русский.
            var system = OS.GetLocaleLanguage();
            Language = system == "en" ? "en" : "ru";
            return;
        }
        Resolution = cfg.GetValue("display", "resolution", Resolution).AsVector2I();
        WindowMode = (WindowModeKind)cfg.GetValue("display", "window_mode", (int)WindowMode).AsInt32();
        VSync = cfg.GetValue("display", "vsync", VSync).AsBool();
        MaxFps = cfg.GetValue("display", "max_fps", MaxFps).AsInt32();
        ScreenEffects = cfg.GetValue("display", "screen_effects", ScreenEffects).AsBool();
        MasterVolume = (float)cfg.GetValue("audio", "master", MasterVolume).AsDouble();
        MusicVolume = (float)cfg.GetValue("audio", "music", MusicVolume).AsDouble();
        SfxVolume = (float)cfg.GetValue("audio", "sfx", SfxVolume).AsDouble();
        VoiceVolume = (float)cfg.GetValue("audio", "voice", VoiceVolume).AsDouble();
        TextSpeed = (float)cfg.GetValue("text", "speed", TextSpeed).AsDouble();
        AutoDelay = (float)cfg.GetValue("text", "auto_delay", AutoDelay).AsDouble();
        SkipUnseen = cfg.GetValue("text", "skip_unseen", SkipUnseen).AsBool();
        Photosensitive = cfg.GetValue("access", "photosensitive", Photosensitive).AsBool();
        Colorblind = (ColorblindKind)cfg.GetValue("access", "colorblind", (int)Colorblind).AsInt32();
        ColorblindStrength = (float)cfg.GetValue("access", "colorblind_strength", ColorblindStrength).AsDouble();
        Language = cfg.GetValue("general", "language", Language).AsString();
    }

    /// <summary>Изменить настройку: применить, сохранить, оповестить экраны.</summary>
    public void Commit(bool display = false)
    {
        if (display) ApplyDisplay();
        ApplyAudio();
        ApplyLanguage();
        Save();
        EmitSignal(SignalName.Changed);
    }
}
