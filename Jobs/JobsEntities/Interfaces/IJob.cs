namespace Jobs.JobsEntities.Interfaces;

/// <summary>
/// Контракт Job
/// </summary>
public interface IJob
{
    /// <summary>
    /// Настраивает конвейеры задания
    /// </summary>
    /// <param name="builder">Построитель задания</param>
    void Configure(IJobBuilder builder);
}
