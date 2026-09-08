using System.Text;
using Confluent.Kafka;
using Jobs.Connectors;
using Jobs.JobsEntities;
using Jobs.JobsEntities.Interfaces;
using Jobs.Sinks;

namespace TestBuilds;

/// <summary>
/// Пересылает длину строкового сообщения из одной темы Kafka в другую
/// </summary>
public sealed class KafkaRelayJob : IJob
{
    private const string DefaultBootstrapServers =
        "localhost:9092,localhost:9093,localhost:9094";

    /// <inheritdoc />
    public void Configure(IJobBuilder builder)
    {
        var bootstrapServers = GetEnvironmentValue("KAFKA_BOOTSTRAP_SERVERS", DefaultBootstrapServers);
        var inputTopic = GetEnvironmentValue("KAFKA_INPUT_TOPIC", "jobs-input");
        var outputTopic = GetEnvironmentValue("KAFKA_OUTPUT_TOPIC", "jobs-output");
        var checkpointPath = GetEnvironmentValue(
            "KAFKA_CHECKPOINT_PATH",
            Path.Combine(AppContext.BaseDirectory, "checkpoints", "kafka-relay.json"));

        var stringSerializer = new Utf8StringSerializer();
        var lengthSerializer = new CustomJsonSerializer<int>();

        builder.ConfigureCheckpoints(new CheckpointOptions
            {
                Enabled = true,
                DelayMillisecond = 1_000,
                PathToCheckpoint = checkpointPath
            })
            .Source(
                "KafkaInput",
                _ => new KafkaConnectorSource<string>(
                    stringSerializer,
                    new ConsumerConfig
                    {
                        BootstrapServers = bootstrapServers,
                        GroupId = "jobs-orchestrator-kafka-relay",
                        AutoOffsetReset = AutoOffsetReset.Earliest
                    },
                    inputTopic))
            .Process<int>(
                "StringLength",
                (value, _, _) => ValueTask.FromResult(value.Length))
            .ConnectToSink(
                "KafkaOutput",
                _ => new KafkaSink<int>(
                    lengthSerializer,
                    new ProducerConfig { BootstrapServers = bootstrapServers },
                    outputTopic));
    }

    /// <summary>
    /// Читает переменную окружения с подстановкой значения по умолчанию
    /// </summary>
    /// <param name="name">Имя переменной окружения</param>
    /// <param name="fallback">Значение при отсутствии переменной или наличии только пробельных символов</param>
    /// <returns>Непустое значение переменной окружения или переданное значение по умолчанию</returns>
    private static string GetEnvironmentValue(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private sealed class Utf8StringSerializer : Jobs.JobsEntities.Interfaces.ISerializer<string>
    {
        /// <inheritdoc />
        public string Deserialize(ReadOnlySpan<byte> data) => Encoding.UTF8.GetString(data);

        /// <inheritdoc />
        public byte[] Serialize(string data) => Encoding.UTF8.GetBytes(data);
    }
}
