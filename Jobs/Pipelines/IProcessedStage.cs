using Jobs.Connectors.Interfaces;
using Jobs.Sinks;

namespace Jobs.Pipelines;

/// <summary>
/// Стадия описания конвейера, на которой задан обработчик и ожидается выход.
/// </summary>
/// <typeparam name="TOutput">Тип результата обработчика</typeparam>
public interface IProcessedStage<TOutput>
{
    /// <summary>
    /// Подключает выход обработчика к принимающему коннектору.
    /// </summary>
    void ConnectToSink(
        string name,
        Func<IServiceProvider, IConnectorSink<TOutput>> factory);

    /// <summary>
    /// Завершает конвейер принимающим узлом, который отбрасывает результат.
    /// </summary>
    void Discard();
}

internal sealed class ProcessedStage<TInput, TOutput>(
    JobsEntities.BasicJobBuilder builder,
    string sourceName,
    Func<IServiceProvider, IConnectorSource<TInput>> sourceFactory,
    string processName,
    Func<TInput, ProcessContext, CancellationToken, ValueTask<TOutput>> processor) : IProcessedStage<TOutput>
{
    private bool _isCompleted;

    public void ConnectToSink(
        string name,
        Func<IServiceProvider, IConnectorSink<TOutput>> factory)
    {
        EnsureNotCompleted();
        builder.CompletePipeline(
            sourceName,
            sourceFactory,
            processName,
            processor,
            name,
            factory);
        _isCompleted = true;
    }

    public void Discard()
    {
        EnsureNotCompleted();
        builder.CompletePipeline(
            sourceName,
            sourceFactory,
            processName,
            processor,
            "Discard",
            _ => DiscardSink<TOutput>.Instance);
        _isCompleted = true;
    }

    private void EnsureNotCompleted()
    {
        if (_isCompleted)
            throw new InvalidOperationException($"Pipeline for source '{sourceName}' is already completed.");
    }
}
