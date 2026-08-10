using Jobs.Connectors;
using Jobs.Connectors.Interfaces;
using Jobs.JobsEntities;
using Jobs.Sources.Interfaces;

namespace Jobs.Sources;

/// <summary>
/// 
/// </summary>
/// <param name="name"></param>
/// <param name="connectorFactory"></param>
/// <param name="handlers"></param>
/// <typeparam name="T"></typeparam>
internal sealed class SourceDefinition<T>(
    string name,
    Func<IServiceProvider, IConnectorSource<T>> connectorFactory,
    IReadOnlyCollection<Func<SourceRecord<T>, CancellationToken, Task>> handlers) : ISourceDefinition
{
    public ISourceRunner CreateRunner(IServiceProvider serviceProvider)
    {
        var connector = connectorFactory(serviceProvider);
        return new SourceRunner<T>(
            name,
            handlers,
            connector,
            new SourcePosition(0));
    }
}