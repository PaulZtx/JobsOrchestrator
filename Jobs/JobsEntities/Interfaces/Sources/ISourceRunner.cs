using Jobs.Connectors;

namespace Jobs.JobsEntities.Interfaces.Sources;

/// <summary>
/// Обработчик источника данных
/// </summary>
public interface ISourceRunner
{
    /// <summary>
    /// Наименование обработчика
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Запуск задачи
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task RunAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Приостановить обработчик
    /// </summary>
    /// <returns></returns>
    Task PauseAsync();
    
    /// <summary>
    /// Возобновить работу
    /// </summary>
    /// <returns></returns>
    Task ResumeAsync();
    
    /// <summary>
    /// Остановить выполнение
    /// </summary>
    /// <returns></returns>
    Task StopAsync();
    
    /// <summary>
    /// Зафиксировать состояние
    /// </summary>
    /// <returns></returns>
    Task<SourcePosition> CaptureStateAsync();
}
