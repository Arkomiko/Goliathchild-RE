using System.Collections.Generic;
using GicEngine.Script;
using Godot;

namespace GoliathChildRE;

/// <summary>
/// Инструмент разработчика: запуск игры из командной строки с автоматическими действиями.
/// Без параметров ничего не делает и сразу удаляется, поэтому на игроков не влияет.
///
/// Параметры (после «--» в командной строке Godot):
///   --validate              проверить основной сюжет, вывести ошибки и выйти (код выхода = число ошибок);
///   --continue              нажать «Продолжить» (загрузить самое свежее сохранение);
///   --open=ID               открыть: story, saves, gallery, settings, about, editor, game или chapter:метка;
///   --action=МЕТКА          кликнуть иконку/файл рабочего стола с этим действием (в игре);
///   --project=ПАПКА         в редакторе сразу открыть историю (main — основной сюжет);
///   --tab=N                 в редакторе переключиться на вкладку (0 граф, 1 таймлайн, 2 текст);
///   --preview=МЕТКА         в редакторе запустить предпросмотр с метки (снимок — окна предпросмотра);
///   --clicks=N              нажать «далее» N раз (в игре);
///   --delay=СЕК             сколько ждать перед снимком (по умолчанию 2);
///   --shot=ПУТЬ.png         сохранить снимок экрана (для editor — снимок окна редактора);
///   --quit                  выйти после снимка.
/// Пример: Godot.exe --path . -- --open=chapter:chapter_act1 --clicks=3 --shot=E:/shot.png --quit
/// </summary>
public partial class DevAutomation : Node
{
    private readonly Dictionary<string, string> _args = new();
    private Editor.PreviewWindow _preview;

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            var a = arg.TrimStart('-');
            var eq = a.IndexOf('=');
            _args[eq >= 0 ? a.Substring(0, eq) : a] = eq >= 0 ? a.Substring(eq + 1) : "true";
        }
        if (_args.Count == 0)
        {
            QueueFree();
            return;
        }
        Run();
    }

    private async void Run()
    {
        if (_args.ContainsKey("validate"))
        {
            var story = StoryProject.Load(GameManager.MainStoryRoot);
            var errors = 0;
            foreach (var d in story.Diagnostics)
            {
                GD.Print(d.ToString());
                if (d.Severity == DiagnosticSeverity.Error) errors++;
            }
            GD.Print($"Меток: {story.Labels.Count}, персонажей: {story.Characters.Count}, ошибок: {errors}, всего сообщений: {story.Diagnostics.Count}");
            GetTree().Quit(errors);
            return;
        }

        // Ждём, пока главное меню появится после затемнения.
        await Wait(1.2);

        if (_args.ContainsKey("continue"))
        {
            GameManager.Instance.ContinueLatest();
            await Wait(1.5);
        }

        if (_args.TryGetValue("open", out var open))
        {
            if (open == "editor")
            {
                GameManager.Instance.OpenEditor();
                if (_args.TryGetValue("project", out var project))
                {
                    await Wait(0.5);
                    GameManager.Instance.Editor?.OpenProject(project == "main" ? GameManager.MainStoryRoot : project);
                    if (_args.TryGetValue("tab", out var tab)) GameManager.Instance.Editor?.ShowTab(int.Parse(tab));
                    if (_args.TryGetValue("preview", out var previewLabel)) _preview = GameManager.Instance.Editor?.RunFrom(previewLabel);
                }
            }
            else if (open == "game") GameManager.Instance.NewGame();
            else if (open.StartsWith("chapter:")) GameManager.Instance.NewGame(open.Substring(8));
            else if (GetTree().Root.GetNode<Main>("Main").CurrentScreen is Menu.MainMenu menu) menu.OpenPage(open);
            await Wait(1.5);
        }

        if (_args.TryGetValue("action", out var action))
        {
            GameManager.Instance.Session?.Presenter.SimulateDesktopAction(action);
            await Wait(0.8);
        }

        if (_args.TryGetValue("clicks", out var clicksText) && int.TryParse(clicksText, out var clicks))
        {
            for (var i = 0; i < clicks; i++)
            {
                GameManager.Instance.Session?.Presenter.Advance();
                await Wait(0.6);
            }
        }

        await Wait(_args.TryGetValue("delay", out var d2) && double.TryParse(d2, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var delay) ? delay : 2.0);

        if (_args.TryGetValue("shot", out var shot))
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Viewport viewport = IsInstanceValid(_preview) ? _preview
                : open == "editor" && GameManager.Instance.Editor != null ? GameManager.Instance.Editor : GetViewport();
            var image = viewport.GetTexture().GetImage();
            image.SavePng(shot);
            GD.Print("Снимок сохранён: " + shot);
        }

        if (_args.ContainsKey("quit")) GetTree().Quit();
    }

    private SignalAwaiter Wait(double seconds) => ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
}
