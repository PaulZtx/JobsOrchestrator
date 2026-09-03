namespace Jobs.Pipelines;

/// <summary>
/// Неизменяемый контекст выполняемого обработчика.
/// </summary>
/// <param name="SourceName">Имя источника</param>
/// <param name="ProcessName">Имя обработчика</param>
/// <param name="SinkName">Имя принимающего узла</param>
public sealed record ProcessContext(
    string SourceName,
    string ProcessName,
    string SinkName);

/// <summary>
/// Технический тип результата обработчика, который не производит выходное значение.
/// </summary>
public readonly record struct NoOutput
{
    public static NoOutput Value => default;
}
