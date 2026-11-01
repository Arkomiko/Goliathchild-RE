using System;
using GoliathChildRE.Ui;
using Godot;

namespace GoliathChildRE.Os;

/// <summary>
/// Окно виртуальной ОС: заголовок с градиентом, кнопка закрытия, перетаскивание за заголовок.
/// Внутрь кладётся любое содержимое (документ, картинка, папка, диалог).
/// </summary>
public partial class OsWindow : PanelContainer
{
    /// <summary>Игрок нажал крестик.</summary>
    public event Action CloseRequested;

    private Label _title;
    private Button _close;
    private PanelContainer _bar;
    private bool _dragging;
    private Vector2 _dragOffset;

    /// <summary>Сюда добавляется содержимое окна.</summary>
    public PanelContainer Body { get; private set; }

    public OsWindow() : this("", true) { }

    public OsWindow(string title, bool closable, StyleBox body = null)
    {
        AddThemeStyleboxOverride("panel", OsStyle.Frame(true));
        var column = UiKit.VBox(0);
        AddChild(column);

        _bar = UiKit.Panel(OsStyle.TitleBar(true));
        _bar.CustomMinimumSize = new Vector2(0, OsStyle.TitleHeight);
        _bar.MouseFilter = MouseFilterEnum.Stop;
        _bar.GuiInput += OnBarInput;
        column.AddChild(_bar);

        var row = UiKit.HBox(10);
        _bar.AddChild(row);
        _title = UiKit.Label(title, Fonts.BodyBold, 30, Colors.White);
        _title.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.6f));
        _title.AddThemeConstantOverride("shadow_offset_x", 2);
        _title.AddThemeConstantOverride("shadow_offset_y", 2);
        _title.VerticalAlignment = VerticalAlignment.Center;
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _title.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _title.ClipText = true;
        row.AddChild(_title);

        _close = OsStyle.CloseButton();
        _close.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _close.Visible = closable;
        _close.Pressed += () => CloseRequested?.Invoke();
        row.AddChild(_close);

        Body = UiKit.Panel(body ?? OsStyle.Face());
        Body.SizeFlagsVertical = SizeFlags.ExpandFill;
        // Клики по телу окна проходят насквозь: так клик по документу продвигает историю, как в оригинале.
        Body.MouseFilter = MouseFilterEnum.Ignore;
        column.AddChild(Body);

        MouseFilter = MouseFilterEnum.Ignore;
    }

    public string Title
    {
        get => _title.Text;
        set => _title.Text = value;
    }

    public bool Closable
    {
        get => _close.Visible;
        set => _close.Visible = value;
    }

    /// <summary>Перетаскивание окна за заголовок в пределах родителя; клик выводит окно наверх.</summary>
    private void OnBarInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            _dragging = mb.Pressed;
            _dragOffset = GetGlobalMousePosition() - GlobalPosition;
            if (mb.Pressed) MoveToFront();
            AcceptEvent();
        }
        else if (e is InputEventMouseMotion && _dragging)
        {
            var parent = GetParentControl();
            var target = GetGlobalMousePosition() - _dragOffset;
            if (parent != null)
            {
                // Не даём утащить заголовок за пределы экрана — иначе окно нельзя будет вернуть.
                var rect = parent.GetGlobalRect();
                target.X = Mathf.Clamp(target.X, rect.Position.X - Size.X + 200, rect.End.X - 200);
                target.Y = Mathf.Clamp(target.Y, rect.Position.Y, rect.End.Y - OsStyle.TitleHeight);
            }
            GlobalPosition = target;
            AcceptEvent();
        }
    }
}
