using System;
using GoliathChildRE.Ui;
using Godot;

namespace GoliathChildRE;

/// <summary>
/// Корневой узел игры. Держит текущий экран (главное меню или игровую сессию) и слои поверх него:
/// фильтр для дальтоников и затемнение при переходах между экранами.
/// Сама навигация живёт в GameManager.
/// </summary>
public partial class Main : Node
{
    private Control _screenRoot;
    private Control _current;
    private ColorRect _colorblind;
    private ShaderMaterial _colorblindMaterial;
    private ColorRect _fade;
    private bool _transitioning;

    public override void _Ready()
    {
        // Слой 0: экраны игры.
        var screens = new CanvasLayer { Layer = 0 };
        AddChild(screens);
        _screenRoot = UiKit.Full(new Control { MouseFilter = Control.MouseFilterEnum.Ignore });
        screens.AddChild(_screenRoot);

        // Слой 100: фильтр для дальтоников поверх всего изображения.
        var post = new CanvasLayer { Layer = 100 };
        AddChild(post);
        _colorblindMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://addons/gic_engine/Effects/colorblind.gdshader") };
        _colorblind = UiKit.Full(new ColorRect { Material = _colorblindMaterial, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false });
        post.AddChild(_colorblind);

        // Слой 110: чёрное затемнение для переходов.
        var fadeLayer = new CanvasLayer { Layer = 110 };
        AddChild(fadeLayer);
        _fade = UiKit.Full(new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore });
        fadeLayer.AddChild(_fade);

        SettingsManager.Instance.Changed += ApplyAccessibility;
        ApplyAccessibility();

        GameManager.Instance.Attach(this);
    }

    /// <summary>Включить фильтр дальтонизма по настройкам.</summary>
    private void ApplyAccessibility()
    {
        var s = SettingsManager.Instance;
        _colorblind.Visible = s.Colorblind != SettingsManager.ColorblindKind.Off;
        _colorblindMaterial.SetShaderParameter("mode", (int)s.Colorblind);
        _colorblindMaterial.SetShaderParameter("amount", s.ColorblindStrength);
    }

    /// <summary>
    /// Сменить экран с затемнением: экран темнеет, старый удаляется, новый создаётся, экран светлеет.
    /// В режиме светочувствительности переход медленнее и мягче.
    /// </summary>
    public void ChangeScreen(Func<Control> create, float duration = 0.45f)
    {
        if (_transitioning) return;
        _transitioning = true;
        _fade.MouseFilter = Control.MouseFilterEnum.Stop;

        var tween = CreateTween();
        tween.TweenProperty(_fade, "modulate:a", 1f, _current == null ? 0f : duration);
        tween.TweenCallback(Callable.From(() =>
        {
            _current?.QueueFree();
            _current = create();
            _screenRoot.AddChild(UiKit.Full(_current));
        }));
        tween.TweenProperty(_fade, "modulate:a", 0f, duration);
        tween.TweenCallback(Callable.From(() =>
        {
            _fade.MouseFilter = Control.MouseFilterEnum.Ignore;
            _transitioning = false;
        }));
    }

    /// <summary>Текущий экран (для DevAutomation и отладки).</summary>
    public Control CurrentScreen => _current;
}
