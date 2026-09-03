using Jobs.Connectors.Interfaces;
using Jobs.Pipelines;
using Jobs.States.Interfaces;

namespace Jobs.JobsEntities.Interfaces;

/// <summary>
/// Строитель задания.
/// </summary>
public interface IJobBuilder
{
    /// <summary>
    /// Начинает описание конвейера с источника данных.
    /// </summary>
    ISourceStage<T> Source<T>(
        string name,
        Func<IServiceProvider, IConnectorSource<T>> factory);

    /// <summary>
    /// Регистрирует внутреннее состояние для заданного типа элементов.
    /// </summary>
    IState<T> RegisterState<T>(string name);

    /// <summary>
    /// Включает периодическое создание контрольных точек.
    /// </summary>
    IJobBuilder EnableCheckpoints(TimeSpan delay);
}
