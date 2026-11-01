using System.Collections.Generic;
using System.Globalization;

namespace GicEngine.Script;

/// <summary>
/// Парсер формата сценариев .gics (GiC Script).
/// Синтаксис похож на Ren'Py, чтобы старый сценарий переносился почти один в один:
/// строки-команды, вложенность отступами, реплики вида <c>персонаж "текст"</c>.
/// Полное описание формата — docs/GICS_FORMAT.md.
/// </summary>
public static class GicsParser
{
    /// <summary>
    /// Зарезервированные слова. Персонажа нельзя назвать так же, иначе строка
    /// <c>music "файл"</c> была бы неотличима от реплики персонажа music.
    /// </summary>
    public static readonly HashSet<string> Keywords = new()
    {
        "label", "character", "var", "say", "doc", "scene", "show", "hide", "viewer",
        "music", "ambient", "sound", "voice", "stop", "pause", "glitch", "shake", "flash",
        "jump", "call", "return", "set", "if", "elif", "else", "menu", "input", "os",
        "checkpoint", "notify", "end",
    };

    /// <summary>Строка исходника после разбивки на лексемы, с вложенными строками-детьми.</summary>
    private sealed class LineNode
    {
        public int Line;
        public int Indent;
        public string Text;
        public List<Token> Tokens = new();
        public List<LineNode> Children = new();

        public bool EndsWithColon => Tokens.Count > 0 && Tokens[^1].Kind == TokenKind.Colon;
        public string First => Tokens.Count > 0 ? Tokens[0].Text : "";
    }

    /// <summary>Разобранные параметры команды: позиционные значения и именованные key=value.</summary>
    private sealed class Args
    {
        public readonly List<Token> Positional = new();
        public readonly Dictionary<string, Token> Named = new();

        public string Pos(int i) => i < Positional.Count ? Positional[i].Text : null;
        public string Get(string key) => Named.TryGetValue(key, out var t) ? t.Text : null;

        /// <summary>Есть ли среди позиционных слово-флаг (например wait у viewer).</summary>
        public bool HasFlag(string word)
        {
            foreach (var t in Positional)
            {
                if (t.Kind == TokenKind.Word && t.Text == word) return true;
            }
            return false;
        }
    }

    /// <summary>Разбирает текст файла. Ошибки не прерывают разбор: они попадают в Diagnostics, чтобы редактор показал все сразу.</summary>
    public static ScriptDocument Parse(string path, string source)
    {
        var doc = new ScriptDocument { Path = path };
        var root = BuildTree(path, source, doc.Diagnostics);

        foreach (var node in root.Children)
        {
            switch (node.First)
            {
                case "label":
                    ParseLabel(doc, node);
                    break;
                case "character":
                    ParseCharacter(doc, node);
                    break;
                case "var":
                    ParseVar(doc, node);
                    break;
                default:
                    Error(doc.Diagnostics, path, node.Line, "Команда вне метки. Сценарий должен лежать внутри «label имя:»");
                    break;
            }
        }
        return doc;
    }

    // ---------------------------------------------------------------- дерево отступов

