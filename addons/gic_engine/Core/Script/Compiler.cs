using System.Collections.Generic;

namespace GicEngine.Script;

/// <summary>Вид инструкции в плоском коде метки.</summary>
public enum OpCode
{
    Exec,       // выполнить команду (реплика, звук, os ...)
    JumpIfNot,  // перейти к Target, если условие ложно (так раскрываются if/elif)
    Goto,       // безусловный переход внутри метки
    Menu,       // показать меню; каждый вариант ведёт к своему Target
}

/// <summary>Вариант меню после компиляции: куда прыгать, если игрок его выбрал.</summary>
public sealed class MenuChoice
{
    public string Text;
    public string Condition;
    public int Target;
}

/// <summary>
/// Одна инструкция плоского кода.
/// Позиция исполнителя = (метка, номер инструкции). Это легко сохранять в файл и откатывать назад,
/// в отличие от позиции внутри дерева вложенных блоков.
/// </summary>
public sealed class Instruction
{
    public OpCode Op;
    public Command Command;
    public string Condition;
    public int Target;
    public List<MenuChoice> Choices;
    public string Prompt;
    public string File;
    public int Line;
}

/// <summary>Превращает дерево команд метки в плоский список инструкций с переходами.</summary>
public static class Compiler
{
    /// <summary>Компилирует тело метки и сохраняет результат в label.Code.</summary>
    public static void Compile(LabelDef label)
    {
        var code = new List<Instruction>();
        Emit(label.Body, code);
        label.Code = code;
    }

    private static void Emit(List<Command> commands, List<Instruction> code)
    {
        foreach (var cmd in commands)
        {
            switch (cmd)
            {
                case IfCommand ifc:
                    EmitIf(ifc, code);
                    break;
                case MenuCommand menu:
                    EmitMenu(menu, code);
                    break;
                default:
                    code.Add(new Instruction { Op = OpCode.Exec, Command = cmd, File = cmd.File, Line = cmd.Line });
                    break;
            }
        }
    }

    /// <summary>
    /// if A: x / elif B: y / else: z  превращается в:
    ///   JumpIfNot A → L1; x; Goto END; L1: JumpIfNot B → L2; y; Goto END; L2: z; END:
    /// </summary>
    private static void EmitIf(IfCommand ifc, List<Instruction> code)
    {
        var exits = new List<Instruction>();
        foreach (var branch in ifc.Branches)
        {
            var test = new Instruction { Op = OpCode.JumpIfNot, Condition = branch.Condition, File = ifc.File, Line = branch.Line };
            code.Add(test);
            Emit(branch.Body, code);
            var exit = new Instruction { Op = OpCode.Goto, File = ifc.File, Line = branch.Line };
            code.Add(exit);
            exits.Add(exit);
            test.Target = code.Count;
        }
        if (ifc.Else != null) Emit(ifc.Else, code);
        foreach (var e in exits) e.Target = code.Count;
    }

    /// <summary>Меню: инструкция Menu со списком целей, после каждого варианта — переход в конец меню.</summary>
    private static void EmitMenu(MenuCommand menu, List<Instruction> code)
    {
        var instr = new Instruction
        {
            Op = OpCode.Menu,
            Prompt = menu.Prompt,
            Choices = new List<MenuChoice>(),
            File = menu.File,
            Line = menu.Line,
        };
        code.Add(instr);

        var exits = new List<Instruction>();
        foreach (var opt in menu.Options)
        {
            instr.Choices.Add(new MenuChoice { Text = opt.Text, Condition = opt.Condition, Target = code.Count });
            Emit(opt.Body, code);
            var exit = new Instruction { Op = OpCode.Goto, File = menu.File, Line = opt.Line };
            code.Add(exit);
            exits.Add(exit);
        }
        foreach (var e in exits) e.Target = code.Count;
    }
}
