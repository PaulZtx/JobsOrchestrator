namespace Jobs;

/// <summary>
/// Статус выполнения запроса
/// </summary>
public class OrchestratorStatus
{
    /// <summary>
    /// Идентификатор задания
    /// </summary>
    public Guid? JobId { get; set; }

    /// <summary>
    /// Путь к файлу контрольной точки
    /// </summary>
    public string? CheckpointPath { get; set; }

    /// <summary>
    /// Сообщение об ошибке
    /// </summary>
    public string? ErrorMessage { get; set; }
}
