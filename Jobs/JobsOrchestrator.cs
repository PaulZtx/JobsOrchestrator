using System.Collections.Concurrent;
using Jobs.Enums;
using Jobs.JobsEntities;
using Jobs.JobsEntities.Interfaces;

namespace Jobs;

/// <summary>
/// Управляет запуском и остановкой заданий
/// </summary>
public class JobsOrchestrator
{
    private readonly ConcurrentDictionary<Guid, JobEntry> _jobs;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Создает оркестратор заданий
    /// </summary>
    /// <param name="serviceProvider">Провайдер сервисов</param>
    public JobsOrchestrator(IServiceProvider? serviceProvider = null)
    {
        _jobs = [];
        _cancellationTokenSource = new CancellationTokenSource();
        _serviceProvider = serviceProvider ?? EmptyServiceProvider.Instance;
    }

    /// <summary>
    /// Добавляет и запускает задание
    /// </summary>
    /// <param name="job">Задание</param>
    /// <param name="jobStartOptions">Параметры запуска</param>
    /// <returns>Результат добавления задания</returns>
    public OrchestratorStatus TryAddJob(IJob job, JobStartOptions? jobStartOptions = null)
    {
        var status = new OrchestratorStatus();
        try
        {
            var jobId = Guid.NewGuid();
            var builder = new BasicJobBuilder();

            job.Configure(builder);
            var definition = builder.Build();
            var resolvedStartOptions = ResolveStartOptions(
                jobId,
                definition.CheckpointOptions,
                jobStartOptions);

            var cancellationTokenSource = new CancellationTokenSource();
            var runtime = new BasicJobRuntime(definition, _serviceProvider, resolvedStartOptions);

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
            status.CheckpointPath = resolvedStartOptions?.CheckpointPath;
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

    /// <summary>
    /// Останавливает и удаляет задание
    /// </summary>
    /// <param name="jobId">Идентификатор задания</param>
    /// <returns>Признак успешного удаления</returns>
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

    /// <summary>
    /// Проверяет и дополняет параметры запуска задания
    /// </summary>
    /// <param name="jobId">Идентификатор задания</param>
    /// <param name="checkpointOptions">Параметры контрольных точек</param>
    /// <param name="requestedOptions">Запрошенные параметры запуска</param>
    /// <returns>Проверенные параметры запуска</returns>
    private static JobStartOptions? ResolveStartOptions(
        Guid jobId,
        CheckpointOptions? checkpointOptions,
        JobStartOptions? requestedOptions)
    {
        if (checkpointOptions is not { Enabled: true })
        {
            if (requestedOptions is not null)
            {
                throw new InvalidOperationException(
                    "Checkpoint start options were provided, but checkpoints are not enabled for the job.");
            }

            return null;
        }

        requestedOptions ??= new JobStartOptions();

        if (!Enum.IsDefined(requestedOptions.RestoreMode))
            throw new ArgumentOutOfRangeException(nameof(requestedOptions.RestoreMode));

        var hasPath = !string.IsNullOrWhiteSpace(requestedOptions.CheckpointPath);
        if (!hasPath && requestedOptions.RestoreMode == CheckpointRestoreMode.ResumeOnly)
        {
            throw new ArgumentException(
                "Checkpoint path must be provided for ResumeOnly mode.",
                nameof(requestedOptions));
        }

        var checkpointPath = hasPath
            ? Path.GetFullPath(requestedOptions.CheckpointPath!, AppContext.BaseDirectory)
            : Path.Combine(
                AppContext.BaseDirectory,
                "checkpoints",
                $"{jobId:N}.json");

        if (Directory.Exists(checkpointPath) || string.IsNullOrWhiteSpace(Path.GetFileName(checkpointPath)))
            throw new ArgumentException($"Checkpoint path '{checkpointPath}' must point to a file.", nameof(requestedOptions));

        if (requestedOptions.RestoreMode == CheckpointRestoreMode.ResumeOnly && !File.Exists(checkpointPath))
            throw new FileNotFoundException("Checkpoint file was not found.", checkpointPath);

        if (requestedOptions.RestoreMode == CheckpointRestoreMode.CreateNew && File.Exists(checkpointPath))
            throw new IOException($"Checkpoint file '{checkpointPath}' already exists.");

        return new JobStartOptions
        {
            CheckpointPath = checkpointPath,
            RestoreMode = requestedOptions.RestoreMode
        };
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static EmptyServiceProvider Instance { get; } = new();

        /// <inheritdoc />
        public object? GetService(Type serviceType) => null;
    }
}
