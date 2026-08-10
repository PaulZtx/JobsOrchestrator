using Jobs.Connectors;
using Jobs.Connectors.Interfaces;
using Jobs.Sources.Interfaces;

namespace Jobs.Sources;

/// <summary>
/// Регистратор источников данных
/// </summary>
/// <param name="name">Наименование</param>
/// <param name="factory">Фабрика для получения источника данных</param>
/// <typeparam name="T">Тип данных</typeparam>
internal sealed class SourceRegistration<T>(
    string name,
    Func<IServiceProvider, IConnectorSource<T>> factory) : ISourceRegistration
{
    private readonly List<Func<SourceRecord<T>, CancellationToken, Task>> _handlers = [];

    public void AddHandler(Func<SourceRecord<T>, CancellationToken, Task> handler)
    {
        _handlers.Add(handler);
    }

    public ISourceDefinition Build()
    {
        if (_handlers.Count == 0)
            throw new InvalidOperationException($"Source '{name}' has no processors.");

        return new SourceDefinition<T>(name, factory, _handlers.ToArray());
    }
}