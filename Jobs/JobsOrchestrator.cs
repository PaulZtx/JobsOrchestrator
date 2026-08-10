using System.Collections.Concurrent;
using Jobs.JobsEntities;
using Jobs.JobsEntities.Interfaces;

namespace Jobs;

public class JobsOrchestrator
{
    private readonly ConcurrentDictionary<Guid, JobEntry> _jobs;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly IServiceProvider _serviceProvider;
    
    public JobsOrchestrator(IServiceProvider? serviceProvider = null)
    {
        _jobs = [];
        _cancellationTokenSource = new CancellationTokenSource();
        _serviceProvider = serviceProvider ?? EmptyServiceProvider.Instance;
    }

    public OrchestratorStatus TryAddJob(IJob job)
    {
        var status = new OrchestratorStatus();
        try
        {
            var jobId = Guid.NewGuid();
            var builder = new BasicJobBuilder();
            
            var cancellationTokenSource = new CancellationTokenSource();
            
            job.Configure(builder);
            var runtime = new BasicJobRuntime(builder.Build(), _serviceProvider);
            
            var task = Task.Run(
                () => runtime.RunAsync(cancellationTokenSource.Token),
                _cancellationTokenSource.Token);

            
            var jobEntry = new JobEntry()
            {
                JobId = jobId, 
                Job = job,
                CancellationTokenSource = cancellationTokenSource,
                Runtime = runtime,
                JobTask = task
            };

            _jobs[jobEntry.JobId] = jobEntry;
            
            // Log
            
            status.JobId = jobId;
            Console.WriteLine($"Job {jobId} added");
            return status;
        }
        catch (Exception e)
        {
            // Log
            status.JobId = null;
            status.ErrorMessage = e.Message;
            return status;
        }
    }

    public async Task<bool> TryRemoveJob(Guid jobId)
    {
        try
        {
            if (!_jobs.TryRemove(jobId, out var job))
            {
                throw new ArgumentException($"Job {jobId} does not exist");
            }
            await job.CancellationTokenSource.CancelAsync();
            await job.JobTask;
            
            // Log
            return true;
        }
        catch (Exception)
        {
            // Log
            return false;
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static EmptyServiceProvider Instance { get; } = new();

        public object? GetService(Type serviceType) => null;
    }
    
}
