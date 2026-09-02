using Jobs.Connectors;
using Jobs.JobsEntities.Interfaces;

namespace TestBuilds;

public class SampleJob : IJob
{
    private record Test(string Name);

    public void Configure(IJobBuilder builder)
    {
        var names = builder.EnableCheckpoints(TimeSpan.FromSeconds(1))
            .AddSource(
            "File",
            _ => new JsonFileConnectorSource<Test>(Path.Combine(AppContext.BaseDirectory, "Test.json")));

        var state = builder.RegisterState<Test>("test-state");

        builder.Process(names, (record, _) =>
        {
            state.Push(record.Value);
            Console.WriteLine(record.Value);
            return Task.CompletedTask;
        });
    }
}
