namespace Jobs.JobsEntities;

/// <summary>
/// Параметры создания контрольных точек
/// </summary>
public sealed class CheckpointOptions
{
    /// <summary>
    /// Признак включения контрольных точек
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Интервал между контрольными точками в миллисекундах
    /// </summary>
    public int DelayMillisecond { get; init; }
}
