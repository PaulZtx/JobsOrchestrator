using Jobs.Connectors.Interfaces;
using Jobs.Sinks;

namespace Jobs.Pipelines;

/// <summary>
/// Стадия описания конвейера с настроенным обработчиком
/// </summary>
/// <typeparam name="TOutput">Тип результата обработчика</typeparam>
public interface IProcessedStage<TOutput>
{
    /// <summary>
    /// Подключает выход обработчика к принимающему коннектору
    /// </summary>
    /// <param name="name">Уникальное имя принимающего узла</param>
    /// <param name="factory">Фабрика принимающего коннектора</param>
    void ConnectToSink(
        string name,
        Func<IServiceProvider, IConnectorSink<TOutput>> factory);

    /// <summary>
    /// Завершает конвейер принимающим узлом без сохранения результата
    /// </summary>
    void Discard();
}

/// <summary>
/// Реализует стадию настройки принимающего узла
/// </summary>
/// <typeparam name="TInput">Тип элементов источника</typeparam>
/// <typeparam name="TOutput">Тип результатов обработчика</typeparam>
/// <param name="builder">Построитель задания</param>
/// <param name="sourceName">Имя источника</param>
/// <param name="sourceFactory">Фабрика исходящего коннектора</param>
/// <param name="processName">Имя обработчика</param>
/// <param name="processor">Функция обработки элемента</param>
internal sealed class ProcessedStage<TInput, TOutput>(
    JobsEntities.BasicJobBuilder builder,
    string sourceName,
    Func<IServiceProvider, IConnectorSource<TInput>> sourceFactory,
    string processName,
    Func<TInput, ProcessContext, CancellationToken, ValueTask<TOutput>> processor) : IProcessedStage<TOutput>
{
    private bool _isCompleted;

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <summary>
    /// Проверяет, что настройка конвейера еще не завершена
    /// </summary>
    private void EnsureNotCompleted()
    {
        if (_isCompleted)
            throw new InvalidOperationException($"Pipeline for source '{sourceName}' is already completed.");
    }
}
