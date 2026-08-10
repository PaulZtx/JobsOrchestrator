namespace Jobs.JobsEntities.Interfaces;

/// <summary>
/// Runtime для обработки Jobs
/// </summary>
public interface IJobRuntime
{
    /// <summary>
    /// Запустить собранный граф Job
    /// </summary>
    Task RunAsync(CancellationToken cancellationToken);
}
