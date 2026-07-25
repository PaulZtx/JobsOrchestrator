using Jobs.Connectors;

namespace Jobs.JobsEntities.Interfaces;

/// <summary>
/// Контекст выполнения Job
/// </summary>
public interface IJobContext
{
    /// <summary>
    /// Чтение данных из источника
    /// </summary>
    /// <param name="source">Источник данных</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <typeparam name="T">Ожидаемый тип данных</typeparam>
    /// <returns></returns>
    IAsyncEnumerable<SourceRecord<T>> ReadAsync<T>(SourceHandle<T> source, CancellationToken cancellationToken);

    /// <summary>
    /// Запись в принимающий узел
    /// </summary>
    /// <param name="sink"></param>
    /// <param name="record"></param>
    /// <param name="cancellationToken"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task WriteAsync<T>(SinkHandle<T> sink, SinkRecord<T> record, CancellationToken cancellationToken);
}