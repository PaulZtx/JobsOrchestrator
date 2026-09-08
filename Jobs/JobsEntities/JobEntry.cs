using Jobs.JobsEntities.Interfaces;

namespace Jobs.JobsEntities;

/// <summary>
/// Сведения о запущенном задании
/// </summary>
public class JobEntry
{
    /// <summary>
    /// Идентификатор задания
    /// </summary>
    public Guid JobId { get; set; }
    
    /// <summary>
    /// Реализация задания
    /// </summary>
    public required IJob Job { get; set; }
    
    /// <summary>
    /// Токен отмены
    /// </summary>
    public required CancellationTokenSource CancellationTokenSource { get; set; }
    
    /// <summary>
    /// Среда выполнения текущей попытки задания
    /// </summary>
    public required IJobRuntime Runtime { get; set; }

    /// <summary>
    /// Задача с выполняемым заданием
    /// </summary>
    public required Task JobTask { get; set; }
}
