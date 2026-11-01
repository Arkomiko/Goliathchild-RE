using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Godot;

namespace GicEngine.Runtime;

/// <summary>
/// Вычисление выражений сценария через встроенный класс Godot Expression.
/// Синтаксис как в GDScript: and/or/not, ==, !=, арифметика, методы строк ("abc".to_upper()).
/// Переменные истории передаются как входные значения, поэтому выражение не может трогать узлы игры —
/// это важно для пользовательских историй.
/// </summary>
public static class GicExpressions
{
    /// <summary>Вычисляет выражение. При ошибке возвращает null и пишет текст ошибки в error.</summary>
    public static object Evaluate(string expression, Dictionary<string, object> variables, GicExprApi api, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(expression))
        {
            error = "Пустое выражение";
            return null;
        }

        var names = new string[variables.Count];
        var values = new Godot.Collections.Array();
        var i = 0;
        foreach (var kv in variables)
        {
            names[i++] = kv.Key;
            values.Add(ToVariant(kv.Value));
        }

        var expr = new Expression();
        var parseError = expr.Parse(PrepareSyntax(expression), names);
        if (parseError != Error.Ok)
        {
            error = $"Ошибка в выражении «{expression}»: {expr.GetErrorText()}";
            return null;
        }
        var result = expr.Execute(values, api, false);
        if (expr.HasExecuteFailed())
        {
            error = $"Не удалось вычислить «{expression}»: {expr.GetErrorText()}";
            return null;
        }
        return FromVariant(result);
    }

    /// <summary>
    /// Небольшие удобства синтаксиса: true/false/none в любом регистре (как в Python у Ren'Py-авторов)
    /// превращаем в true/false/null, понятные Godot. Строки в кавычках не трогаем.
    /// </summary>
    private static string PrepareSyntax(string expression)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < expression.Length)
        {
            var c = expression[i];
            if (c == '"' || c == '\'')
            {
                var end = i + 1;
                while (end < expression.Length && expression[end] != c)
                {
                    if (expression[end] == '\\') end++;
                    end++;
                }
                sb.Append(expression, i, System.Math.Min(end + 1, expression.Length) - i);
                i = end + 1;
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < expression.Length && (char.IsLetterOrDigit(expression[i]) || expression[i] == '_')) i++;
                var word = expression.Substring(start, i - start);
                sb.Append(word switch
                {
                    "True" => "true",
                    "False" => "false",
                    "None" or "none" => "null",
                    _ => word,
                });
                continue;
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    /// <summary>Истинность значения как в Python/Ren'Py: пустая строка, 0, false и null — ложь.</summary>
    public static bool IsTruthy(object v) => v switch
    {
        null => false,
        bool b => b,
        long l => l != 0,
        double d => d != 0,
        string s => s.Length > 0,
        _ => true,
    };

    /// <summary>Значение переменной → Variant Godot.</summary>
    public static Variant ToVariant(object v) => v switch
    {
        null => default,
        bool b => b,
        long l => l,
        int i => (long)i,
        double d => d,
        float f => (double)f,
        string s => s,
        _ => v.ToString(),
    };

    /// <summary>Variant Godot → значение переменной (bool, long, double или string).</summary>
    public static object FromVariant(Variant v) => v.VariantType switch
    {
        Variant.Type.Nil => null,
        Variant.Type.Bool => v.AsBool(),
        Variant.Type.Int => v.AsInt64(),
        Variant.Type.Float => v.AsDouble(),
        Variant.Type.String or Variant.Type.StringName => v.AsString(),
        _ => v.ToString(),
    };

    /// <summary>Текстовое представление значения для подстановки [переменная] в реплики.</summary>
    public static string Format(object v) => v switch
    {
        null => "",
        bool b => b ? "true" : "false",
        double d => d.ToString(CultureInfo.InvariantCulture),
        _ => v.ToString(),
    };
}
