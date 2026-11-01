using System;
using GicEngine.Localization;
using GoliathChildRE.Os;
using GoliathChildRE.Ui;
using Godot;

namespace GoliathChildRE.Session;

/// <summary>
/// Меню «Пуск» виртуальной ОС — оно же пауза. В оригинале кнопка «Пуск» открывала меню Ren'Py,
/// здесь меню оформлено как меню «Пуск» Windows XP: шапка, пункты с действиями, «Выключение» внизу.
/// </summary>
public partial class StartMenu : Control
{
    private readonly GameSession _session;
    private Button _first;

    public StartMenu(GameSession session)
    {
        _session = session;
    }

    public StartMenu() { }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        // Клик мимо меню закрывает его, как в Windows.
        GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true }) Visible = false;
        };

        var frame = UiKit.Panel(OsStyle.Frame(true));
        frame.AnchorTop = frame.AnchorBottom = 1;
        frame.OffsetLeft = 0;
        frame.OffsetRight = 720;
        frame.OffsetTop = -72 - 1000;
        frame.OffsetBottom = -72;
        frame.MouseFilter = MouseFilterEnum.Stop;
        AddChild(frame);

        var column = UiKit.VBox(0);
        frame.AddChild(column);

        // Шапка с «пользователем» — в XP здесь были аватар и имя.
        var header = UiKit.Panel(OsStyle.Gradient(Palette.OsTitleB, Palette.OsTitleA));
        header.CustomMinimumSize = new Vector2(0, 120);
        column.Add(header);
        var headerRow = UiKit.HBox(24);
        header.Add(UiKit.Margin(28, 0, 28, 0)).Add(headerRow);
        var avatar = new TextureRect
        {
            Texture = GD.Load<Texture2D>("res://icon.svg"),
            CustomMinimumSize = new Vector2(84, 84),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        headerRow.Add(avatar);
        var who = UiKit.Label("GiC_OS", Fonts.BodyBold, 40, Colors.White);
        who.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        headerRow.Add(who);

        var body = UiKit.Panel(UiKit.Box(Colors.White, default, 0, 0, 16, 16));
        body.SizeFlagsVertical = SizeFlags.ExpandFill;
        column.Add(body);
        var items = UiKit.VBox(4);
        body.AddChild(items);

        _first = Item(items, "start.resume", () => Visible = false);
        Item(items, "start.save", _session.ShowSaves);
        Item(items, "start.load", _session.ShowSaves);
        Item(items, "start.quicksave", () =>
        {
            Visible = false;
            _session.QuickSave();
        });
        Item(items, "start.backlog", _session.ShowBacklog);
        Item(items, "start.auto", () =>
        {
            Visible = false;
            _session.ToggleAuto();
        });
        Item(items, "start.skip", () =>
        {
            Visible = false;
            _session.ToggleSkip();
        });
        Item(items, "start.settings", _session.ShowSettings);
        items.Add(UiKit.Expander());

        // Нижняя полоса как в XP: «Выход из системы» и «Выключение».
        var footer = UiKit.Panel(OsStyle.Gradient(Palette.OsTitleA, Palette.OsTitleB));
        column.Add(footer);
        var footerRow = UiKit.HBox(16);
        footerRow.Alignment = BoxContainer.AlignmentMode.End;
        footer.Add(UiKit.Margin(20, 14, 20, 14)).Add(footerRow);
        footerRow.Add(FooterButton(GicLocalization.T("start.to_menu"), _session.ExitToMenu));
        footerRow.Add(FooterButton(GicLocalization.T("start.shutdown"), () => GameManager.Instance.QuitGame()));
    }

    private Button Item(VBoxContainer box, string key, Action action)
    {
        var normal = UiKit.Empty(24, 14);
        var hover = UiKit.Box(Palette.OsSelect, default, 0, 4, 24, 14);
        var b = UiKit.Button(GicLocalization.T(key), Fonts.Body, 36, Palette.OsInk, Colors.White, normal, hover);
        b.Alignment = HorizontalAlignment.Left;
        b.MouseEntered += () => b.GrabFocus();
        b.Pressed += action;
        box.Add(b);
        return b;
    }

    private static Button FooterButton(string text, Action action)
    {
        var normal = UiKit.Box(new Color(1, 1, 1, 0.08f), new Color(1, 1, 1, 0.3f), 1, 6, 22, 8);
        var hover = UiKit.Box(new Color(1, 1, 1, 0.2f), Colors.White, 1, 6, 22, 8);
        var b = UiKit.Button(text, Fonts.Body, 30, Colors.White, Colors.White, normal, hover);
        b.Pressed += action;
        return b;
    }

    public void FocusFirst() => _first?.CallDeferred(Control.MethodName.GrabFocus);
}
