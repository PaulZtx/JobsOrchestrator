using Confluent.Kafka;
using Jobs.Connectors;
using Jobs.Connectors.Interfaces;

namespace Jobs.Sinks;

/// <summary>
/// Записывает сообщения в раздел Kafka
/// </summary>
/// <param name="serializer">Сериализатор значений</param>
/// <param name="producerConfig">Конфигурация производителя Kafka</param>
/// <param name="topic">Имя темы</param>
/// <param name="partition">Номер раздела</param>
/// <typeparam name="T">Тип записываемых значений</typeparam>
public class KafkaSink<T>(
    JobsEntities.Interfaces.ISerializer<T> serializer,
    ProducerConfig producerConfig,
    string topic,
    int partition = 0) : IConnectorSink<T>, IDisposable
{
    private IProducer<Null, byte[]>? _producer;

    /// <inheritdoc />
    public async Task<bool> WriteAsync(SinkRecord<T> value, CancellationToken token)
    {
        var producer = _producer ??
                       throw new InvalidOperationException("Kafka sink is not connected. Call TryConnect first.");

        if (token.IsCancellationRequested)
            return false;

        var result = await producer.ProduceAsync(
            new TopicPartition(topic, partition),
            new Message<Null, byte[]> { Value = serializer.Serialize(value.Value) },
            token);

        return result.Status == PersistenceStatus.Persisted;
    }

    /// <inheritdoc />
    public bool TryConnect()
    {
        if (_producer is not null)
            return !_producer.Handle.IsInvalid;

        var prodConfig = new ProducerConfig(producerConfig)
        {
            Acks = Acks.All,
            EnableIdempotence = true
        };

        _producer = new ProducerBuilder<Null, byte[]>(prodConfig).Build();
        return !_producer.Handle.IsInvalid;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        var producer = Interlocked.Exchange(ref _producer, null);
        if (producer is null)
            return;

        try
        {
            producer.Flush(TimeSpan.FromSeconds(10));
        }
        finally
        {
            producer.Dispose();
        }
    }
}
