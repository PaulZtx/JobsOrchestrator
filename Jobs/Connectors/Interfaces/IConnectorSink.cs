namespace Jobs.Connectors.Interfaces;

/// <summary>
/// Принимающий коннектор для элементов заданного типа
/// </summary>
/// <typeparam name="T">Тип принимаемых значений</typeparam>
public interface IConnectorSink<T> : IConnector
{
    /// <summary>
    /// Записывает элемент в принимающий узел
    /// </summary>
    /// <param name="value">Записываемый элемент</param>
    /// <param name="token">Токен отмены</param>
    /// <returns>Признак успешной записи</returns>
    Task<bool> WriteAsync(SinkRecord<T> value, CancellationToken token);
}
