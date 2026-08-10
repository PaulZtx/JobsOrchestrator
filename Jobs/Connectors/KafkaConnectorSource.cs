using System.Runtime.CompilerServices;
using Confluent.Kafka;
using Jobs.Connectors.Interfaces;

namespace Jobs.Connectors;

public class KafkaConnectorSource<T>(
    JobsEntities.Interfaces.ISerializer<T> serializer,
    ConsumerConfig config,
    string topic,
    int partition = 0) : IConnectorSource<T>
{
    private IConsumer<Ignore, byte[]>? _consumer;

    public async IAsyncEnumerable<SourceRecord<T>> ReadNextAsync(
        SourcePosition position,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var consumer = _consumer
            ?? throw new InvalidOperationException("Kafka source is not connected. Call TryConnect first.");

        try
        {
            // SourcePosition points to the next Kafka message that must be read.
            consumer.Assign(new TopicPartitionOffset(
                topic,
                new Partition(partition),
                new Offset(position.Offset)));

            while (!cancellationToken.IsCancellationRequested)
            {
                var consumeResult = consumer.Consume(cancellationToken);

                if (consumeResult.IsPartitionEOF)
                    continue;

                var value = serializer.Deserialize(consumeResult.Message.Value.AsSpan())
                    ?? throw new InvalidDataException(
                        $"Kafka message at {consumeResult.TopicPartitionOffset} contains null.");

                var nextOffset = checked(consumeResult.Offset.Value + 1);
                yield return new SourceRecord<T>(value, new SourcePosition(nextOffset));
            }
        }
        finally
        {
            try
            {
                consumer.Close();
            }
            finally
            {
                consumer.Dispose();
                _consumer = null;
            }
        }
    }

    public Task CommitAsync(SourcePosition position, CancellationToken cancellationToken)
    {
        // The framework checkpoint is the source of truth. Kafka offsets are not
        // committed independently, otherwise they can get ahead of handler state.
        return Task.CompletedTask;
    }

    public bool TryConnect()
    {
        if (_consumer is not null)
            return !_consumer.Handle.IsInvalid;

        var sourceConfig = new ConsumerConfig(config)
        {
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false
        };

        _consumer = new ConsumerBuilder<Ignore, byte[]>(sourceConfig).Build();
        return !_consumer.Handle.IsInvalid;
    }
}
