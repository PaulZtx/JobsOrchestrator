using Jobs.Connectors;
using Jobs.JobsEntities.Interfaces;

namespace Jobs.JobsEntities;

public class BasicJobBuilder : IJobBuilder
{
    private readonly Dictionary<string, ISourceRegistration> _sources = [];
    private readonly Dictionary<string, ISinkRegistration> _sinks = [];
    private bool _isBuilt;
    
    public SourceHandle<T> AddSource<T>(string name, Func<IServiceProvider, IConnectorSource<T>> factory)
    {
        EnsureNotBuilt();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);

        if (!_sources.TryAdd(name, new SourceRegistration<T>(name, factory)))
            throw new InvalidOperationException($"Source '{name}' is already registered.");

        return new SourceHandle<T>(name);
    }

    public void Process<T>(
        SourceHandle<T> source,
        Func<SourceRecord<T>, CancellationToken, Task> handler)
    {
        EnsureNotBuilt();
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handler);

        if (!_sources.TryGetValue(source.Name, out var registration))
            throw new InvalidOperationException($"Source '{source.Name}' is not registered.");

        if (registration is not SourceRegistration<T> typedRegistration)
            throw new InvalidOperationException($"Source '{source.Name}' has a different record type.");

        typedRegistration.AddHandler(handler);
    }

    public SinkHandle<T> AddSink<T>(string name, Func<IServiceProvider, IConnectorSink<T>> factory)
    {
        EnsureNotBuilt();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);

        if (!_sinks.TryAdd(name, new SinkRegistration<T>(name, factory)))
            throw new InvalidOperationException($"Sink '{name}' is already registered.");

        return new SinkHandle<T>(name);
    }

    internal JobDefinition Build()
    {
        EnsureNotBuilt();
        _isBuilt = true;

        var sources = _sources.Values.Select(source => source.Build()).ToArray();
        return new JobDefinition(sources);
    }

    private void EnsureNotBuilt()
    {
        if (_isBuilt)
            throw new InvalidOperationException("Job builder has already been built.");
    }
}
