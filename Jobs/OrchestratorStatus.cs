namespace Jobs;

/// <summary>
/// Статус выполнения запроса
/// </summary>
public class OrchestratorStatus
{
    public Guid? JobId { get; set; }
    
    public string? ErrorMessage { get; set; }
}