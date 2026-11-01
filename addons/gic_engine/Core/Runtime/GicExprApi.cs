using System.Text;
using Godot;

namespace GicEngine.Runtime;

/// <summary>
/// Функции, доступные авторам внутри выражений if/set: <c>if normalize(password) == "cogito ergo sum":</c>.
/// Godot Expression вызывает методы этого объекта по имени, поэтому имена методов написаны строчными
/// буквами — так их удобнее набирать в сценарии, и они похожи на функции GDScript.
/// </summary>
public partial class GicExprApi : GodotObject
{
    // Исполнитель нужен функциям visited/seen; null, если выражение вычисляется вне истории (например, в редакторе).
    internal StoryPlayer Player;

    /// <summary>Строка в нижнем регистре.</summary>
    public string lower(string s) => s?.ToLowerInvariant() ?? "";

    /// <summary>Строка в верхнем регистре.</summary>
    public string upper(string s) => s?.ToUpperInvariant() ?? "";

    /// <summary>
    /// «Нормализация» ответа игрока: нижний регистр, без знаков препинания и лишних пробелов, ё → е.
    /// Нужна для паролей: «Cogito, ergo sum», «cogito ergo sum» и «COGITO ERGO SUM» считаются одним ответом.
    /// </summary>
    public string normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder();
        var lastSpace = true;
        foreach (var c in s.ToLowerInvariant().Replace('ё', 'е'))
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                lastSpace = false;
            }
            else if (!lastSpace)
            {
                sb.Append(' ');
                lastSpace = true;
            }
        }
        return sb.ToString().Trim();
    }

    /// <summary>Содержит ли строка подстроку (без учёта регистра).</summary>
    public bool contains(string s, string part) =>
        s != null && part != null && s.Contains(part, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>Длина строки.</summary>
    public int length(string s) => s?.Length ?? 0;

    /// <summary>Случайное целое от a до b включительно.</summary>
    public int rand(int a, int b) => (int)GD.RandRange(a, b);

    /// <summary>Случайное событие с вероятностью p (от 0 до 1).</summary>
    public bool chance(double p) => GD.Randf() < p;

    /// <summary>Посещал ли игрок метку в этом прохождении.</summary>
    public bool visited(string label) => Player != null && Player.State.VisitedLabels.Contains(label);
}
