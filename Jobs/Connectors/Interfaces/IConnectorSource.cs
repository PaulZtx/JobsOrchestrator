namespace Jobs.Connectors.Interfaces;

public interface IConnectorSource<T> : IConnector
{
    /// <summary>
    /// Попытка чтения очередного элемента
    /// </summary>
    /// <returns></returns>
    IAsyncEnumerable<SourceRecord<T>> ReadNextAsync(SourcePosition position, CancellationToken cancellationToken);
    
    Task CommitAsync(SourcePosition position, CancellationToken cancellationToken);
}