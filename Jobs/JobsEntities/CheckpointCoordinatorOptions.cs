namespace Jobs.JobsEntities;

/// <summary>
/// Параметры координатора контрольных точек
/// </summary>
public sealed class CheckpointCoordinatorOptions
{
    /// <summary>
    /// Параметры запуска задания
    /// </summary>
    public required JobStartOptions JobStartOptions { get; init; }

    /// <summary>
    /// Параметры создания контрольных точек
    /// </summary>
    public required CheckpointOptions CheckpointOptions { get; init; }
}
