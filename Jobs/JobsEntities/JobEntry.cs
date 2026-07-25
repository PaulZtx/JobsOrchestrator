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
    public IJob Job { get; set; }
    
    /// <summary>
    /// Токен отмены
    /// </summary>
    public CancellationTokenSource CancellationTokenSource { get; set; }
    
    /// <summary>
    /// Таска, в которой запущена джоба
    /// </summary>
    public Task JobTask { get; set; }
}