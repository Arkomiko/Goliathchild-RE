using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GicEngine.Runtime;

/// <summary>Событие внутри печатающегося текста: пауза на заданном символе.</summary>
public readonly struct TextPause
{
    // После скольких видимых символов срабатывает пауза.
    public readonly int CharIndex;
    // Длительность в секундах; отрицательное — ждать клика игрока.
    public readonly float Seconds;

    public TextPause(int charIndex, float seconds)
    {
        CharIndex = charIndex;
        Seconds = seconds;
    }
}

/// <summary>Реплика после разбора тегов: готовый BBCode для RichTextLabel и паузы для печатной машинки.</summary>
public sealed class MarkupResult
{
    public string Bbcode;
    public string Plain;
    public int CharCount;
    public List<TextPause> Pauses = new();
    // {nw} в конце — не ждать клика после реплики, сразу идти дальше.
    public bool NoWait;
}

/// <summary>
/// Разбор текстовых тегов реплик. Набор тегов совпадает с Ren'Py, поэтому старый сценарий переносится без правок:
/// {w=0.3} — пауза, {w} — ждать клик, {p=0.3} — новая строка и пауза, {p} — новая строка и клик, {nw} — без ожидания,
/// {b} {i} {u} {s} {size=N} {color=#hex} — оформление, [переменная] — подстановка, [[ и {{ — сами символы [ и {.
/// </summary>
public static class TextMarkup
{
    /// <param name="text">Исходный текст реплики.</param>
    /// <param name="lookup">Получение значения переменной по имени для [подстановок].</param>
    /// <param name="sizeScale">Множитель для {size=N}: старые размеры рассчитаны на 720p, игра — на 1440p.</param>
    public static MarkupResult Parse(string text, System.Func<string, string> lookup, float sizeScale = 1f)
    {
        var result = new MarkupResult();
        var bb = new StringBuilder();
        var plain = new StringBuilder();
        var count = 0;
        var i = 0;

        // Видимый символ: добавляем и в BBCode (с экранированием [), и в простой текст.
        void Visible(char c)
        {
            if (c == '[') bb.Append("[lb]");
            else bb.Append(c);
            plain.Append(c);
            count++;
        }

        while (i < text.Length)
        {
            var c = text[i];

            if (c == '[')
            {
                if (i + 1 < text.Length && text[i + 1] == '[')
                {
                    Visible('[');
                    i += 2;
                    continue;
                }
                var close = text.IndexOf(']', i + 1);
                if (close > i)
                {
                    var value = lookup?.Invoke(text.Substring(i + 1, close - i - 1).Trim()) ?? "";
                    foreach (var vc in value) Visible(vc);
                    i = close + 1;
                    continue;
                }
                Visible('[');
                i++;
                continue;
            }

            if (c == '{')
            {
                if (i + 1 < text.Length && text[i + 1] == '{')
                {
                    Visible('{');
                    i += 2;
                    continue;
                }
                var close = text.IndexOf('}', i + 1);
                if (close < 0)
                {
                    Visible('{');
                    i++;
                    continue;
                }
                ApplyTag(text.Substring(i + 1, close - i - 1), result, bb, plain, ref count, sizeScale);
                i = close + 1;
                continue;
            }

            Visible(c);
            i++;
        }

        result.Bbcode = bb.ToString();
        result.Plain = plain.ToString();
        result.CharCount = count;
        return result;
    }

    /// <summary>Применяет один тег {…}. Незнакомые теги молча пропускаем, чтобы опечатка не ломала реплику.</summary>
    private static void ApplyTag(string tag, MarkupResult result, StringBuilder bb, StringBuilder plain, ref int count, float sizeScale)
    {
        var eq = tag.IndexOf('=');
        var name = (eq >= 0 ? tag.Substring(0, eq) : tag).Trim();
        var arg = eq >= 0 ? tag.Substring(eq + 1).Trim() : null;

        switch (name)
        {
            case "w":
                result.Pauses.Add(new TextPause(count, ParseSeconds(arg)));
                break;
            case "p":
                // Пауза перед переносом строки: сначала дочитывается текущий абзац.
                result.Pauses.Add(new TextPause(count, ParseSeconds(arg)));
                bb.Append('\n');
                plain.Append('\n');
                count++;
                break;
            case "nw":
                result.NoWait = true;
                break;
            case "b": bb.Append("[b]"); break;
            case "/b": bb.Append("[/b]"); break;
            case "i": bb.Append("[i]"); break;
            case "/i": bb.Append("[/i]"); break;
            case "u": bb.Append("[u]"); break;
            case "/u": bb.Append("[/u]"); break;
            case "s": bb.Append("[s]"); break;
            case "/s": bb.Append("[/s]"); break;
            case "size":
                if (arg != null && float.TryParse(arg.TrimStart('+'), NumberStyles.Float, CultureInfo.InvariantCulture, out var size))
                {
                    bb.Append("[font_size=").Append((int)(size * sizeScale)).Append(']');
                }
                break;
            case "/size": bb.Append("[/font_size]"); break;
            case "color":
                if (arg != null) bb.Append("[color=").Append(arg).Append(']');
                break;
            case "/color": bb.Append("[/color]"); break;
        }
    }

    // Отсутствие числа ({w}, {p}) означает «ждать клика».
    private static float ParseSeconds(string arg) =>
        arg != null && float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : -1f;
}
