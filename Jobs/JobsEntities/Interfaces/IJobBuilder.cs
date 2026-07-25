using Jobs.Connectors;

namespace Jobs.JobsEntities.Interfaces;

/// <summary>
/// Строитель для Job
/// </summary>
public interface IJobBuilder
{
    /// <summary>
    /// Добавить источник данных
    /// </summary>
    /// <param name="name">Наименование</param>
    /// <param name="factory"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    SourceHandle<T> AddSource<T>(string name, Func<IServiceProvider, IConnectorSource<T>> factory);

    /// <summary>
    /// Добавить приниматель данных
    /// </summary>
    /// <param name="name">Наименование</param>
    /// <param name="factory"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    SinkHandle<T> AddSink<T>(string name, Func<IServiceProvider, IConnectorSink<T>> factory);
}