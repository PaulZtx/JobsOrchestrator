using Jobs.Connectors.Interfaces;
using Jobs.JobsEntities.Interfaces;
using Jobs.Pipelines;
using Jobs.Pipelines.Interfaces;
using Jobs.States;
using Jobs.States.Interfaces;

namespace Jobs.JobsEntities;

/// <summary>
/// Базовая реализация билдера для построения графа выполнения Jobs.
/// </summary>
public class BasicJobBuilder : IJobBuilder
{
    private readonly List<IPipelineDefinition> _pipelines = [];
    private readonly HashSet<string> _sourceNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _incompletePipelines = new(StringComparer.Ordinal);
    private readonly StateRegistry _stateRegistry = new();

    private bool _isBuilt;
    private CheckpointOptions? _checkpointOptions;

    /// <inheritdoc />
    public ISourceStage<T> Source<T>(
        string name,
        Func<IServiceProvider, IConnectorSource<T>> factory)
    {
        EnsureNotBuilt();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);

        if (!_sourceNames.Add(name))
            throw new InvalidOperationException($"Source '{name}' is already registered.");

        _incompletePipelines.Add(name);
        return new SourceStage<T>(this, name, factory);
    }

    /// <summary>
    /// Создает стадию обработанного потока.
    /// </summary>
    internal IProcessedStage<TOutput> Process<TInput, TOutput>(
        string sourceName,
        Func<IServiceProvider, IConnectorSource<TInput>> sourceFactory,
        string processName,
        Func<TInput, ProcessContext, CancellationToken, ValueTask<TOutput>> processor)
    {
        EnsureNotBuilt();
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);
        ArgumentNullException.ThrowIfNull(processor);

        return new ProcessedStage<TInput, TOutput>(
            this,
            sourceName,
            sourceFactory,
            processName,
            processor);
    }

    /// <summary>
    /// Завершает описание конвейера и добавляет его в определение задания.
    /// </summary>
    internal void CompletePipeline<TInput, TOutput>(
        string sourceName,
        Func<IServiceProvider, IConnectorSource<TInput>> sourceFactory,
        string processName,
        Func<TInput, ProcessContext, CancellationToken, ValueTask<TOutput>> processor,
        string sinkName,
        Func<IServiceProvider, IConnectorSink<TOutput>> sinkFactory)
    {
        EnsureNotBuilt();
        ArgumentException.ThrowIfNullOrWhiteSpace(sinkName);
        ArgumentNullException.ThrowIfNull(sinkFactory);

        if (!_incompletePipelines.Remove(sourceName))
            throw new InvalidOperationException($"Pipeline for source '{sourceName}' is already completed.");

        _pipelines.Add(new PipelineDefinition<TInput, TOutput>(
            sourceName,
            sourceFactory,
            processName,
            processor,
            sinkName,
            sinkFactory));
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public IState<T> RegisterState<T>(string name)
    {
        EnsureNotBuilt();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var state = new BagState<T>(name);
        _stateRegistry.Register(state);
        return state;
    }

    /// <summary>
    /// Создает Job и все его зависимости.
    /// </summary>
    internal JobDefinition Build()
    {
        EnsureNotBuilt();

        if (_incompletePipelines.Count > 0)
        {
            throw new InvalidOperationException(
                $"Pipelines for the following sources are incomplete: {string.Join(", ", _incompletePipelines)}.");
        }

        _isBuilt = true;
        return new JobDefinition(_pipelines.ToArray(), _checkpointOptions, _stateRegistry);
    }

    private void EnsureNotBuilt()
    {
        if (_isBuilt)
            throw new InvalidOperationException("Job builder has already been built.");
    }
}
