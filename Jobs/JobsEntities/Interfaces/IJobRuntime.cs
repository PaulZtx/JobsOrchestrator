namespace Jobs.JobsEntities.Interfaces;

public interface IJobRuntime
{
    /// <summary>
    /// Запустить собранный граф Job.
    /// </summary>
    Task RunAsync(CancellationToken cancellationToken);
}
