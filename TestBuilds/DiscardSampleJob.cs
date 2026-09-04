using Jobs.Connectors;
using Jobs.JobsEntities.Interfaces;

namespace TestBuilds;

/// <summary>
/// Пример конвейера без внешнего принимающего узла
/// </summary>
internal sealed class DiscardSampleJob : IJob
{
    /// <summary>
    /// Тестовое значение
    /// </summary>
    /// <param name="Name">Имя значения</param>
    private sealed record Test(string Name);

    /// <inheritdoc />
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
