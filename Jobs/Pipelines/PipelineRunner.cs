using System.Threading.Channels;
using Jobs.Connectors;
using Jobs.Connectors.Interfaces;
using Jobs.Diagnostics;
using Jobs.Pipelines.Interfaces;

namespace Jobs.Pipelines;

/// <summary>
/// Выполняет связанный конвейер чтения, обработки и записи
/// </summary>
/// <typeparam name="TInput">Тип элементов источника</typeparam>
/// <typeparam name="TOutput">Тип результатов обработчика</typeparam>
/// <param name="sourceName">Имя источника</param>
/// <param name="processName">Имя обработчика</param>
/// <param name="sinkName">Имя принимающего узла</param>
/// <param name="processor">Функция обработки элемента</param>
/// <param name="source">Исходящий коннектор</param>
/// <param name="sink">Принимающий коннектор</param>
/// <param name="currentPosition">Начальная позиция источника</param>
internal sealed class PipelineRunner<TInput, TOutput>(
    string sourceName,
    string processName,
    string sinkName,
    Func<TInput, ProcessContext, CancellationToken, ValueTask<TOutput>> processor,
    IConnectorSource<TInput> source,
    IConnectorSink<TOutput> sink,
    SourcePosition currentPosition) : IPipelineRunner
{
    private const int ChannelCapacity = 128;

    private readonly SemaphoreSlim _pauseSemaphore = new(1, 1);
    private readonly Lock _drainLock = new();
    private readonly Lock _diagnosticsLock = new();
    private readonly ProcessContext _processContext = new(sourceName, processName, sinkName);

    private bool _isPaused;
    private int _pendingRecords;
    private TaskCompletionSource<bool> _drained = CreateCompletedDrainSource();
    private Task[] _workers = [];
    private CancellationTokenSource? _pipelineCancellation;
    private long _processedCount;
    private DateTimeOffset? _lastProcessedAt;
    private string? _lastProcessedMessage;

    /// <inheritdoc />
    public string SourceName => sourceName;

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var pipelineCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pipelineCancellation = pipelineCancellation;

        try
        {
            if (!sink.TryConnect())
                throw new InvalidOperationException($"Could not connect sink '{sinkName}'.");

            if (!source.TryConnect())
                throw new InvalidOperationException($"Could not connect source '{sourceName}'.");

            var channel = Channel.CreateBounded<SourceRecord<TInput>>(new BoundedChannelOptions(ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true
            });

            var consumer = ConsumeAsync(channel.Reader, pipelineCancellation.Token);
            var producer = ProduceAsync(channel.Writer, pipelineCancellation.Token);
            _workers = [consumer, producer];

            var firstCompleted = await Task.WhenAny(_workers);
            if (firstCompleted.IsFaulted || firstCompleted.IsCanceled)
                await pipelineCancellation.CancelAsync();

            await Task.WhenAll(_workers);
        }
        finally
        {
            await pipelineCancellation.CancelAsync();
            await DisposeSinkAsync();
        }
    }

    /// <summary>
    /// Освобождает ресурсы принимающего коннектора после остановки конвейера
    /// </summary>
    private async ValueTask DisposeSinkAsync()
    {
        if (sink is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
            return;
        }

        if (sink is IDisposable disposable)
            disposable.Dispose();
    }

    /// <summary>
    /// Читает элементы источника и помещает их в канал
    /// </summary>
    /// <param name="writer">Канал для записи элементов</param>
    /// <param name="cancellationToken">Токен отмены</param>
    private async Task ProduceAsync(
        ChannelWriter<SourceRecord<TInput>> writer,
        CancellationToken cancellationToken)
    {
        Exception? failure = null;

        try
        {
            await foreach (var record in source.ReadNextAsync(currentPosition, cancellationToken))
            {
                await _pauseSemaphore.WaitAsync(cancellationToken);
                var pendingRecordRegistered = false;

                try
                {
                    RegisterPendingRecord();
                    pendingRecordRegistered = true;
                    await writer.WriteAsync(record, cancellationToken);
                }
                catch
                {
                    if (pendingRecordRegistered)
                        CompletePendingRecord();

                    throw;
                }
                finally
                {
                    _pauseSemaphore.Release();
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            failure = exception;
            throw;
        }
        finally
        {
            writer.TryComplete(failure);
        }
    }

    /// <summary>
    /// Обрабатывает элементы канала и передает результаты принимающему узлу
    /// </summary>
    /// <param name="reader">Канал для чтения элементов</param>
    /// <param name="cancellationToken">Токен отмены</param>
    private async Task ConsumeAsync(
        ChannelReader<SourceRecord<TInput>> reader,
        CancellationToken cancellationToken)
    {
        await foreach (var record in reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                var output = await processor(record.Value, _processContext, cancellationToken);
                var isWritten = await sink.WriteAsync(new SinkRecord<TOutput>(output), cancellationToken);

                if (!isWritten)
                {
                    throw new InvalidOperationException(
                        $"Sink '{sinkName}' rejected a record from process '{processName}'.");
                }

                currentPosition = record.Position;
                RecordProcessed(record.Value);
            }
            finally
            {
                CompletePendingRecord();
            }
        }
    }

    /// <inheritdoc />
    public async Task PauseAsync(CancellationToken cancellationToken)
    {
        if (!_isPaused)
        {
            await _pauseSemaphore.WaitAsync(cancellationToken);
            _isPaused = true;

            try
            {
                await WaitUntilDrainedAsync().WaitAsync(cancellationToken);
            }
            catch
            {
                _isPaused = false;
                _pauseSemaphore.Release();
                throw;
            }
        }
    }

    /// <inheritdoc />
    public Task ResumeAsync()
    {
        if (_isPaused)
        {
            _isPaused = false;
            _pauseSemaphore.Release();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        if (_pipelineCancellation is null)
            return;

        if (!_pipelineCancellation.IsCancellationRequested)
            await _pipelineCancellation.CancelAsync();

        try
        {
            await Task.WhenAll(_workers);
        }
        catch (OperationCanceledException)
        {
            // Отмена является ожидаемым способом остановки
        }
    }

    /// <inheritdoc />
    public Task<SourcePosition> CaptureStateAsync()
    {
        return Task.FromResult(currentPosition);
    }

    /// <inheritdoc />
    public PipelineExecutionSnapshot CaptureDiagnostics()
    {
        lock (_diagnosticsLock)
        {
            return new PipelineExecutionSnapshot(
                _processedCount,
                _lastProcessedAt,
                _lastProcessedMessage);
        }
    }

    /// <summary>
    /// Обновляет счетчики после успешной записи сообщения в принимающий узел.
    /// </summary>
    private void RecordProcessed(TInput message)
    {
        var formattedMessage = DiagnosticValueFormatter.Format(message);

        lock (_diagnosticsLock)
        {
            _processedCount++;
            _lastProcessedAt = DateTimeOffset.UtcNow;
            _lastProcessedMessage = formattedMessage;
        }
    }

    /// <summary>
    /// Регистрирует элемент, ожидающий завершения обработки
    /// </summary>
    private void RegisterPendingRecord()
    {
        lock (_drainLock)
        {
            if (_pendingRecords == 0)
                _drained = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            _pendingRecords++;
        }
    }

    /// <summary>
    /// Отмечает завершение обработки ожидающего элемента
    /// </summary>
    private void CompletePendingRecord()
    {
        lock (_drainLock)
        {
            _pendingRecords--;

            if (_pendingRecords == 0)
                _drained.TrySetResult(true);
        }
    }

    /// <summary>
    /// Возвращает задачу ожидания завершения всех элементов
    /// </summary>
    /// <returns>Задача ожидания опустошения конвейера</returns>
    private Task WaitUntilDrainedAsync()
    {
        lock (_drainLock)
            return _drained.Task;
    }

    /// <summary>
    /// Создает завершенный источник ожидания
    /// </summary>
    /// <returns>Завершенный источник ожидания</returns>
    private static TaskCompletionSource<bool> CreateCompletedDrainSource()
    {
        var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult(true);
        return source;
    }
}
