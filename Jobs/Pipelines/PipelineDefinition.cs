using Jobs.Connectors;
using Jobs.Connectors.Interfaces;
using Jobs.Pipelines.Interfaces;

namespace Jobs.Pipelines;

/// <summary>
/// Неизменяемое описание связанного конвейера
/// </summary>
/// <typeparam name="TInput">Тип элементов источника</typeparam>
/// <typeparam name="TOutput">Тип результатов обработчика</typeparam>
/// <param name="sourceName">Имя источника</param>
/// <param name="sourceFactory">Фабрика исходящего коннектора</param>
/// <param name="processName">Имя обработчика</param>
/// <param name="processor">Функция обработки элемента</param>
/// <param name="sinkName">Имя принимающего узла</param>
/// <param name="sinkFactory">Фабрика принимающего коннектора</param>
internal sealed class PipelineDefinition<TInput, TOutput>(
    string sourceName,
    Func<IServiceProvider, IConnectorSource<TInput>> sourceFactory,
    string processName,
    Func<TInput, ProcessContext, CancellationToken, ValueTask<TOutput>> processor,
    string sinkName,
    Func<IServiceProvider, IConnectorSink<TOutput>> sinkFactory) : IPipelineDefinition
{
    /// <inheritdoc />
    public string SourceName => sourceName;

    /// <inheritdoc />
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
