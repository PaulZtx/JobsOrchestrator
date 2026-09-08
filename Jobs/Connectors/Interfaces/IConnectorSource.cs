namespace Jobs.Connectors.Interfaces;

/// <summary>
/// Исходящий коннектор для элементов заданного типа
/// </summary>
/// <typeparam name="T">Тип читаемых значений</typeparam>
public interface IConnectorSource<T> : IConnector
{
    /// <summary>
    /// Последовательно читает элементы начиная с заданной позиции
    /// </summary>
    /// <param name="position">Начальная позиция чтения</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Асинхронная последовательность прочитанных элементов</returns>
    IAsyncEnumerable<SourceRecord<T>> ReadNextAsync(SourcePosition position, CancellationToken cancellationToken);

    /// <summary>
    /// Подтверждает обработку элементов до заданной позиции
    /// </summary>
    /// <param name="position">Следующая позиция после обработанных элементов</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Задача завершения подтверждения указанной позиции источника</returns>
    Task CommitAsync(SourcePosition position, CancellationToken cancellationToken);
}
