using System;
using System.Collections.Generic;
using GicEngine.Script;

namespace GicEngine.Runtime;

/// <summary>Всё, что нужно презентеру, чтобы показать реплику.</summary>
public sealed class SayRequest
{
    // Персонаж или null для рассказчика.
    public CharacterDef Character;
    // Имя для заголовка окна (уже с подставленными переменными).
    public string Name;
    // Исходный текст с тегами; презентер разбирает его через TextMarkup.
    public string Text;
    // Видел ли игрок эту реплику раньше (для пропуска только прочитанного).
    public bool Seen;
    // Ключ реплики «файл:строка» для истории реплик и списка прочитанного.
    public string Key;
}

/// <summary>
/// Презентер — «внешний вид» истории. Исполнитель решает, ЧТО происходит, презентер — КАК это выглядит.
/// Основной сюжет показывает рабочий стол виртуальной ОС (DesktopPresenter), позже появится классический ВН-презентер.
/// Методы с колбэком блокируют историю, пока колбэк не будет вызван.
/// </summary>
public interface IStoryPresenter
{
    /// <summary>Полностью перерисовать экран по состоянию (после загрузки сохранения или отката назад).</summary>
    void ApplyPresentation(PresentationState state);

    /// <summary>Отменить все ожидания (печать текста, паузы, окна) — перед откатом или загрузкой.</summary>
    void CancelPending();

    /// <summary>Показать реплику и вызвать done, когда игрок её дочитал.</summary>
    void Say(SayRequest request, Action done);

    void SetScene(string path, string transition, float time, Action done);
    void ShowImage(ImageState image, string transition, float time, Action done);
    void HideImage(string tag, string transition, float time, Action done);

    /// <summary>Окно просмотра картинки. closed вызывается, когда игрок закрыл окно.</summary>
    void ShowViewer(string path, Action closed);
    void HideViewer();

    void PlayAudio(string channel, AudioState audio, float fadeIn);
    void StopAudio(string channel, float fadeOut);

    /// <summary>Экранный эффект (glitch, shake, flash). done — когда эффект закончился.</summary>
    void Effect(EffectCommand effect, Action done);

    /// <summary>Пауза. seconds &lt; 0 — ждать клика игрока.</summary>
    void Wait(float seconds, Action done);

    void AskInput(string prompt, string defaultText, int maxLength, Action<string> done);
    void AskMenu(string prompt, List<string> options, Action<int> done);

    /// <summary>Рабочий стол изменился — перерисовать иконки, папки, часы.</summary>
    void RefreshDesktop(DesktopState desktop);

    /// <summary>Показать окно ошибки ОС с картинкой или текстом.</summary>
    void ShowError(string image, string title, string message);

    /// <summary>Ждать действия игрока на рабочем столе. action вызывается с меткой, на которую надо перейти.</summary>
    void WaitForDesktopAction(Action<string> action);

    void Notify(string text);

    /// <summary>Контрольная точка пройдена: игре пора сделать автосохранение и открыть главу.</summary>
    void Checkpoint(string name, string title);

    /// <summary>Ошибка исполнения (неизвестная метка, ошибка в выражении). Игра пишет в журнал, редактор — в консоль.</summary>
    void ReportError(string file, int line, string message);

    /// <summary>История закончилась (команда end или return из главной метки).</summary>
    void StoryEnded();
}
