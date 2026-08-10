using Jobs.Connectors;
using Jobs.Connectors.Interfaces;
using Jobs.JobsEntities.Interfaces.Sinks;
using Jobs.JobsEntities.Interfaces.Sources;

namespace Jobs.JobsEntities;

/// <summary>
/// Определение конкретной Job
/// </summary>
/// <param name="sources">Коллекция источников</param>
internal sealed class JobDefinition(IReadOnlyCollection<ISourceDefinition> sources, CheckpointOptions? checkpointOptions)
{
    internal CheckpointOptions? CheckpointOptions { get; } = checkpointOptions;
    internal IReadOnlyCollection<ISourceDefinition> Sources { get; } = sources;
}

internal sealed class SinkRegistration<T>(
    string name,
    Func<IServiceProvider, IConnectorSink<T>> factory) : ISinkRegistration
{
    public string Name { get; } = name;
    public Func<IServiceProvider, IConnectorSink<T>> Factory { get; } = factory;
}