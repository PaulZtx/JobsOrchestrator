namespace Jobs.Connectors;

public interface IConnectorSource<T> : IConnector
{
    /// <summary>
    /// Попытка чтения очередного элемента
    /// </summary>
    /// <returns></returns>
    IAsyncEnumerable<SourceRecord<T>> ReadNextAsync(SourcePosition position, CancellationToken cancellationToken);
}