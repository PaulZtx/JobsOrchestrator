using Jobs.Connectors;
using Jobs.JobsEntities.Interfaces;

namespace Jobs.JobsEntities;

public class SampleJob : IJob
{
    private record Test(string Name);

    public void Configure(IJobBuilder builder)
    {
        var names = builder.AddSource(
            "File",
            _ => new JsonFileConnectorSource<Test>(Path.Combine(AppContext.BaseDirectory, "Test.json")));

        builder.Process(names, (record, _) =>
        {
            Console.WriteLine(record.Value);
            return Task.CompletedTask;
        });
    }
}
