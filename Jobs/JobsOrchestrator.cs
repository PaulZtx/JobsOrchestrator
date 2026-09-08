using System.Collections.Concurrent;
using Jobs.Diagnostics;
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
    /// <returns>Результат добавления задания</returns>
    public OrchestratorStatus TryAddJob(IJob job)
    {
        var status = new OrchestratorStatus();
        try
        {
            var jobId = Guid.NewGuid();
            var builder = new BasicJobBuilder();

            job.Configure(builder);
            var definition = builder.Build();
            var resolvedCheckpointOptions = ResolveCheckpointOptions(
                jobId,
                definition.CheckpointOptions);

            if (resolvedCheckpointOptions is not null)
            {
                definition = new JobDefinition(
                    definition.Pipelines,
                    resolvedCheckpointOptions,
                    definition.StateRegistry);
            }

            var cancellationTokenSource = new CancellationTokenSource();
            var runtime = new BasicJobRuntime(definition, _serviceProvider);

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
            
            // Точка расширения для журналирования успешного запуска

            status.JobId = jobId;
            status.CheckpointPath = resolvedCheckpointOptions?.PathToCheckpoint;
            Console.WriteLine($"Job {jobId} added");
            return status;
        }
        catch (Exception e)
        {
            // Точка расширения для журналирования ошибки запуска
            status.JobId = null;
            status.ErrorMessage = e.Message;
            return status;
        }
    }

    /// <summary>
    /// Возвращает диагностический снимок задания.
    /// </summary>
    /// <param name="jobId">Идентификатор попытки выполнения.</param>
    /// <returns>Снимок задания или <see langword="null"/>, если задание не найдено.</returns>
    public JobExecutionSnapshot? GetJobSnapshot(Guid jobId)
    {
        return _jobs.TryGetValue(jobId, out var entry)
            ? CreateSnapshot(entry)
            : null;
    }

    /// <summary>
    /// Останавливает и удаляет задание
    /// </summary>
    /// <param name="jobId">Идентификатор задания</param>
    /// <returns>Признак успешного удаления</returns>
    public async Task<bool> TryRemoveJob(Guid jobId)
    {
        if (!_jobs.TryRemove(jobId, out var job))
            return false;

        try
        {
            await job.CancellationTokenSource.CancelAsync();
            await job.JobTask;

            // Точка расширения для журналирования успешной остановки
            return true;
        }
        catch (OperationCanceledException) when (job.CancellationTokenSource.IsCancellationRequested)
        {
            // Отмена является ожидаемым способом остановки задания
            return true;
        }
        catch (Exception)
        {
            // Точка расширения для журналирования ошибки остановки
            return false;
        }
        finally
        {
            job.CancellationTokenSource.Dispose();
        }
    }

    /// <summary>
    /// Создает диагностический снимок записи оркестратора.
    /// </summary>
    private static JobExecutionSnapshot CreateSnapshot(JobEntry entry)
    {
        var runtime = (BasicJobRuntime)entry.Runtime;
        var diagnostics = runtime.CaptureDiagnostics();
        var state = ResolveState(entry);
        var errorMessage = entry.JobTask.Exception?.GetBaseException().Message;

        return new JobExecutionSnapshot(
            entry.JobId,
            state,
            diagnostics.ProcessedCount,
            diagnostics.LastProcessedAt,
            diagnostics.LastProcessedMessage,
            diagnostics.States,
            errorMessage);
    }

    /// <summary>
    /// Определяет состояние задания по задаче выполнения и токену отмены.
    /// </summary>
    private static JobExecutionState ResolveState(JobEntry entry)
    {
        if (entry.JobTask.IsFaulted)
            return JobExecutionState.Failed;

        if (entry.JobTask.IsCanceled)
            return JobExecutionState.Cancelled;

        if (entry.JobTask.IsCompletedSuccessfully)
            return JobExecutionState.Completed;

        return entry.CancellationTokenSource.IsCancellationRequested
            ? JobExecutionState.Cancelling
            : JobExecutionState.Running;
    }

    /// <summary>
    /// Проверяет и дополняет параметры запуска задания
    /// </summary>
    /// <param name="jobId">Идентификатор задания</param>
    /// <param name="checkpointOptions">Параметры контрольных точек</param>
    /// <returns>Проверенные параметры запуска</returns>
    private static CheckpointOptions? ResolveCheckpointOptions(
        Guid jobId,
        CheckpointOptions? checkpointOptions)
    {
        if (checkpointOptions is not { Enabled: true })
        {
            return null;
        }

        if (!Enum.IsDefined(checkpointOptions.RestoreMode))
            throw new ArgumentOutOfRangeException(nameof(checkpointOptions.RestoreMode));

        if (checkpointOptions.DelayMillisecond < 1)
            throw new ArgumentOutOfRangeException(
                nameof(checkpointOptions),
                "Checkpoint interval must be at least 1 millisecond.");

        var hasPath = !string.IsNullOrWhiteSpace(checkpointOptions.PathToCheckpoint);
        if (!hasPath && checkpointOptions.RestoreMode == CheckpointRestoreMode.ResumeOnly)
        {
            throw new ArgumentException(
                "Checkpoint path must be provided for ResumeOnly mode.",
                nameof(checkpointOptions));
        }

        var checkpointPath = hasPath
            ? Path.GetFullPath(checkpointOptions.PathToCheckpoint!, AppContext.BaseDirectory)
            : Path.Combine(
                AppContext.BaseDirectory,
                "checkpoints",
                $"{jobId:N}.json");

        if (Directory.Exists(checkpointPath) || string.IsNullOrWhiteSpace(Path.GetFileName(checkpointPath)))
            throw new ArgumentException($"Checkpoint path '{checkpointPath}' must point to a file.", nameof(checkpointOptions));

        if (checkpointOptions.RestoreMode == CheckpointRestoreMode.ResumeOnly && !File.Exists(checkpointPath))
            throw new FileNotFoundException("Checkpoint file was not found.", checkpointPath);

        if (checkpointOptions.RestoreMode == CheckpointRestoreMode.CreateNew && File.Exists(checkpointPath))
            throw new IOException($"Checkpoint file '{checkpointPath}' already exists.");

        return new CheckpointOptions()
        {
            Enabled = true,
            DelayMillisecond = checkpointOptions.DelayMillisecond,
            PathToCheckpoint = checkpointPath,
            RestoreMode = checkpointOptions.RestoreMode
        };
    }

    /// <summary>
    /// Пустой провайдер для заданий без внедряемых зависимостей
    /// </summary>
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        /// <summary>
        /// Единственный экземпляр пустого провайдера
        /// </summary>
        public static EmptyServiceProvider Instance { get; } = new();

        /// <inheritdoc />
        public object? GetService(Type serviceType) => null;
    }
}
