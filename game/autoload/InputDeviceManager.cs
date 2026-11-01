using Godot;

namespace GoliathChildRE;

/// <summary>
/// Действия управления и автоопределение устройства (ответ 39).
/// Основное устройство — клавиатура и мышь; геймпады определяются по имени, чтобы показывать правильные подсказки
/// (Xbox, PlayStation, Nintendo, Steam, Sega, руль). Старые PS1/PS2 подключаются через USB-адаптер и видны как обычный геймпад.
/// </summary>
public partial class InputDeviceManager : Node
{
    public static InputDeviceManager Instance { get; private set; }

    public enum DeviceKind
    {
        KeyboardMouse,
        Xbox,
        PlayStation,
        Nintendo,
        Steam,
        Sega,
        Wheel,
        Gamepad,
    }

    [Signal]
    public delegate void DeviceChangedEventHandler(int kind);

    /// <summary>Каким устройством игрок пользовался последним.</summary>
    public DeviceKind Current { get; private set; } = DeviceKind.KeyboardMouse;

    /// <summary>Имя последнего геймпада (для экрана настроек).</summary>
    public string CurrentPadName { get; private set; } = "";

    public override void _EnterTree()
    {
        Instance = this;
        RegisterActions();
    }

    public override void _Ready()
    {
        Input.JoyConnectionChanged += OnJoyConnectionChanged;
    }

    /// <summary>
    /// Действия создаются кодом, а не в project.godot: так их проще читать и менять,
    /// и раскладка не ломается при ручной правке файла проекта.
    /// </summary>
    private static void RegisterActions()
    {
        Add("vn_advance", Key.Enter, Key.KpEnter, Key.Space);
        AddMouse("vn_advance", MouseButton.Left);
        AddJoy("vn_advance", JoyButton.A);

        Add("vn_rollback", Key.Pageup);
        AddMouse("vn_rollback", MouseButton.WheelUp);
        AddJoy("vn_rollback", JoyButton.LeftShoulder);

        Add("vn_skip", Key.Ctrl);
        AddJoy("vn_skip", JoyButton.RightShoulder);

        Add("vn_auto", Key.A);
        AddJoy("vn_auto", JoyButton.Y);

        Add("vn_backlog", Key.H);
        AddJoy("vn_backlog", JoyButton.X);

        Add("vn_menu", Key.Escape);
        AddJoy("vn_menu", JoyButton.Start);

        Add("quick_save", Key.F5);
        Add("quick_load", Key.F9);
    }

    private static void Ensure(string action)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action);
    }

    private static void Add(string action, params Key[] keys)
    {
        Ensure(action);
        foreach (var k in keys) InputMap.ActionAddEvent(action, new InputEventKey { Keycode = k });
    }

    private static void AddMouse(string action, MouseButton button)
    {
        Ensure(action);
        InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button });
    }

    private static void AddJoy(string action, JoyButton button)
    {
        Ensure(action);
        // Device = -1: любой подключённый геймпад.
        InputMap.ActionAddEvent(action, new InputEventJoypadButton { ButtonIndex = button, Device = -1 });
    }

    /// <summary>Смотрим на каждое событие ввода и запоминаем, каким устройством пользуется игрок.</summary>
    public override void _Input(InputEvent e)
    {
        switch (e)
        {
            case InputEventKey or InputEventMouseButton:
                SetDevice(DeviceKind.KeyboardMouse, "");
                break;
            case InputEventMouseMotion motion when motion.Relative.LengthSquared() > 4:
                SetDevice(DeviceKind.KeyboardMouse, "");
                break;
            case InputEventJoypadButton jb:
                SetDevice(Classify(Input.GetJoyName(jb.Device)), Input.GetJoyName(jb.Device));
                break;
            // Мелкое дрожание стиков не должно переключать подсказки.
            case InputEventJoypadMotion jm when Mathf.Abs(jm.AxisValue) > 0.5f:
                SetDevice(Classify(Input.GetJoyName(jm.Device)), Input.GetJoyName(jm.Device));
                break;
        }
    }

    private void SetDevice(DeviceKind kind, string name)
    {
        if (kind == Current && name == CurrentPadName) return;
        Current = kind;
        CurrentPadName = name;
        EmitSignal(SignalName.DeviceChanged, (int)kind);
    }

    private void OnJoyConnectionChanged(long device, bool connected)
    {
        if (connected) SetDevice(Classify(Input.GetJoyName((int)device)), Input.GetJoyName((int)device));
        else if (Current != DeviceKind.KeyboardMouse && Input.GetConnectedJoypads().Count == 0) SetDevice(DeviceKind.KeyboardMouse, "");
    }

    /// <summary>Определение типа геймпада по имени, которое сообщает драйвер (SDL).</summary>
    public static DeviceKind Classify(string name)
    {
        var n = (name ?? "").ToLowerInvariant();
        if (n.Contains("xbox") || n.Contains("xinput") || n.Contains("microsoft")) return DeviceKind.Xbox;
        if (n.Contains("playstation") || n.Contains("dualshock") || n.Contains("dualsense") || n.Contains("sony") ||
            n.Contains("ps1") || n.Contains("ps2") || n.Contains("ps3") || n.Contains("ps4") || n.Contains("ps5")) return DeviceKind.PlayStation;
        if (n.Contains("nintendo") || n.Contains("switch") || n.Contains("joy-con") || n.Contains("gamecube") || n.Contains("pro controller")) return DeviceKind.Nintendo;
        if (n.Contains("steam")) return DeviceKind.Steam;
        if (n.Contains("sega") || n.Contains("genesis") || n.Contains("mega drive") || n.Contains("saturn")) return DeviceKind.Sega;
        if (n.Contains("wheel") || n.Contains("racing") || n.Contains("g29") || n.Contains("g920") || n.Contains("g923") ||
            n.Contains("thrustmaster") || n.Contains("fanatec")) return DeviceKind.Wheel;
        return DeviceKind.Gamepad;
    }

    /// <summary>Подпись для кнопки «далее» под текущее устройство.</summary>
    public string AdvanceHint() => Current switch
    {
        DeviceKind.KeyboardMouse => "ЛКМ / Enter",
        DeviceKind.PlayStation => "✕",
        DeviceKind.Nintendo => "B",
        DeviceKind.Sega => "C",
        _ => "A",
    };
}
