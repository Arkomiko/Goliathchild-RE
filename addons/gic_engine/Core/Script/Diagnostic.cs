namespace GicEngine.Script;

/// <summary>Насколько серьёзна найденная проблема в сценарии.</summary>
public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// Сообщение о проблеме в сценарии: синтаксическая ошибка, переход на несуществующую метку, пропавший файл.
/// Редактор GiC_OS показывает их списком и по клику переходит к строке.
/// </summary>
public sealed class Diagnostic
{
    public DiagnosticSeverity Severity;
    public string File;
    public int Line;
    public string Message;

    public Diagnostic(DiagnosticSeverity severity, string file, int line, string message)
    {
        Severity = severity;
        File = file;
        Line = line;
        Message = message;
    }

    /// <summary>Текст для консоли и журнала: «файл:строка: сообщение».</summary>
    public override string ToString()
    {
        var prefix = Severity switch
        {
            DiagnosticSeverity.Error => "ОШИБКА",
            DiagnosticSeverity.Warning => "ВНИМАНИЕ",
            _ => "ИНФО",
        };
        return $"{prefix} {System.IO.Path.GetFileName(File)}:{Line}: {Message}";
    }
}
