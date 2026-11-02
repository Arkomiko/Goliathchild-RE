using System.Collections.Generic;

namespace GicEngine.Script;

// Дерево сценария (AST) после разбора файла .gics.
// Оно хранит структуру как в тексте: блоки if/menu вложены друг в друга.
// Редактор GiC_OS строит граф и таймлайн по этому дереву, а исполнитель получает
// из него плоский список инструкций (см. Compiler).

/// <summary>Базовый класс любой команды сценария. Помнит файл и строку, чтобы показывать ошибки и прыгать к тексту из графа.</summary>
public abstract class Command
{
    /// <summary>Путь к файлу сценария, из которого взята команда.</summary>
    public string File;

    /// <summary>Номер строки (с единицы), как его видит автор в редакторе.</summary>
    public int Line;

    /// <summary>Короткое имя команды для таймлайна и инспектора редактора.</summary>
    public abstract string Kind { get; }
}

/// <summary>Реплика: говорит персонаж (Speaker) или рассказчик (Speaker == null).</summary>
public sealed class SayCommand : Command
{
    public string Speaker;
    public string Text;
    public override string Kind => "say";
}

/// <summary>Заголовок окна-документа. Сахар для переменной doc: в оригинале это $hai.</summary>
public sealed class DocCommand : Command
{
    public string Title;
    public override string Kind => "doc";
}

/// <summary>Сменить фон сцены. Path == null очищает фон.</summary>
public sealed class SceneCommand : Command
{
    public string Path;
    public string Transition = "none";
    public float Time = 0.5f;
    public override string Kind => "scene";
}

/// <summary>Показать картинку с меткой Tag поверх рабочего стола (BIOS, титры, спрайты).</summary>
public sealed class ShowCommand : Command
{
    public string Tag;
    public string Path;
    // Позиция центра картинки в долях экрана: 0.5/0.5 — по центру.
    public float X = 0.5f;
    public float Y = 0.5f;
    public string Transition = "none";
    public float Time = 0.5f;
    public override string Kind => "show";
}

/// <summary>Убрать картинку с меткой Tag.</summary>
public sealed class HideCommand : Command
{
    public string Tag;
    public string Transition = "none";
    public float Time = 0.5f;
    public override string Kind => "hide";
}

/// <summary>Окно просмотра картинки (imageviewer в оригинале).</summary>
public sealed class ViewerCommand : Command
{
    public string Path;
    public bool Hide;
    // Wait — ждать, пока игрок сам закроет окно (call screen imageviewer в оригинале).
    public bool Wait;
    public override string Kind => "viewer";
}

/// <summary>Запуск звука на канале: music, ambient, sound или voice.</summary>
public sealed class AudioCommand : Command
{
    public string Channel;
    public string Path;
    public float Volume = 1f;
    public bool Loop = true;
    public float Fade;
    public override string Kind => Channel;
}

/// <summary>Остановить канал звука (или все каналы, если Channel == "all").</summary>
public sealed class StopCommand : Command
{
    public string Channel;
    public float Fade;
    public override string Kind => "stop";
}

/// <summary>Пауза в секундах. Отрицательное значение — ждать клика игрока.</summary>
public sealed class PauseCommand : Command
{
    public float Seconds = -1f;
    public override string Kind => "pause";
}

/// <summary>Экранный эффект: glitch, shake или flash.</summary>
public sealed class EffectCommand : Command
{
    public string Effect;
    public float Strength;
    public float Time;
    public string Color;
    public override string Kind => Effect;
}

/// <summary>Переход к метке. IsCall — с возвратом обратно по return.</summary>
public sealed class JumpCommand : Command
{
    public string Target;
    public bool IsCall;
    public override string Kind => IsCall ? "call" : "jump";
}

/// <summary>Возврат из call. Если стек пуст — история заканчивается.</summary>
public sealed class ReturnCommand : Command
{
    public override string Kind => "return";
}

/// <summary>Присвоение переменной: set имя = выражение (или $ имя = выражение).</summary>
public sealed class SetCommand : Command
{
    public string Variable;
    public string Expression;
    public override string Kind => "set";
}

/// <summary>Ветка условия: if или elif.</summary>
public sealed class IfBranch
{
    public string Condition;
    public int Line;
    public List<Command> Body = new();
}

