using System.Collections.Concurrent;
using System.Data;
using Jobs.JobsEntities;
using Jobs.JobsEntities.Interfaces;

namespace Jobs;

public class JobsOrchestrator
{
    private readonly ConcurrentDictionary<Guid, JobEntry> _jobs;
    private readonly CancellationTokenSource _cancellationTokenSource;
    
    public JobsOrchestrator()
    {
        _jobs = [];
        _cancellationTokenSource = new CancellationTokenSource();
    }

    public OrchestratorStatus TryAddJob(IJob job)
    {
        var status = new OrchestratorStatus();
        try
        {
            var jobId = Guid.NewGuid();
            var cancellationTokenSource = new CancellationTokenSource();
            var jobEntry = new JobEntry()
            {
                JobId = jobId, 
                Job = job,
                CancellationTokenSource = cancellationTokenSource
            };
            
            var task = Task.Run(async () => await job.ExecuteAsync(_, jobEntry.CancellationTokenSource.Token), _cancellationTokenSource.Token);
            
            jobEntry.JobTask = task;
            _jobs[jobEntry.JobId] = jobEntry;
            
            // Log
            
            status.JobId = jobId;
            
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
        catch (Exception e)
        {
            // Log
            return false;
        }
    }
    
}