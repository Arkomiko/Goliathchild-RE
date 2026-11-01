using System.Collections.Generic;
using System.Text;

namespace GicEngine.Script;

/// <summary>Вид лексемы в строке сценария.</summary>
public enum TokenKind
{
    Word,    // слово: имя команды, метки, переменной, флаг (wait, hide)
    String,  // строка в двойных кавычках, escape-последовательности уже раскрыты
    Number,  // число: 1, 0.3, -2
    Color,   // цвет: #RRGGBB или #RRGGBBAA
    Equals,  // знак = в параметрах вида key=value
    Colon,   // двоеточие в конце строки — начало вложенного блока
    Symbol,  // любой другой символ ($ и т. п.)
}

/// <summary>Одна лексема с позицией в строке. Позиция нужна, чтобы взять «сырой» хвост строки для выражений.</summary>
public readonly struct Token
{
    public readonly TokenKind Kind;
    public readonly string Text;
    public readonly int Start;

    public Token(TokenKind kind, string text, int start)
    {
        Kind = kind;
        Text = text;
        Start = start;
    }

    public override string ToString() => $"{Kind}:{Text}";
}

/// <summary>
/// Разбивает одну строку сценария .gics на лексемы.
/// Формат строчный (как в Ren'Py), поэтому лексер работает построчно, а отступы считает парсер.
/// </summary>
public static class GicsLexer
{
    /// <summary>
    /// Убирает комментарий из строки. Комментарий начинается с # в начале строки или после пробела,
    /// но не внутри строки в кавычках и не сразу после = (там это цвет, например color=#000000).
    /// </summary>
    public static string StripComment(string line)
    {
        var inString = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inString)
            {
                // Экранированную кавычку пропускаем, чтобы она не закрыла строку.
                if (c == '\\') { i++; continue; }
                if (c == '"') inString = false;
                continue;
            }
            if (c == '"') { inString = true; continue; }
            if (c == '#')
            {
                var prev = i == 0 ? ' ' : line[i - 1];
                if (char.IsWhiteSpace(prev)) return line.Substring(0, i).TrimEnd();
            }
        }
        return line.TrimEnd();
    }

    /// <summary>Разбивает строку (уже без отступа и комментария) на лексемы. Ошибки складывает в errors.</summary>
    public static List<Token> Tokenize(string text, List<string> errors)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            var start = i;
            if (c == '"')
            {
                tokens.Add(new Token(TokenKind.String, ReadString(text, ref i, errors), start));
                continue;
            }
            if (c == '#')
            {
                // Цвет: # и шестнадцатеричные цифры.
                i++;
                while (i < text.Length && System.Uri.IsHexDigit(text[i])) i++;
                tokens.Add(new Token(TokenKind.Color, text.Substring(start, i - start), start));
                continue;
            }
            if (char.IsDigit(c) || (c == '-' && i + 1 < text.Length && char.IsDigit(text[i + 1])) ||
                (c == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1])))
            {
                i++;
                while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.')) i++;
                tokens.Add(new Token(TokenKind.Number, text.Substring(start, i - start), start));
                continue;
            }
            if (IsWordStart(c))
            {
                i++;
                while (i < text.Length && IsWordPart(text[i])) i++;
                tokens.Add(new Token(TokenKind.Word, text.Substring(start, i - start), start));
                continue;
            }
            if (c == '=')
            {
                // Двойное == встречается только внутри выражений, а их парсер берёт «сырым» текстом.
                i++;
                tokens.Add(new Token(TokenKind.Equals, "=", start));
                continue;
            }
            if (c == ':')
            {
                i++;
                tokens.Add(new Token(TokenKind.Colon, ":", start));
                continue;
            }
            i++;
            tokens.Add(new Token(TokenKind.Symbol, c.ToString(), start));
        }
        return tokens;
    }

    /// <summary>Читает строку в кавычках. Раскрывает \" \\ \n \t; остальные символы (теги {p}, [переменные]) оставляет как есть.</summary>
    private static string ReadString(string text, ref int i, List<string> errors)
    {
        var sb = new StringBuilder();
        i++; // пропускаем открывающую кавычку
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length)
            {
                var n = text[i + 1];
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    // Неизвестную последовательность оставляем как есть, чтобы не терять текст автора.
                    default: sb.Append('\\').Append(n); break;
                }
                i += 2;
                continue;
            }
            if (c == '"')
            {
                i++;
                return sb.ToString();
            }
            sb.Append(c);
            i++;
        }
        errors.Add("Не закрыта кавычка в строке");
        return sb.ToString();
    }

    // Слова могут быть на латинице и кириллице: авторы-фанаты будут называть метки по-русски.
    private static bool IsWordStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsWordPart(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '.';
}
