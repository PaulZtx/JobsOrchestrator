using Jobs.Connectors;
using Jobs.JobsEntities.Interfaces;

namespace Jobs.JobsEntities;

public class SampleJob : IJob
{
    private record Test(string Name);
    
    private SourceHandle<Test> _names = null!;
    
    public void Configure(IJobBuilder builder)
    {
        builder.AddSource("File", service => new JsonFileConnectorSource<Test>("C:\\Users\\stsma\\RiderProjects\\JobsOperator\\JobsOperator\\Test.json"));
        
    }

    public async Task ExecuteAsync(IJobContext context, CancellationToken cancellationToken)
    {
        await foreach (SourceRecord<Test> record in context.ReadAsync(_names, cancellationToken))
        {
            Console.WriteLine(record.Value);
        }
    }
}
