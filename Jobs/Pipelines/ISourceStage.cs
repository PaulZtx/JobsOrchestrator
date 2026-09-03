using Jobs.Connectors.Interfaces;

namespace Jobs.Pipelines;

/// <summary>
/// Стадия описания конвейера, на которой задан источник и ожидается обработчик.
/// </summary>
/// <typeparam name="TInput">Тип элементов источника</typeparam>
public interface ISourceStage<TInput>
{
    /// <summary>
    /// Добавляет преобразующий обработчик.
    /// </summary>
    IProcessedStage<TOutput> Process<TOutput>(
        string name,
        Func<TInput, ProcessContext, CancellationToken, ValueTask<TOutput>> processor);

    /// <summary>
    /// Добавляет обработчик без выходного значения.
    /// </summary>
    IProcessedStage<NoOutput> Process(
        string name,
        Func<TInput, ProcessContext, CancellationToken, ValueTask> processor);
}

internal sealed class SourceStage<TInput>(
    JobsEntities.BasicJobBuilder builder,
    string sourceName,
    Func<IServiceProvider, IConnectorSource<TInput>> sourceFactory) : ISourceStage<TInput>
{
    private bool _isProcessed;

    public IProcessedStage<TOutput> Process<TOutput>(
        string name,
        Func<TInput, ProcessContext, CancellationToken, ValueTask<TOutput>> processor)
    {
        EnsureNotProcessed();
        var processedStage = builder.Process(sourceName, sourceFactory, name, processor);
        _isProcessed = true;
        return processedStage;
    }

    public IProcessedStage<NoOutput> Process(
        string name,
        Func<TInput, ProcessContext, CancellationToken, ValueTask> processor)
    {
        ArgumentNullException.ThrowIfNull(processor);

        return Process<NoOutput>(name, async (value, context, cancellationToken) =>
        {
            await processor(value, context, cancellationToken);
            return NoOutput.Value;
        });
    }

    private void EnsureNotProcessed()
    {
        if (_isProcessed)
            throw new InvalidOperationException($"Source '{sourceName}' already has a processor.");
    }
}
