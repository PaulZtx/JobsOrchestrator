using Jobs.Connectors.Interfaces;
using Jobs.Sinks.Interfaces;
using Jobs.Sources.Interfaces;
using Jobs.States;

namespace Jobs.JobsEntities;

/// <summary>
/// Определение конкретной Job
/// </summary>
/// <param name="sources">Коллекция источников</param>
/// <param name="checkpointOptions">Параметры контрольных точек</param>
/// <param name="stateRegistry">Реестр внутренних состояний</param>
internal sealed class JobDefinition(
    IReadOnlyCollection<ISourceDefinition> sources,
    CheckpointOptions? checkpointOptions,
    StateRegistry stateRegistry)
{
    /// <summary>
    /// Параметры контрольных точек
    /// </summary>
    internal CheckpointOptions? CheckpointOptions { get; } = checkpointOptions;

    /// <summary>
    /// Коллекция источников
    /// </summary>
    internal IReadOnlyCollection<ISourceDefinition> Sources { get; } = sources;

    /// <summary>
    /// Реестр внутренних состояний
    /// </summary>
    internal StateRegistry StateRegistry { get; } = stateRegistry;
}

internal sealed class SinkRegistration<T>(
    string name,
    Func<IServiceProvider, IConnectorSink<T>> factory) : ISinkRegistration
{
    public string Name { get; } = name;
    public Func<IServiceProvider, IConnectorSink<T>> Factory { get; } = factory;
}
