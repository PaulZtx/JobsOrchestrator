using Jobs.Connectors;
using Jobs.States.Models;

namespace Jobs.JobsEntities;

/// <summary>
/// Содержимое файла контрольной точки
/// </summary>
internal sealed class CheckpointDocument
{
    /// <summary>
    /// Создает документ контрольной точки
    /// </summary>
    public CheckpointDocument()
    {
    }

    /// <summary>
    /// Версия формата контрольной точки
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// Время создания контрольной точки
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>
    /// Позиции источников
    /// </summary>
    public Dictionary<string, SourcePosition> Sources { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Снимки внутренних состояний
    /// </summary>
    public Dictionary<string, StateSnapshot> States { get; init; } = new(StringComparer.Ordinal);
}
