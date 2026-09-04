using Jobs.Connectors;
using Jobs.Connectors.Interfaces;

namespace Jobs.Sinks;

/// <summary>
/// Принимающий узел, который успешно отбрасывает все значения
/// </summary>
/// <typeparam name="T">Тип принимаемых значений</typeparam>
internal sealed class DiscardSink<T> : IConnectorSink<T>
{
    private static readonly Task<bool> SuccessfulWrite = Task.FromResult(true);

    /// <summary>
    /// Единственный экземпляр принимающего узла
    /// </summary>
    public static DiscardSink<T> Instance { get; } = new();

    /// <summary>
    /// Создает принимающий узел
    /// </summary>
    private DiscardSink()
    {
    }

    /// <inheritdoc />
    public bool TryConnect() => true;

    /// <inheritdoc />
    public Task<bool> WriteAsync(SinkRecord<T> value, CancellationToken token) => SuccessfulWrite;
}
