using Jobs.Connectors.Interfaces;
using Jobs.Pipelines;
using Jobs.States.Interfaces;

namespace Jobs.JobsEntities.Interfaces;

/// <summary>
/// Строитель задания
/// </summary>
public interface IJobBuilder
{
    /// <summary>
    /// Начинает описание конвейера с источника данных
    /// </summary>
    /// <typeparam name="T">Тип элементов источника</typeparam>
    /// <param name="name">Уникальное имя источника</param>
    /// <param name="factory">Фабрика исходящего коннектора</param>
    /// <returns>Стадия настройки обработчика</returns>
    ISourceStage<T> Source<T>(string name, Func<IServiceProvider, IConnectorSource<T>> factory);

    /// <summary>
    /// Регистрирует внутреннее состояние для заданного типа элементов
    /// </summary>
    /// <typeparam name="T">Тип элементов состояния</typeparam>
    /// <param name="name">Уникальное имя состояния</param>
    /// <returns>Зарегистрированное состояние</returns>
    IState<T> RegisterState<T>(string name);

    /// <summary>
    /// Включает периодическое создание контрольных точек
    /// </summary>
    /// <param name="delay">Интервал между контрольными точками</param>
    /// <returns>Текущий построитель задания</returns>
    IJobBuilder EnableCheckpoints(TimeSpan delay);
}
