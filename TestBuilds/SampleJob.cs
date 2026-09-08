using Jobs.Connectors;
using Jobs.Connectors.Interfaces;
using Jobs.JobsEntities;
using Jobs.JobsEntities.Interfaces;

namespace TestBuilds;

/// <summary>
/// Пример задания с состоянием и выводом в консоль
/// </summary>
public class SampleJob : IJob
{
    /// <summary>
    /// Тестовое значение
    /// </summary>
    /// <param name="Name">Имя значения</param>
    private record Test(string Name);

    /// <summary>
    /// Записывает полученные значения в консоль
    /// </summary>
    /// <typeparam name="T">Тип записываемых значений</typeparam>
    private sealed class ConsoleSink<T> : IConnectorSink<T>
    {
        /// <inheritdoc />
        public bool TryConnect() => true;

        /// <inheritdoc />
        public Task<bool> WriteAsync(SinkRecord<T> value, CancellationToken token)
        {
            Console.WriteLine(value.Value);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public void Configure(IJobBuilder builder)
    {
        var state = builder.RegisterState<Test>("test-state");

        builder.ConfigureCheckpoints(new CheckpointOptions
        {
            Enabled = true,
            DelayMillisecond = 1_000
        })
            .Source(
                "File",
                _ => new JsonFileConnectorSource<Test>(Path.Combine(AppContext.BaseDirectory, "Test.json")))
            .Process<Test>(
                "Remember",
                (value, _, _) =>
                {
                    state.Push(value);
                    return ValueTask.FromResult(value);
                })
            .ConnectToSink("Console", _ => new ConsoleSink<Test>());
    }
}
