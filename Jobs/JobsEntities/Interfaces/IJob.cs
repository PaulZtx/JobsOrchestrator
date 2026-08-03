namespace Jobs.JobsEntities.Interfaces;

/// <summary>
/// Контракт Job
/// </summary>
public interface IJob
{
    /// <summary>
    /// Конфигурация Job
    /// </summary>
    /// <param name="builder"></param>
    void Configure(IJobBuilder builder);
}
