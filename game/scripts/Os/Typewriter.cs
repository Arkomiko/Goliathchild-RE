using System;
using GicEngine.Runtime;
using Godot;

namespace GoliathChildRE.Os;

/// <summary>
/// Печатная машинка для реплик: показывает текст посимвольно с заданной скоростью,
/// выдерживает паузы из тегов {w} и {p} и ждёт клика там, где это задано в сценарии.
/// </summary>
public partial class Typewriter : RichTextLabel
{
    /// <summary>Текст показан полностью (с учётом пауз в конце).</summary>
    public event Action TypingDone;

    /// <summary>Печать остановилась и ждёт клика ({w} или {p} без числа).</summary>
    public event Action WaitingForClick;

    /// <summary>Печать продолжилась после паузы или клика.</summary>
    public event Action Resumed;

    // Скорость в символах в секунду; float.PositiveInfinity — мгновенно.
    public float Speed = 45f;

    private MarkupResult _markup;
    private double _shown;
    private int _pauseIndex;
    private double _pauseLeft;
    private bool _waitingClick;

    public bool IsTyping { get; private set; }

    public Typewriter()
    {
        BbcodeEnabled = true;
        ScrollActive = true;
        ScrollFollowing = true;
        VisibleCharactersBehavior = TextServer.VisibleCharactersBehavior.CharsAfterShaping;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>Начать печать новой реплики (предыдущий текст заменяется).</summary>
    public void Start(MarkupResult markup)
    {
        _markup = markup;
        Text = markup.Bbcode;
        VisibleCharacters = 0;
        _shown = 0;
        _pauseIndex = 0;
        _pauseLeft = 0;
        _waitingClick = false;
        IsTyping = true;
    }

    /// <summary>Клик игрока: продолжить после паузы «ждать клика» или показать всё сразу.</summary>
    public void Advance()
    {
        if (!IsTyping) return;
        if (_waitingClick)
        {
            _waitingClick = false;
            _pauseIndex++;
            Resumed?.Invoke();
            return;
        }
        RevealAll();
    }

    /// <summary>Показать текст целиком без пауз (клик во время печати, режим пропуска).</summary>
    public void RevealAll()
    {
        if (!IsTyping) return;
        VisibleCharacters = -1;
        Finish();
    }

    /// <summary>Остановить печать без события TypingDone (откат, загрузка).</summary>
    public void Stop()
    {
        IsTyping = false;
        _markup = null;
    }

    /// <summary>Остановить печать и очистить окно (после загрузки сохранения).</summary>
    public void ClearText()
    {
        Stop();
        Text = "";
    }

    private void Finish()
    {
        IsTyping = false;
        _waitingClick = false;
        TypingDone?.Invoke();
    }

    public override void _Process(double delta)
    {
        if (!IsTyping || _markup == null || _waitingClick) return;

        if (_pauseLeft > 0)
        {
            _pauseLeft -= delta;
            if (_pauseLeft > 0) return;
            _pauseIndex++;
            Resumed?.Invoke();
        }

        _shown += float.IsPositiveInfinity(Speed) ? _markup.CharCount + 1 : Speed * delta;

        // Следующая пауза останавливает печать на своём символе.
        if (_pauseIndex < _markup.Pauses.Count)
        {
            var pause = _markup.Pauses[_pauseIndex];
            if (_shown >= pause.CharIndex)
            {
                _shown = pause.CharIndex;
                VisibleCharacters = pause.CharIndex;
                if (pause.Seconds < 0)
                {
                    _waitingClick = true;
                    WaitingForClick?.Invoke();
                }
                else
                {
                    _pauseLeft = pause.Seconds;
                    // Нулевая пауза сразу пропускается в следующем кадре.
                    if (_pauseLeft <= 0) _pauseLeft = 0.0001;
                }
                return;
            }
        }

        if (_shown >= _markup.CharCount)
        {
            VisibleCharacters = -1;
            Finish();
            return;
        }
        VisibleCharacters = (int)_shown;
    }
}
