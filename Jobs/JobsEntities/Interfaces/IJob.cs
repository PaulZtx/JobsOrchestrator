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

    /// <summary>
    /// Выполнение основного блока
    /// </summary>
    /// <param name="context">Контекст Job</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns></returns>
    Task ExecuteAsync(IJobContext context, CancellationToken cancellationToken);
}