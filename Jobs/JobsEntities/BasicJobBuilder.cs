using Jobs.Connectors;
using Jobs.Connectors.Interfaces;
using Jobs.JobsEntities.Interfaces;
using Jobs.Sinks;
using Jobs.Sinks.Interfaces;
using Jobs.Sources;
using Jobs.Sources.Interfaces;

namespace Jobs.JobsEntities;

/// <summary>
/// Базовая реализация билдера для построения графа выполнения Jobs
/// </summary>
public class BasicJobBuilder : IJobBuilder
{
    private readonly Dictionary<string, ISourceRegistration> _sources = [];
    private readonly Dictionary<string, ISinkRegistration> _sinks = [];
    private bool _isBuilt;
    
    private CheckpointOptions? _checkpointOptions;

    /// <inheritdoc />
    public SourceHandle<T> AddSource<T>(string name, Func<IServiceProvider, IConnectorSource<T>> factory)
    {
        EnsureNotBuilt();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);

        if (!_sources.TryAdd(name, new SourceRegistration<T>(name, factory)))
            throw new InvalidOperationException($"Source '{name}' is already registered.");

        return new SourceHandle<T>(name);
    }


    /// <inheritdoc />
    public void Process<T>(SourceHandle<T> source, Func<SourceRecord<T>, CancellationToken, Task> handler)
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

    /// <inheritdoc />
    public SinkHandle<T> AddSink<T>(string name, Func<IServiceProvider, IConnectorSink<T>> factory)
    {
        EnsureNotBuilt();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);

        if (!_sinks.TryAdd(name, new SinkRegistration<T>(name, factory)))
            throw new InvalidOperationException($"Sink '{name}' is already registered.");

        return new SinkHandle<T>(name);
    }

    public IJobBuilder EnableCheckpoints(TimeSpan delay)
    {
        EnsureNotBuilt();

        if (delay < TimeSpan.FromMilliseconds(1))
            throw new ArgumentOutOfRangeException(nameof(delay), "Checkpoint interval must be at least 1 millisecond.");

        if (delay.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(delay), "Checkpoint interval is too large.");

        _checkpointOptions = new CheckpointOptions
        {
            Enabled = true,
            DelayMillisecond = checked((int)delay.TotalMilliseconds)
        };

        return this;
    }

    /// <summary>
    /// Создание Job и всех зависимостей
    /// </summary>
    /// <returns></returns>
    internal JobDefinition Build()
    {
        EnsureNotBuilt();
        _isBuilt = true;

        var sources = _sources.Values.Select(source => source.Build()).ToArray();
        return new JobDefinition(sources, _checkpointOptions);
    }

    /// <summary>
    /// Защита от повторной сборки
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    private void EnsureNotBuilt()
    {
        if (_isBuilt)
            throw new InvalidOperationException("Job builder has already been built.");
    }
}
