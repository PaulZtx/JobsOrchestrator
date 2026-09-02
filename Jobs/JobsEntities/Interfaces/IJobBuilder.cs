using Jobs.Connectors;
using Jobs.Connectors.Interfaces;
using Jobs.Sinks;
using Jobs.Sources;
using Jobs.States.Interfaces;

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
    /// Добавить обработчик записей из источника
    /// Циклом чтения и вызовом обработчика владеет runtime
    /// </summary>
    void Process<T>(SourceHandle<T> source, Func<SourceRecord<T>, CancellationToken, Task> handler);

    /// <summary>
    /// Добавить приниматель данных
    /// </summary>
    /// <param name="name">Наименование</param>
    /// <param name="factory"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    SinkHandle<T> AddSink<T>(string name, Func<IServiceProvider, IConnectorSink<T>> factory);

    /// <summary>
    /// Регистрирует внутреннее состояние для заданного типа элементов
    /// </summary>
    /// <param name="name">Наименование состояния</param>
    /// <typeparam name="T">Тип элементов состояния</typeparam>
    /// <returns>Зарегистрированное состояние</returns>
    IState<T> RegisterState<T>(string name);

    /// <summary>
    /// Включение чекпоинтов через интервал времени
    /// </summary>
    /// <param name="delay">Интервал времени</param>
    /// <returns></returns>
    IJobBuilder EnableCheckpoints(TimeSpan delay);
}
