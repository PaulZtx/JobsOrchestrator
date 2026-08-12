namespace Jobs.Enums;

/// <summary>
/// Режим восстановления из контрольной точки
/// </summary>
public enum CheckpointRestoreMode
{
    /// <summary>
    /// Восстановить существующую контрольную точку или создать новую
    /// </summary>
    ResumeOrCreate,

    /// <summary>
    /// Восстановить только существующую контрольную точку
    /// </summary>
    ResumeOnly,

    /// <summary>
    /// Создать новую контрольную точку
    /// </summary>
    CreateNew
}
