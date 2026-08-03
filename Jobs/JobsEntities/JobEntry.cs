using Jobs.JobsEntities.Interfaces;

namespace Jobs.JobsEntities;

/// <summary>
/// Фактическая единица Job
/// </summary>
public class JobEntry
{
    /// <summary>
    /// Идентификатор Job
    /// </summary>
    public Guid JobId { get; set; }
    
    /// <summary>
    /// Реализация Job
    /// </summary>
    public required IJob Job { get; set; }
    
    /// <summary>
    /// Токен отмены
    /// </summary>
    public required CancellationTokenSource CancellationTokenSource { get; set; }
    
    /// <summary>
    /// Runtime текущей попытки выполнения Job
    /// </summary>
    public required IJobRuntime Runtime { get; set; }

    /// <summary>
    /// Таска, в которой запущена джоба
    /// </summary>
    public required Task JobTask { get; set; }
}
