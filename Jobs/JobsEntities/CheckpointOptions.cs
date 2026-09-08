using Jobs.Enums;

namespace Jobs.JobsEntities;

/// <summary>
/// Параметры создания и восстановления контрольных точек, настраиваемые заданием
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

    /// <summary>
    /// Путь к файлу контрольной точки
    /// </summary>
    public string? PathToCheckpoint { get; init; }

    /// <summary>
    /// Режим восстановления из контрольной точки
    /// </summary>
    public CheckpointRestoreMode RestoreMode { get; init; }
        = CheckpointRestoreMode.ResumeOrCreate;
}
