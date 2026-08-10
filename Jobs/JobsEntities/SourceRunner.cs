using System.Threading.Channels;
using Jobs.Connectors;
using Jobs.Connectors.Interfaces;
using Jobs.JobsEntities.Interfaces.Sources;

namespace Jobs.JobsEntities;

/// <inheritdoc />
internal sealed class SourceRunner<T>(
    string sourceName,
    IReadOnlyCollection<Func<SourceRecord<T>, CancellationToken, Task>> handlers,
    IConnectorSource<T> connector,
    SourcePosition currentPosition) : ISourceRunner
{
    private const int ChannelCapacity = 128;

    private readonly SemaphoreSlim _pauseSemaphore = new(1, 1);
    private readonly Lock _drainLock = new();

    private bool _isPaused;
    private int _pendingRecords;
    private TaskCompletionSource<bool> _drained = CreateCompletedDrainSource();
    private Task[] _workers = [];
    private CancellationTokenSource? _sourceCancellation;

    public string Name => sourceName;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!connector.TryConnect())
            throw new InvalidOperationException($"Could not connect source '{sourceName}'.");

        using var sourceCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _sourceCancellation = sourceCancellation;

        var channel = Channel.CreateBounded<SourceRecord<T>>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });

        var consumer = ConsumeAsync(channel.Reader, sourceCancellation.Token);
        var producer = ProduceAsync(channel.Writer, sourceCancellation.Token);
        _workers = [consumer, producer];

        try
        {
            var firstCompleted = await Task.WhenAny(_workers);
            if (firstCompleted.IsFaulted || firstCompleted.IsCanceled)
                await sourceCancellation.CancelAsync();

            await Task.WhenAll(_workers);
        }
        finally
        {
            await sourceCancellation.CancelAsync();
        }
    }

    /// <summary>
    /// Чтение из коннектора и передача данных в канал
    /// </summary>
    /// <param name="writer">Писатель</param>
    /// <param name="cancellationToken">Токен отмены</param>
    private async Task ProduceAsync(ChannelWriter<SourceRecord<T>> writer, CancellationToken cancellationToken)
    {
        Exception? failure = null;

        try
        {
            await foreach (var record in connector.ReadNextAsync(currentPosition, cancellationToken))
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

    private async Task ConsumeAsync(
        ChannelReader<SourceRecord<T>> reader,
        CancellationToken cancellationToken)
    {
        await foreach (var record in reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                var handlerTasks = handlers
                    .Select(handler => handler(record, cancellationToken))
                    .ToArray();

                await Task.WhenAll(handlerTasks);
                currentPosition = record.Position;
            }
            finally
            {
                CompletePendingRecord();
            }
        }
    }

    public async Task PauseAsync()
    {
        if (!_isPaused)
        {
            await _pauseSemaphore.WaitAsync();
            _isPaused = true;
            await WaitUntilDrainedAsync();
        }
    }

    public Task ResumeAsync()
    {
        if (_isPaused)
        {
            _isPaused = false;
            _pauseSemaphore.Release();
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_sourceCancellation is null)
            return;

        if (!_sourceCancellation.IsCancellationRequested)
            await _sourceCancellation.CancelAsync();

        try
        {
            await Task.WhenAll(_workers);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is the expected stop path.
        }
    }

    public Task<SourcePosition> CaptureStateAsync()
    {
        return Task.FromResult(currentPosition);
    }

    private void RegisterPendingRecord()
    {
        lock (_drainLock)
        {
            if (_pendingRecords == 0)
            {
                _drained = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            _pendingRecords++;
        }
    }

    private void CompletePendingRecord()
    {
        lock (_drainLock)
        {
            _pendingRecords--;

            if (_pendingRecords == 0)
                _drained.TrySetResult(true);
        }
    }

    private Task WaitUntilDrainedAsync()
    {
        lock (_drainLock)
            return _drained.Task;
    }

    private static TaskCompletionSource<bool> CreateCompletedDrainSource()
    {
        var source = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult(true);
        return source;
    }
}
