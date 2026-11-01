using GicEngine.IO;
using GoliathChildRE.Ui;
using Godot;

namespace GoliathChildRE.Menu;

/// <summary>Просмотр картинки из галереи на весь экран. Закрывается кликом, Esc или кнопкой геймпада.</summary>
public partial class ImageOverlay : CanvasLayer
{
    private string _path;

    /// <summary>Показать картинку поверх всего.</summary>
    public static void Show(Node root, string path)
    {
        root.AddChild(new ImageOverlay { _path = path, Layer = 90 });
    }

    public override void _Ready()
    {
        var dim = UiKit.Full(new ColorRect { Color = new Color(0, 0, 0, 0.92f) });
        AddChild(dim);
        var image = UiKit.Full(new TextureRect
        {
            Texture = GicAssets.LoadTexture(_path),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        image.OffsetLeft = image.OffsetTop = 60;
        image.OffsetRight = image.OffsetBottom = -60;
        AddChild(image);
        dim.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true }) QueueFree();
        };
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("ui_cancel") || e.IsActionPressed("ui_accept"))
        {
            GetViewport().SetInputAsHandled();
            QueueFree();
        }
    }
}
