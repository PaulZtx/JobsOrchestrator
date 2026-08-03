using Jobs.JobsEntities.Interfaces;

namespace Jobs.JobsEntities;

internal sealed class BasicJobRuntime(
    JobDefinition definition,
    IServiceProvider serviceProvider) : IJobRuntime
{
    public Task RunAsync(CancellationToken cancellationToken)
    {
        return definition.RunAsync(serviceProvider, cancellationToken);
    }
}
