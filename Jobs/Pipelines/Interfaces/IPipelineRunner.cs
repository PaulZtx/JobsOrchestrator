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
    /// <returns>Задача выполнения конвейера до завершения чтения и обработки, отмены или ошибки</returns>
    Task RunAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Приостанавливает чтение и ожидает завершения обработки
    /// </summary>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Задача, завершающаяся после приостановки чтения и обработки всех уже принятых элементов</returns>
    Task PauseAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Возобновляет чтение
    /// </summary>
    /// <returns>Задача завершения снятия паузы чтения источника</returns>
    Task ResumeAsync();

    /// <summary>
    /// Останавливает конвейер
    /// </summary>
    /// <returns>Задача завершения остановки рабочих задач конвейера</returns>
    Task StopAsync();

    /// <summary>
    /// Возвращает текущую позицию источника
    /// </summary>
    /// <returns>Позиция следующего элемента</returns>
    Task<SourcePosition> CaptureStateAsync();

    /// <summary>
    /// Возвращает текущие счетчики обработки конвейера
    /// </summary>
    /// <returns>Снимок количества обработанных сообщений, времени и содержимого последнего сообщения</returns>
    PipelineExecutionSnapshot CaptureDiagnostics();
}
