using Jobs.Connectors.Interfaces;

namespace Jobs.Pipelines;

/// <summary>
/// Стадия описания конвейера с настроенным источником
/// </summary>
/// <typeparam name="TInput">Тип элементов источника</typeparam>
public interface ISourceStage<TInput>
{
    /// <summary>
    /// Добавляет преобразующий обработчик
    /// </summary>
    /// <typeparam name="TOutput">Тип результата обработчика</typeparam>
    /// <param name="name">Уникальное имя обработчика</param>
    /// <param name="processor">Функция обработки элемента</param>
    /// <returns>Стадия настройки принимающего узла</returns>
    IProcessedStage<TOutput> Process<TOutput>(
        string name,
        Func<TInput, ProcessContext, CancellationToken, ValueTask<TOutput>> processor);

    /// <summary>
    /// Добавляет обработчик без выходного значения
    /// </summary>
    /// <param name="name">Уникальное имя обработчика</param>
    /// <param name="processor">Функция обработки элемента</param>
    /// <returns>Стадия настройки принимающего узла</returns>
    IProcessedStage<NoOutput> Process(
        string name,
        Func<TInput, ProcessContext, CancellationToken, ValueTask> processor);
}

/// <summary>
/// Реализует стадию настройки обработчика
/// </summary>
/// <typeparam name="TInput">Тип элементов источника</typeparam>
/// <param name="builder">Построитель задания</param>
/// <param name="sourceName">Имя источника</param>
/// <param name="sourceFactory">Фабрика исходящего коннектора</param>
internal sealed class SourceStage<TInput>(
    JobsEntities.BasicJobBuilder builder,
    string sourceName,
    Func<IServiceProvider, IConnectorSource<TInput>> sourceFactory) : ISourceStage<TInput>
{
    private bool _isProcessed;

    /// <inheritdoc />
    public IProcessedStage<TOutput> Process<TOutput>(
        string name,
        Func<TInput, ProcessContext, CancellationToken, ValueTask<TOutput>> processor)
    {
        EnsureNotProcessed();
        var processedStage = builder.Process(sourceName, sourceFactory, name, processor);
        _isProcessed = true;
        return processedStage;
    }

    /// <inheritdoc />
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

    /// <summary>
    /// Проверяет, что обработчик источника еще не задан
    /// </summary>
    private void EnsureNotProcessed()
    {
        if (_isProcessed)
            throw new InvalidOperationException($"Source '{sourceName}' already has a processor.");
    }
}
