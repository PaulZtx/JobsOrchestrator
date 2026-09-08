using Jobs.Connectors;
using Jobs.Diagnostics;

namespace Jobs.Pipelines.Interfaces;

/// <summary>
/// Управляет чтением, обработкой и записью одного конвейера
/// </summary>
internal interface IPipelineRunner
{
    /// <summary>
    /// Имя источника
    /// </summary>
    string SourceName { get; }

    /// <summary>
    /// Запускает конвейер
    /// </summary>
    /// <param name="cancellationToken">Токен отмены</param>
    Task RunAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Приостанавливает чтение и ожидает завершения обработки
    /// </summary>
    /// <param name="cancellationToken">Токен отмены</param>
    Task PauseAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Возобновляет чтение
    /// </summary>
    Task ResumeAsync();

    /// <summary>
    /// Останавливает конвейер
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Возвращает текущую позицию источника
    /// </summary>
    /// <returns>Позиция следующего элемента</returns>
    Task<SourcePosition> CaptureStateAsync();

    /// <summary>
    /// Возвращает текущие счетчики обработки конвейера.
    /// </summary>
    PipelineExecutionSnapshot CaptureDiagnostics();
}
