namespace Jobs.JobsEntities.Interfaces;

/// <summary>
/// Среда выполнения задания
/// </summary>
public interface IJobRuntime
{
    /// <summary>
    /// Запускает собранный граф задания
    /// </summary>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Задача выполнения задания до завершения конвейеров, отмены или ошибки</returns>
    Task RunAsync(CancellationToken cancellationToken);
}
