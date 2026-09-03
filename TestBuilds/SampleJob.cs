using Jobs.Connectors;
using Jobs.Connectors.Interfaces;
using Jobs.JobsEntities.Interfaces;

namespace TestBuilds;

public class SampleJob : IJob
{
    private record Test(string Name);

    private sealed class ConsoleSink<T> : IConnectorSink<T>
    {
        public bool TryConnect() => true;

        public Task<bool> WriteAsync(SinkRecord<T> value, CancellationToken token)
        {
            Console.WriteLine(value.Value);
            return Task.FromResult(true);
        }
    }

    public void Configure(IJobBuilder builder)
    {
        var state = builder.RegisterState<Test>("test-state");

        builder.EnableCheckpoints(TimeSpan.FromSeconds(1))
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
