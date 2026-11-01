using GicEngine.Localization;
using GicEngine.Save;
using GoliathChildRE.Session;
using GoliathChildRE.Ui;
using Godot;

namespace GoliathChildRE.Menu;

/// <summary>
/// Список сохранений в стиле Dishonored (ответ 33): все сохранения одним списком, новые сверху,
/// с пометкой типа (авто / быстрое / ручное), миниатюрой и последней репликой.
/// В главном меню можно загрузить и удалить; в игре — ещё создать новое и перезаписать выбранное.
/// </summary>
public partial class SavesPage : Control
{
    private readonly GameSession _session;
    private VBoxContainer _list;
    private TextureRect _thumb;
    private Label _title;
    private Label _details;
    private Label _quote;
    private Button _load;
    private Button _overwrite;
    private Button _delete;
    private SaveInfo _selected;

    public SavesPage(GameSession session)
    {
        _session = session;
    }

    public SavesPage() { }

    public override void _Ready()
    {
        var frame = PageFrame.Create(GicLocalization.T("menu.saves"), out var content);
        AddChild(frame);

        if (_session != null)
        {
            content.Add(UiKit.CinemaButton("+  " + GicLocalization.T("saves.new"), 36)).OnPressed(() =>
            {
                _session.SaveManual(null, Refresh);
            });
        }

        var row = UiKit.HBox(32);
        row.SizeFlagsVertical = SizeFlags.ExpandFill;
        content.Add(row);

        // Слева — прокручиваемый список.
        var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        row.Add(scroll);
        _list = UiKit.VBox(10);
        _list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.Add(_list);

        // Справа — подробности выбранного сохранения.
        var side = UiKit.VBox(16);
        side.CustomMinimumSize = new Vector2(480, 0);
        row.Add(side);
        _thumb = side.Add(new TextureRect
        {
            CustomMinimumSize = new Vector2(480, 270),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        });
        _title = side.Add(UiKit.Label("", Fonts.BodyBold, 34, Palette.Text));
        _details = side.Add(UiKit.Label("", Fonts.Mono, 26, Palette.TextMuted));
        _quote = side.Add(UiKit.Paragraph("", Fonts.Body, 26, Palette.TextMuted));
        // Длинная реплика не должна выталкивать кнопки за край панели.
        _quote.MaxLinesVisible = 3;
        _quote.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        side.Add(UiKit.Expander());
        _load = side.Add(UiKit.CinemaButton(GicLocalization.T("saves.load"), 34)).OnPressed(LoadSelected);
        if (_session != null)
        {
            _overwrite = side.Add(UiKit.CinemaButton(GicLocalization.T("saves.overwrite"), 34)).OnPressed(() =>
            {
                if (_selected == null) return;
                _session.SaveManual(_selected.FilePath, Refresh);
            });
        }
        _delete = side.Add(UiKit.CinemaButton(GicLocalization.T("saves.delete"), 34)).OnPressed(DeleteSelected);

        Refresh();
    }

    /// <summary>Перечитать список с диска.</summary>
    private void Refresh()
    {
        foreach (var child in _list.GetChildren()) child.QueueFree();
        var saves = SaveManager.Instance.List(GameManager.Instance.MainStory.Meta.Id);
        if (saves.Count == 0)
        {
            _list.Add(UiKit.Label(GicLocalization.T("saves.empty"), Fonts.Body, 32, Palette.TextMuted));
        }
        foreach (var info in saves)
        {
            var kind = info.Kind switch
            {
                SaveKind.Auto => GicLocalization.T("saves.kind_auto"),
                SaveKind.Quick => GicLocalization.T("saves.kind_quick"),
                _ => GicLocalization.T("saves.kind_manual"),
            };
            var b = UiKit.CinemaButton($"{kind,-8} {info.Title}\n         {info.Created:dd.MM.yyyy  HH:mm}", 28);
            var captured = info;
            b.Pressed += () => Select(captured);
            b.FocusEntered += () => Select(captured);
            _list.Add(b);
        }
        Select(saves.Count > 0 ? saves[0] : null);
    }

    private void Select(SaveInfo info)
    {
        // Смена выбора отменяет начатое подтверждение удаления.
        if (_confirmDelete && _delete != null)
        {
            _confirmDelete = false;
            _delete.Text = GicLocalization.T("saves.delete");
        }
        _selected = info;
        var has = info != null;
        _load.Disabled = !has;
        _delete.Disabled = !has;
        if (_overwrite != null) _overwrite.Disabled = !has || info.Kind != SaveKind.Manual;
        if (!has)
        {
            _thumb.Texture = null;
            _title.Text = _details.Text = _quote.Text = "";
            return;
        }
        _thumb.Texture = LoadThumb(info.Thumbnail);
        _title.Text = info.Title;
        var time = System.TimeSpan.FromSeconds(info.PlayTime);
        _details.Text = $"{info.Created:dd.MM.yyyy HH:mm:ss}\n{GicLocalization.T("saves.playtime")}: {(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}\nv{info.GameVersion}";
        _quote.Text = string.IsNullOrEmpty(info.LastText) ? "" : "«" + info.LastText + "»";
    }

    private static Texture2D LoadThumb(byte[] png)
    {
        if (png == null || png.Length == 0) return null;
        var image = new Image();
        return image.LoadPngFromBuffer(png) == Error.Ok ? ImageTexture.CreateFromImage(image) : null;
    }

    private void LoadSelected()
    {
        if (_selected == null) return;
        if (_session != null) _session.LoadSave(_selected);
        else GameManager.Instance.LoadGame(_selected);
    }

    // Удаление в два нажатия: первое спрашивает подтверждение прямо на кнопке, второе удаляет.
    private bool _confirmDelete;

    private void DeleteSelected()
    {
        if (_selected == null) return;
        if (!_confirmDelete)
        {
            _confirmDelete = true;
            _delete.Text = GicLocalization.T("saves.delete_confirm");
            return;
        }
        _confirmDelete = false;
        _delete.Text = GicLocalization.T("saves.delete");
        SaveManager.Instance.Delete(_selected.FilePath);
        Refresh();
    }
}