    /// <summary>Превращает текст в дерево строк по отступам: каждая строка становится ребёнком ближайшей строки с меньшим отступом.</summary>
    private static LineNode BuildTree(string path, string source, List<Diagnostic> diags)
    {
        var root = new LineNode { Indent = -1, Line = 0 };
        var stack = new Stack<LineNode>();
        stack.Push(root);

        var lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            // Убираем BOM, который Блокнот Windows ставит в начало UTF-8 файлов.
            if (i == 0 && raw.Length > 0 && raw[0] == '﻿') raw = raw.Substring(1);

            var text = GicsLexer.StripComment(raw);
            if (string.IsNullOrWhiteSpace(text)) continue;

            var indent = MeasureIndent(text);
            var content = text.TrimStart();

            while (stack.Peek().Indent >= indent) stack.Pop();
            var parent = stack.Peek();

            // Отступ без двоеточия у родителя — частая ошибка; предупреждаем, но строку не теряем.
            if (parent != root && parent.Children.Count == 0 && !parent.EndsWithColon)
            {
                Error(diags, path, i + 1, "Лишний отступ: у предыдущей строки нет двоеточия в конце");
            }

            var errors = new List<string>();
            var node = new LineNode
            {
                Line = i + 1,
                Indent = indent,
                Text = content,
                Tokens = GicsLexer.Tokenize(content, errors),
            };
            foreach (var e in errors) Error(diags, path, i + 1, e);

            parent.Children.Add(node);
            stack.Push(node);
        }
        return root;
    }

    /// <summary>Считает отступ строки. Табуляция = 4 пробела, как в редакторе GiC_OS.</summary>
    private static int MeasureIndent(string text)
    {
        var n = 0;
        foreach (var c in text)
        {
            if (c == ' ') n++;
            else if (c == '\t') n += 4;
            else break;
        }
        return n;
    }

    // ---------------------------------------------------------------- верхний уровень

    private static void ParseLabel(ScriptDocument doc, LineNode node)
    {
        if (node.Tokens.Count < 2 || node.Tokens[1].Kind != TokenKind.Word)
        {
            Error(doc.Diagnostics, doc.Path, node.Line, "Ожидалось имя метки: label имя:");
            return;
        }
        if (!node.EndsWithColon)
        {
            Error(doc.Diagnostics, doc.Path, node.Line, "После имени метки нужно двоеточие");
        }
        var label = new LabelDef
        {
            Name = node.Tokens[1].Text,
            File = doc.Path,
            Line = node.Line,
        };
        label.Body = ParseBlock(doc, node.Children);
        doc.Labels.Add(label);
    }

    private static void ParseCharacter(ScriptDocument doc, LineNode node)
    {
        var args = ParseArgs(node.Tokens, 1);
        var id = args.Pos(0);
        if (id == null || args.Positional[0].Kind != TokenKind.Word)
        {
            Error(doc.Diagnostics, doc.Path, node.Line, "Ожидалось: character id \"Имя\" [color=#hex] [voice=\"файл\"]");
            return;
        }
        if (Keywords.Contains(id))
        {
            Error(doc.Diagnostics, doc.Path, node.Line, $"«{id}» — зарезервированное слово, персонажа так назвать нельзя");
            return;
        }
        var ch = new CharacterDef
        {
            Id = id,
            Name = args.Pos(1) ?? id,
            Color = args.Get("color") ?? "#000000",
            Voice = args.Get("voice"),
            VoiceVolume = ParseFloat(args.Get("voice_volume"), 1f),
            Info = args.Get("info"),
            File = doc.Path,
            Line = node.Line,
        };
        // Параметры вида sprite_грусть="путь" складываем в словарь спрайтов по эмоциям.
        foreach (var kv in args.Named)
        {
            if (kv.Key.StartsWith("sprite_")) ch.Sprites[kv.Key.Substring(7)] = kv.Value.Text;
            else if (kv.Key == "sprite") ch.Sprites["normal"] = kv.Value.Text;
        }
        doc.Characters.Add(ch);
    }

    private static void ParseVar(ScriptDocument doc, LineNode node)
    {
        if (!TrySplitAssignment(TailAfter(node, 1), out var name, out var expr))
        {
            Error(doc.Diagnostics, doc.Path, node.Line, "Ожидалось: var имя = выражение");
            return;
        }
        doc.Variables.Add(new VarDef { Name = name, Expression = expr, File = doc.Path, Line = node.Line });
    }

    // ---------------------------------------------------------------- блок команд

    /// <summary>Разбирает список строк одного уровня вложенности в список команд.</summary>
    private static List<Command> ParseBlock(ScriptDocument doc, List<LineNode> nodes)
    {
        var result = new List<Command>();
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.Tokens.Count == 0) continue;
            var first = node.Tokens[0];

            // "Текст" — реплика рассказчика.
            if (first.Kind == TokenKind.String)
            {
                result.Add(new SayCommand { Text = first.Text, File = doc.Path, Line = node.Line });
                WarnTrailing(doc, node, 1);
                continue;
            }
            // $ имя = выражение — короткая форма set, как в Ren'Py.
            if (first.Kind == TokenKind.Symbol && first.Text == "$")
            {
                result.Add(ParseSet(doc, node, TailAfter(node, 1)));
                continue;
            }
            if (first.Kind != TokenKind.Word)
            {
                Error(doc.Diagnostics, doc.Path, node.Line, $"Непонятная строка: {node.Text}");
                continue;
            }

            switch (first.Text)
            {
                case "if":
                    result.Add(ParseIf(doc, nodes, ref i));
                    break;
                case "elif":
                case "else":
                    Error(doc.Diagnostics, doc.Path, node.Line, $"«{first.Text}» без предшествующего if");
                    break;
                case "menu":
                    result.Add(ParseMenu(doc, node));
                    break;
                default:
                    var cmd = ParseSimple(doc, node);
                    if (cmd != null) result.Add(cmd);
                    break;
            }

            // У простых команд не должно быть вложенных строк.
            if (first.Text != "if" && first.Text != "menu" && node.Children.Count > 0)
            {
                Error(doc.Diagnostics, doc.Path, node.Children[0].Line, $"У команды «{first.Text}» не может быть вложенного блока");
            }
        }
        return result;
    }

    /// <summary>Разбирает одиночную команду без вложенного блока.</summary>
    private static Command ParseSimple(ScriptDocument doc, LineNode node)
    {
        var kw = node.Tokens[0].Text;
        var a = ParseArgs(node.Tokens, 1);
        Command cmd;

        switch (kw)
        {
            case "say":
                cmd = new SayCommand { Speaker = a.Pos(0), Text = a.Pos(1) ?? "" };
                break;
            case "doc":
                cmd = new DocCommand { Title = a.Pos(0) ?? "" };
                break;
            case "scene":
            {
                var path = a.Pos(0);
                cmd = new SceneCommand
                {
                    Path = path == "none" ? null : path,
                    Transition = a.Get("transition") ?? WordAt(a, 1) ?? "none",
                    Time = ParseFloat(a.Get("time"), 0.5f),
                };
                break;
            }
            case "show":
                cmd = new ShowCommand
                {
                    Tag = a.Pos(0),
                    Path = a.Pos(1),
                    X = ParseFloat(a.Get("x"), 0.5f),
                    Y = ParseFloat(a.Get("y"), 0.5f),
                    Transition = a.Get("transition") ?? WordAt(a, 2) ?? "none",
                    Time = ParseFloat(a.Get("time"), 0.5f),
                };
                if (((ShowCommand)cmd).Tag == null || ((ShowCommand)cmd).Path == null)
                {
                    Error(doc.Diagnostics, doc.Path, node.Line, "Ожидалось: show метка \"путь\" [transition=dissolve]");
                }
                break;
            case "hide":
                cmd = new HideCommand
                {
                    Tag = a.Pos(0),
                    Transition = a.Get("transition") ?? WordAt(a, 1) ?? "none",
                    Time = ParseFloat(a.Get("time"), 0.5f),
                };
                break;
            case "viewer":
                cmd = a.Pos(0) == "hide" && a.Positional[0].Kind == TokenKind.Word
                    ? new ViewerCommand { Hide = true }
                    : new ViewerCommand { Path = a.Pos(0), Wait = a.HasFlag("wait") };
                break;
            case "music":
            case "ambient":
            case "sound":
            case "voice":
                cmd = new AudioCommand
                {
                    Channel = kw,
                    Path = a.Pos(0),
                    Volume = ParseFloat(a.Get("volume"), 1f),
                    // Музыка и эмбиент по умолчанию зациклены, разовые звуки — нет.
                    Loop = ParseBool(a.Get("loop"), kw == "music" || kw == "ambient"),
                    Fade = ParseFloat(a.Get("fadein") ?? a.Get("fade"), 0f),
                };
                break;
            case "stop":
                cmd = new StopCommand
                {
                    Channel = a.Pos(0) ?? "all",
                    Fade = ParseFloat(a.Get("fadeout") ?? a.Get("fade"), 0f),
                };
                break;
            case "pause":
                cmd = new PauseCommand { Seconds = ParseFloat(a.Pos(0), -1f) };
                break;
            case "glitch":
                cmd = new EffectCommand
                {
                    Effect = "glitch",
                    Strength = ParseFloat(a.Get("strength") ?? a.Pos(0), 0.3f),
                    Time = ParseFloat(a.Get("time") ?? a.Pos(1), 0.2f),
                };
                break;
            case "shake":
                cmd = new EffectCommand
                {
                    Effect = "shake",
                    Strength = ParseFloat(a.Get("strength") ?? a.Pos(0), 12f),
                    Time = ParseFloat(a.Get("time") ?? a.Pos(1), 0.4f),
                };
                break;
            case "flash":
                cmd = new EffectCommand
                {
                    Effect = "flash",
                    Color = a.Get("color") ?? ColorAt(a, 0) ?? "#ffffff",
                    Time = ParseFloat(a.Get("time") ?? NumberAt(a, 0) ?? NumberAt(a, 1), 0.3f),
                };
                break;
            case "jump":
            case "call":
                cmd = new JumpCommand { Target = a.Pos(0), IsCall = kw == "call" };
                if (a.Pos(0) == null) Error(doc.Diagnostics, doc.Path, node.Line, $"Ожидалось: {kw} имя_метки");
                break;
            case "return":
                cmd = new ReturnCommand();
                break;
            case "end":
                cmd = new EndCommand();
                break;
            case "set":
                return ParseSet(doc, node, TailAfter(node, 1));
            case "input":
                cmd = new InputCommand
                {
                    Variable = a.Pos(0),
                    Prompt = a.Pos(1) ?? "",
                    Default = a.Get("default") ?? "",
                    MaxLength = (int)ParseFloat(a.Get("max"), 64),
                };
                if (a.Pos(0) == null) Error(doc.Diagnostics, doc.Path, node.Line, "Ожидалось: input переменная \"Подсказка\"");
                break;
            case "os":
            {
                var os = new OsCommand { Action = a.Pos(0) ?? "" };
                for (var k = 1; k < a.Positional.Count; k++) os.Args.Add(a.Positional[k].Text);
                foreach (var kv in a.Named) os.Options[kv.Key] = kv.Value.Text;
                if (os.Action == "") Error(doc.Diagnostics, doc.Path, node.Line, "Ожидалось: os действие ...");
                cmd = os;
                break;
            }
            case "checkpoint":
                cmd = new CheckpointCommand { Name = a.Pos(0) ?? "", Title = a.Pos(1) ?? a.Get("title") };
                break;
            case "notify":
                cmd = new NotifyCommand { Text = a.Pos(0) ?? "" };
                break;
            case "label":
            case "character":
            case "var":
                Error(doc.Diagnostics, doc.Path, node.Line, $"«{kw}» можно писать только на верхнем уровне файла");
                return null;
            default:
                // Не команда — значит, реплика персонажа: id "текст".
                if (node.Tokens.Count >= 2 && node.Tokens[1].Kind == TokenKind.String)
                {
                    cmd = new SayCommand { Speaker = kw, Text = node.Tokens[1].Text };
                    WarnTrailing(doc, node, 2);
                    break;
                }
                Error(doc.Diagnostics, doc.Path, node.Line, $"Неизвестная команда «{kw}»");
                return null;
        }

        cmd.File = doc.Path;
        cmd.Line = node.Line;
        return cmd;
    }

    /// <summary>Разбирает if/elif/else. Индекс i сдвигается на последнюю обработанную строку-ветку.</summary>
    private static IfCommand ParseIf(ScriptDocument doc, List<LineNode> nodes, ref int i)
    {
        var head = nodes[i];
        var cmd = new IfCommand { File = doc.Path, Line = head.Line };
        cmd.Branches.Add(new IfBranch
        {
            Condition = ConditionOf(doc, head),
            Line = head.Line,
            Body = ParseBlock(doc, head.Children),
        });

        // Собираем идущие следом elif/else того же уровня.
        while (i + 1 < nodes.Count)
        {
            var next = nodes[i + 1];
            if (next.First == "elif")
            {
                cmd.Branches.Add(new IfBranch
                {
                    Condition = ConditionOf(doc, next),
                    Line = next.Line,
                    Body = ParseBlock(doc, next.Children),
                });
                i++;
            }
            else if (next.First == "else")
            {
                cmd.Else = ParseBlock(doc, next.Children);
                i++;
                break;
            }
            else break;
        }
        return cmd;
    }

    /// <summary>Разбирает меню выбора: каждая вложенная строка "Вариант" [if условие]: — это вариант со своим блоком.</summary>
    private static MenuCommand ParseMenu(ScriptDocument doc, LineNode node)
    {
        var cmd = new MenuCommand { File = doc.Path, Line = node.Line };
        if (node.Tokens.Count > 1 && node.Tokens[1].Kind == TokenKind.String) cmd.Prompt = node.Tokens[1].Text;

        foreach (var child in node.Children)
        {
            if (child.Tokens.Count == 0 || child.Tokens[0].Kind != TokenKind.String)
            {
                Error(doc.Diagnostics, doc.Path, child.Line, "Вариант меню должен начинаться со строки в кавычках");
                continue;
            }
            var opt = new MenuOption { Text = child.Tokens[0].Text, Line = child.Line };
            // Необязательное условие: "Текст" if выражение:
            if (child.Tokens.Count > 1 && child.Tokens[1].Kind == TokenKind.Word && child.Tokens[1].Text == "if")
            {
                opt.Condition = StripColon(child.Text.Substring(child.Tokens[1].Start + 2)).Trim();
            }
            if (!child.EndsWithColon) Error(doc.Diagnostics, doc.Path, child.Line, "После варианта меню нужно двоеточие");
            opt.Body = ParseBlock(doc, child.Children);
            cmd.Options.Add(opt);
        }
        if (cmd.Options.Count == 0) Error(doc.Diagnostics, doc.Path, node.Line, "В меню нет ни одного варианта");
        return cmd;
    }

    /// <summary>Разбирает присвоение. Поддерживает = += -= *= /=, чтобы авторам было удобно считать очки и счётчики.</summary>
    private static Command ParseSet(ScriptDocument doc, LineNode node, string tail)
    {
        if (!TrySplitAssignment(tail, out var name, out var expr))
        {
            Error(doc.Diagnostics, doc.Path, node.Line, "Ожидалось: set имя = выражение");
            return null;
        }
        return new SetCommand { Variable = name, Expression = expr, File = doc.Path, Line = node.Line };
    }

    // ---------------------------------------------------------------- помощники

    /// <summary>Делит «имя оп= выражение» на имя и итоговое выражение.</summary>
    private static bool TrySplitAssignment(string tail, out string name, out string expr)
    {
        name = null;
        expr = null;
        if (string.IsNullOrWhiteSpace(tail)) return false;

        for (var i = 0; i < tail.Length; i++)
        {
            if (tail[i] != '=') continue;
            // Пропускаем ==, <=, >=, != — это сравнения, а не присвоение.
            if (i + 1 < tail.Length && tail[i + 1] == '=') return false;
            var op = i > 0 ? tail[i - 1] : ' ';
            var isCompound = op is '+' or '-' or '*' or '/';
            if (op is '<' or '>' or '!') return false;

            name = tail.Substring(0, isCompound ? i - 1 : i).Trim();
            var rhs = tail.Substring(i + 1).Trim();
            expr = isCompound ? $"{name} {op} ({rhs})" : rhs;
            return IsIdentifier(name) && rhs.Length > 0;
        }
        return false;
    }

    private static bool IsIdentifier(string s)
    {
        if (string.IsNullOrEmpty(s) || !(char.IsLetter(s[0]) || s[0] == '_')) return false;
        foreach (var c in s)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
        }
        return true;
    }

    /// <summary>Условие if/elif: «сырой» текст после ключевого слова без двоеточия.</summary>
    private static string ConditionOf(ScriptDocument doc, LineNode node)
    {
        if (node.Tokens.Count < 2) return "false";
        if (!node.EndsWithColon) Error(doc.Diagnostics, doc.Path, node.Line, "После условия нужно двоеточие");
        return StripColon(node.Text.Substring(node.Tokens[1].Start)).Trim();
    }

    private static string StripColon(string s)
    {
        s = s.TrimEnd();
        return s.EndsWith(":") ? s.Substring(0, s.Length - 1) : s;
    }

    /// <summary>Текст строки начиная с лексемы index — для выражений, которые нельзя разбивать на лексемы.</summary>
    private static string TailAfter(LineNode node, int index) =>
        index < node.Tokens.Count ? node.Text.Substring(node.Tokens[index].Start) : "";

    /// <summary>Собирает параметры: key=value — в Named, остальное — в Positional.</summary>
    private static Args ParseArgs(List<Token> tokens, int start)
    {
        var args = new Args();
        for (var i = start; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Kind == TokenKind.Colon) continue;
            // Слово, за которым идут = и значение, — именованный параметр.
            if (t.Kind == TokenKind.Word && i + 2 < tokens.Count && tokens[i + 1].Kind == TokenKind.Equals)
            {
                args.Named[t.Text] = tokens[i + 2];
                i += 2;
                continue;
            }
            args.Positional.Add(t);
        }
        return args;
    }

    private static string WordAt(Args a, int index) =>
        index < a.Positional.Count && a.Positional[index].Kind == TokenKind.Word ? a.Positional[index].Text : null;

    private static string NumberAt(Args a, int index) =>
        index < a.Positional.Count && a.Positional[index].Kind == TokenKind.Number ? a.Positional[index].Text : null;

    private static string ColorAt(Args a, int index) =>
        index < a.Positional.Count && a.Positional[index].Kind == TokenKind.Color ? a.Positional[index].Text : null;

    /// <summary>Число в формате с точкой независимо от языка системы (в русской локали десятичный разделитель — запятая).</summary>
    public static float ParseFloat(string s, float fallback) =>
        s != null && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static bool ParseBool(string s, bool fallback) => s switch
    {
        "true" or "yes" or "1" => true,
        "false" or "no" or "0" => false,
        _ => fallback,
    };

    /// <summary>Предупреждение, если после реплики остались лишние слова (скорее всего, опечатка в кавычках).</summary>
    private static void WarnTrailing(ScriptDocument doc, LineNode node, int expectedCount)
    {
        if (node.Tokens.Count > expectedCount)
        {
            doc.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, doc.Path, node.Line,
                "После реплики есть лишний текст — проверьте кавычки"));
        }
    }

    private static void Error(List<Diagnostic> diags, string file, int line, string message) =>
        diags.Add(new Diagnostic(DiagnosticSeverity.Error, file, line, message));
}
