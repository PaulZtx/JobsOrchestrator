using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Jobs.Connectors;

namespace Jobs.JobsEntities;

internal sealed class JobDefinition(IReadOnlyCollection<ISourceDefinition> sources)
{
    public async Task RunAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        using var runtimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var sourceTasks = sources
            .Select(source => source.RunAsync(serviceProvider, runtimeCancellation.Token))
            .ToList();

        try
        {
            while (sourceTasks.Count > 0)
            {
                var completed = await Task.WhenAny(sourceTasks);
                sourceTasks.Remove(completed);

                if (completed.IsFaulted)
                {
                    await runtimeCancellation.CancelAsync();
                    await ObserveFailuresAsync(sourceTasks);
                    ExceptionDispatchInfo.Capture(completed.Exception!.GetBaseException()).Throw();
                }

                await completed;
            }
        }
        finally
        {
            await runtimeCancellation.CancelAsync();
        }
    }

    private static async Task ObserveFailuresAsync(IEnumerable<Task> tasks)
    {
        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
            // The first source failure is reported to the orchestrator.
        }
    }
}

internal interface ISourceRegistration
{
    ISourceDefinition Build();
}

internal sealed class SourceRegistration<T>(
    string name,
    Func<IServiceProvider, IConnectorSource<T>> factory) : ISourceRegistration
{
    private readonly List<Func<SourceRecord<T>, CancellationToken, Task>> _handlers = [];

    public void AddHandler(Func<SourceRecord<T>, CancellationToken, Task> handler)
    {
        _handlers.Add(handler);
    }

    public ISourceDefinition Build()
    {
        if (_handlers.Count == 0)
            throw new InvalidOperationException($"Source '{name}' has no processors.");

        return new SourceDefinition<T>(name, factory, _handlers.ToArray());
    }
}

internal interface ISinkRegistration
{
}

internal sealed class SinkRegistration<T>(
    string name,
    Func<IServiceProvider, IConnectorSink<T>> factory) : ISinkRegistration
{
    public string Name { get; } = name;
    public Func<IServiceProvider, IConnectorSink<T>> Factory { get; } = factory;
}

internal interface ISourceDefinition
{
    Task RunAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken);
}

internal sealed class SourceDefinition<T>(
    string name,
    Func<IServiceProvider, IConnectorSource<T>> factory,
    IReadOnlyCollection<Func<SourceRecord<T>, CancellationToken, Task>> handlers) : ISourceDefinition
{
    private const int ChannelCapacity = 128;

    public async Task RunAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var connector = factory(serviceProvider);
        if (!connector.TryConnect())
            throw new InvalidOperationException($"Could not connect source '{name}'.");

        using var sourceCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var channels = handlers
            .Select(_ => Channel.CreateBounded<SourceRecord<T>>(new BoundedChannelOptions(ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true
            }))
            .ToArray();

        var consumers = handlers
            .Zip(channels)
            .Select(pair => ConsumeAsync(pair.Second.Reader, pair.First, sourceCancellation.Token))
            .ToArray();

        var producer = ProduceAsync(connector, channels, sourceCancellation.Token);
        var workers = consumers.Append(producer).ToArray();

        try
        {
            var firstCompleted = await Task.WhenAny(workers);
            if (firstCompleted.IsFaulted || firstCompleted.IsCanceled)
                await sourceCancellation.CancelAsync();

            await Task.WhenAll(workers);
        }
        finally
        {
            await sourceCancellation.CancelAsync();
        }
    }

    private static async Task ProduceAsync(
        IConnectorSource<T> connector,
        IReadOnlyCollection<Channel<SourceRecord<T>>> channels,
        CancellationToken cancellationToken)
    {
        Exception? failure = null;

        try
        {
            await foreach (var record in connector.ReadNextAsync(new SourcePosition(0), cancellationToken))
            {
                foreach (var channel in channels)
                    await channel.Writer.WriteAsync(record, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            failure = exception;
            throw;
        }
        finally
        {
            foreach (var channel in channels)
                channel.Writer.TryComplete(failure);
        }
    }

    private static async Task ConsumeAsync(
        ChannelReader<SourceRecord<T>> reader,
        Func<SourceRecord<T>, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        await foreach (var record in reader.ReadAllAsync(cancellationToken))
            await handler(record, cancellationToken);
    }
}
