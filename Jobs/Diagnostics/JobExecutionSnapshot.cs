namespace Jobs.Diagnostics;

/// <summary>
/// Текущее состояние выполнения задания
/// </summary>
public enum JobExecutionState
{
    /// <summary>
    /// Задание выполняется
    /// </summary>
    Running,

    /// <summary>
    /// Задание получает сигнал отмены
    /// </summary>
    Cancelling,

    /// <summary>
    /// Задание успешно завершило все конвейеры
    /// </summary>
    Completed,

    /// <summary>
    /// Выполнение задания отменено
    /// </summary>
    Cancelled,

    /// <summary>
    /// Выполнение задания завершилось ошибкой
    /// </summary>
    Failed
}

/// <summary>
/// Снимок зарегистрированного внутреннего состояния задания
/// </summary>
/// <param name="Name">Имя состояния</param>
/// <param name="ItemType">Тип элементов</param>
/// <param name="ItemCount">Полное количество элементов</param>
/// <param name="Items">Сериализованные элементы для предпросмотра</param>
public sealed record JobStateSnapshot(
    string Name,
    string ItemType,
    int ItemCount,
    IReadOnlyList<string> Items);

/// <summary>
/// Диагностический снимок выполняемого задания
/// </summary>
/// <param name="JobId">Идентификатор попытки выполнения</param>
/// <param name="State">Текущее состояние выполнения</param>
/// <param name="ProcessedCount">Количество успешно обработанных сообщений</param>
/// <param name="LastProcessedAt">Время обработки последнего сообщения</param>
/// <param name="LastProcessedMessage">Последнее успешно обработанное входное сообщение</param>
/// <param name="States">Снимки зарегистрированных состояний</param>
/// <param name="ErrorMessage">Ошибка выполнения, если она возникла</param>
public sealed record JobExecutionSnapshot(
    Guid JobId,
    JobExecutionState State,
    long ProcessedCount,
    DateTimeOffset? LastProcessedAt,
    string? LastProcessedMessage,
    IReadOnlyList<JobStateSnapshot> States,
    string? ErrorMessage);

/// <summary>
/// Внутренний снимок одного конвейера
/// </summary>
/// <param name="ProcessedCount">Количество успешно обработанных сообщений</param>
/// <param name="LastProcessedAt">Время обработки последнего сообщения или null, если сообщений еще не было</param>
/// <param name="LastProcessedMessage">Диагностическое представление последнего обработанного сообщения или null, если сообщений еще не было</param>
internal sealed record PipelineExecutionSnapshot(
    long ProcessedCount,
    DateTimeOffset? LastProcessedAt,
    string? LastProcessedMessage);

/// <summary>
/// Внутренний агрегированный снимок среды выполнения
/// </summary>
/// <param name="ProcessedCount">Количество успешно обработанных сообщений</param>
/// <param name="LastProcessedAt">Время обработки последнего сообщения или null, если сообщений еще не было</param>
/// <param name="LastProcessedMessage">Диагностическое представление последнего обработанного сообщения или null, если сообщений еще не было</param>
/// <param name="States">Диагностические снимки зарегистрированных состояний задания</param>
internal sealed record JobRuntimeSnapshot(
    long ProcessedCount,
    DateTimeOffset? LastProcessedAt,
    string? LastProcessedMessage,
    IReadOnlyList<JobStateSnapshot> States);
