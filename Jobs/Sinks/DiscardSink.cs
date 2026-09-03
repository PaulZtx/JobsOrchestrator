using Jobs.Connectors;
using Jobs.Connectors.Interfaces;

namespace Jobs.Sinks;

/// <summary>
/// Принимающий узел, который успешно отбрасывает все значения.
/// </summary>
internal sealed class DiscardSink<T> : IConnectorSink<T>
{
    private static readonly Task<bool> SuccessfulWrite = Task.FromResult(true);

    public static DiscardSink<T> Instance { get; } = new();

    private DiscardSink()
    {
    }

    public bool TryConnect() => true;

    public Task<bool> WriteAsync(SinkRecord<T> value, CancellationToken token) => SuccessfulWrite;
}
