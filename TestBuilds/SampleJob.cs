using Jobs.Connectors;
using Jobs.JobsEntities.Interfaces;

namespace TestBuilds;

public class SampleJob : IJob
{
    private record Test(string Name);

    public void Configure(IJobBuilder builder)
    {
        var names = builder.EnableCheckpoints(new TimeSpan(0, 0, 10))
            .AddSource(
            "File",
            _ => new JsonFileConnectorSource<Test>(Path.Combine(AppContext.BaseDirectory, "Test.json")));

        builder.Process(names, (record, _) =>
        {
            Console.WriteLine(record.Value);
            return Task.CompletedTask;
        });
        
        builder.Process(names, (record, _) =>
        {
            Console.WriteLine(record.Value);
            return Task.CompletedTask;
        });
    }
}
