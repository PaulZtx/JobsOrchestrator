using Jobs.Enums;

namespace Jobs.JobsEntities;

/// <summary>
/// Параметры запуска задания с контрольной точкой
/// </summary>
public sealed class JobStartOptions
{
    /// <summary>
    /// Путь к файлу контрольной точки
    /// </summary>
    public string? CheckpointPath { get; init; }

    /// <summary>
    /// Режим восстановления из контрольной точки
    /// </summary>
    public CheckpointRestoreMode RestoreMode { get; init; }
        = CheckpointRestoreMode.ResumeOrCreate;
}
