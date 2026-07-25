using Jobs.Connectors;

namespace Jobs.JobsEntities.Interfaces;

public interface IJobRuntime
{
    /// <summary>
    /// Прочитать данные
    /// </summary>
    /// <param name="source"></param>
    /// <param name="cancellationToken"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    IAsyncEnumerable<SourceRecord<T>> ReadAsync<T>(SourceHandle<T> source, CancellationToken cancellationToken);

    /// <summary>
    /// Записать данные
    /// </summary>
    /// <param name="sink"></param>
    /// <param name="record"></param>
    /// <param name="cancellationToken"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task WriteAsync<T>(SinkHandle<T> sink, SinkRecord<T> record, CancellationToken cancellationToken);
}