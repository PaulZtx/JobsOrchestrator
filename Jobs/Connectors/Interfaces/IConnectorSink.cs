namespace Jobs.Connectors.Interfaces;

public interface IConnectorSink<T> : IConnector
{
    /// <summary>
    /// Попытка записи в принимающий узел
    /// </summary>
    /// <returns></returns>
    Task<bool> WriteAsync(SinkRecord<T> value, CancellationToken token);
}