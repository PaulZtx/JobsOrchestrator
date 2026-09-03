using Jobs.Connectors;
using Jobs.Connectors.Interfaces;
using Jobs.Pipelines.Interfaces;

namespace Jobs.Pipelines;

/// <summary>
/// Неизменяемое описание связанного конвейера source-process-sink.
/// </summary>
internal sealed class PipelineDefinition<TInput, TOutput>(
    string sourceName,
    Func<IServiceProvider, IConnectorSource<TInput>> sourceFactory,
    string processName,
    Func<TInput, ProcessContext, CancellationToken, ValueTask<TOutput>> processor,
    string sinkName,
    Func<IServiceProvider, IConnectorSink<TOutput>> sinkFactory) : IPipelineDefinition
{
    public string SourceName => sourceName;

    public IPipelineRunner CreateRunner(
        IServiceProvider serviceProvider,
        SourcePosition sourcePosition)
    {
        var source = sourceFactory(serviceProvider)
            ?? throw new InvalidOperationException($"Factory for source '{sourceName}' returned null.");
        var sink = sinkFactory(serviceProvider)
            ?? throw new InvalidOperationException($"Factory for sink '{sinkName}' returned null.");

        return new PipelineRunner<TInput, TOutput>(
            sourceName,
            processName,
            sinkName,
            processor,
            source,
            sink,
            sourcePosition);
    }
}
