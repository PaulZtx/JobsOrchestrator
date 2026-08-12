using Jobs.Connectors;

namespace Jobs.Sources.Interfaces;

/// <summary>
/// Управляет чтением и обработкой данных одного источника
/// </summary>
public interface ISourceRunner
{
    /// <summary>
    /// Наименование источника
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Запускает чтение и обработку данных
    /// </summary>
    /// <param name="cancellationToken">Токен отмены</param>
    Task RunAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Приостанавливает прием новых записей и ожидает обработки принятых
    /// </summary>
    /// <param name="cancellationToken">Токен отмены</param>
    Task PauseAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Возобновляет прием новых записей
    /// </summary>
    Task ResumeAsync();

    /// <summary>
    /// Останавливает обработчик источника
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Возвращает текущую обработанную позицию источника
    /// </summary>
    /// <returns>Текущая позиция источника</returns>
    Task<SourcePosition> CaptureStateAsync();
}