/// <summary>Условие if / elif / else.</summary>
public sealed class IfCommand : Command
{
    public List<IfBranch> Branches = new();
    // Null, если ветки else нет.
    public List<Command> Else;
    public override string Kind => "if";
}

/// <summary>Вариант ответа в меню выбора.</summary>
public sealed class MenuOption
{
    public string Text;
    // Необязательное условие показа варианта.
    public string Condition;
    public int Line;
    public List<Command> Body = new();
}

/// <summary>Меню выбора для игрока.</summary>
public sealed class MenuCommand : Command
{
    public string Prompt;
    public List<MenuOption> Options = new();
    public override string Kind => "menu";
}

/// <summary>Ввод текста игроком (пароль от архива и т. п.).</summary>
public sealed class InputCommand : Command
{
    public string Variable;
    public string Prompt;
    public string Default = "";
    public int MaxLength = 64;
    public override string Kind => "input";
}

/// <summary>Команда виртуальной ОС: иконки, папки, окна ошибок, ожидание действия на рабочем столе.</summary>
public sealed class OsCommand : Command
{
    public string Action;
    public List<string> Args = new();
    public Dictionary<string, string> Options = new();
    public override string Kind => "os " + Action;

    /// <summary>Достаёт именованный параметр или значение по умолчанию.</summary>
    public string Opt(string key, string fallback = null) => Options.TryGetValue(key, out var v) ? v : fallback;

    /// <summary>Достаёт позиционный аргумент или значение по умолчанию.</summary>
    public string Arg(int index, string fallback = null) => index < Args.Count ? Args[index] : fallback;
}

/// <summary>Контрольная точка: автосохранение и открытие главы в меню «История».</summary>
public sealed class CheckpointCommand : Command
{
    public string Name;
    public string Title;
    public override string Kind => "checkpoint";
}

/// <summary>Всплывающее уведомление в углу экрана.</summary>
public sealed class NotifyCommand : Command
{
    public string Text;
    public override string Kind => "notify";
}

/// <summary>Конец истории: возврат в главное меню.</summary>
public sealed class EndCommand : Command
{
    public override string Kind => "end";
}

/// <summary>Описание персонажа (команда character).</summary>
public sealed class CharacterDef
{
    /// <summary>
    /// Встроенный рассказчик «centered», как в Ren'Py: реплика по центру экрана, без окна.
    /// Объявлять его не нужно; объявление с тем же id в сценарии заменит встроенное.
    /// </summary>
    public const string CenteredId = "centered";

    public static CharacterDef Centered() => new() { Id = CenteredId, Name = "", File = "", Line = 0 };

    /// <summary>Фон-цвет вместо картинки: scene "#ffffff". Нужен для заставок с текстом по центру.</summary>
    public static bool IsColorPath(string path) => path != null && path.StartsWith("#");

    public string Id;
    // Имя может содержать подстановку переменных, например "[doc]" — тогда в заголовке окна будет имя документа.
    public string Name;
    public string Color = "#000000";
    // «Голос» — мелодия, которая звучит, пока печатается реплика (фирменная механика Goliathchild).
    public string Voice;
    public float VoiceVolume = 1f;
    // Спрайты по эмоциям: "normal" → путь к картинке.
    public Dictionary<string, string> Sprites = new();
    // Свободное поле для сценарных особенностей персонажа.
    public string Info;
    public string File;
    public int Line;
}

/// <summary>Объявление переменной со значением по умолчанию (команда var).</summary>
public sealed class VarDef
{
    public string Name;
    public string Expression;
    public string File;
    public int Line;
}

/// <summary>Метка (label) — именованный кусок сценария, узел графа в редакторе.</summary>
public sealed class LabelDef
{
    public string Name;
    public string File;
    public int Line;
    public List<Command> Body = new();

    // Метка, на которую выполнение «проваливается», если тело не закончилось jump/return/end.
    // Так работает и Ren'Py, поэтому перенесённый сценарий ведёт себя одинаково.
    public string FallThrough;

    // Плоский список инструкций, который строит Compiler.
    public List<Instruction> Code;
}

/// <summary>Результат разбора одного файла .gics.</summary>
public sealed class ScriptDocument
{
    public string Path;
    public List<LabelDef> Labels = new();
    public List<CharacterDef> Characters = new();
    public List<VarDef> Variables = new();
    public List<Diagnostic> Diagnostics = new();
}
