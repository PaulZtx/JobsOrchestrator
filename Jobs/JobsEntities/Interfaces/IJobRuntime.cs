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
    Task RunAsync(CancellationToken cancellationToken);
}
