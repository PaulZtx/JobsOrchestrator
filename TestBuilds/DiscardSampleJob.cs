using Jobs.Connectors;
using Jobs.JobsEntities.Interfaces;

namespace TestBuilds;

/// <summary>
/// Пример конвейера, в котором результат Process не требуется внешнему sink.
/// </summary>
internal sealed class DiscardSampleJob : IJob
{
    private sealed record Test(string Name);

    public void Configure(IJobBuilder builder)
    {
        builder.Source(
                "FileWithoutSink",
                _ => new JsonFileConnectorSource<Test>(Path.Combine(AppContext.BaseDirectory, "Test.json")))
            .Process(
                "Observe",
                (value, _, _) =>
                {
                    Console.WriteLine(value);
                    return ValueTask.CompletedTask;
                })
            .Discard();
    }
}
