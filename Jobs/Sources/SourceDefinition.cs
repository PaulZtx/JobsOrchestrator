using Jobs.Connectors;
using Jobs.Connectors.Interfaces;
using Jobs.JobsEntities;
using Jobs.Sources.Interfaces;

namespace Jobs.Sources;

/// <summary>
/// Описывает источник данных и его обработчики
/// </summary>
/// <param name="name">Наименование источника</param>
/// <param name="connectorFactory">Фабрика коннектора</param>
/// <param name="handlers">Обработчики записей</param>
/// <typeparam name="T">Тип данных источника</typeparam>
internal sealed class SourceDefinition<T>(
    string name,
    Func<IServiceProvider, IConnectorSource<T>> connectorFactory,
    IReadOnlyCollection<Func<SourceRecord<T>, CancellationToken, Task>> handlers) : ISourceDefinition
{
    public string Name => name;

    /// <inheritdoc />
    public ISourceRunner CreateRunner(IServiceProvider serviceProvider, SourcePosition sourcePosition)
    {
        var connector = connectorFactory(serviceProvider);
        return new SourceRunner<T>(
            name,
            handlers,
            connector,
            sourcePosition);
    }
}
