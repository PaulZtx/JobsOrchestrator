using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using Jobs;
using Jobs.Diagnostics;
using Jobs.JobsEntities.Interfaces;
using Microsoft.AspNetCore.Components.Forms;

namespace JobsOperator.Web.Services;

/// <summary>
/// Загружает сборки, управляет заданиями и предоставляет снимки для панели мониторинга.
/// </summary>
public sealed class JobExecutionService(
    JobsOrchestrator orchestrator,
    IWebHostEnvironment environment,
    ILogger<JobExecutionService> logger)
{
    private const int MaxFileCount = 20;
    private const long MaxPackageSize = 100 * 1024 * 1024;
    private readonly ConcurrentDictionary<Guid, ManagedJob> _jobs = [];

    /// <summary>
    /// Возвращает загруженные задания вместе с актуальными показателями выполнения.
    /// </summary>
    public IReadOnlyList<RunningJobInfo> GetJobs() => _jobs.Values
        .Select(CreateInfo)
        .OrderByDescending(job => job.StartedAt)
        .ToArray();

    /// <summary>
    /// Возвращает задания. Оставлено для совместимости с прежним интерфейсом.
    /// </summary>
    public IReadOnlyList<RunningJobInfo> GetRunningJobs() => GetJobs();

    /// <summary>
    /// Сохраняет выбранные сборки и запускает найденные задания.
    /// </summary>
    public async Task<StartJobsResult> StartAsync(
        IReadOnlyCollection<IBrowserFile> files,
        CancellationToken cancellationToken = default)
    {
        var validationError = Validate(files);
        if (validationError is not null)
            return StartJobsResult.Failed(validationError);

        var uploadDirectory = Path.Combine(
            environment.ContentRootPath,
            "App_Data",
            "uploads",
            $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");

        Directory.CreateDirectory(uploadDirectory);

        IReadOnlyList<string> assemblyPaths;
        try
        {
            assemblyPaths = await SaveFilesAsync(files, uploadDirectory, cancellationToken);
        }
        catch
        {
            Directory.Delete(uploadDirectory, recursive: true);
            throw;
        }

        return LoadAndStart(assemblyPaths, uploadDirectory);
    }

    /// <summary>
    /// Останавливает отдельное задание, сохраняя его карточку и последний снимок.
    /// </summary>
    public async Task<bool> StopAsync(Guid jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
            return false;

        await job.OperationLock.WaitAsync();

        try
        {
            Guid runtimeJobId;

            lock (job.SyncRoot)
            {
                if (job.RuntimeJobId is not { } currentRuntimeJobId ||
                    job.State is ManagedJobState.Starting or ManagedJobState.Cancelling or ManagedJobState.Stopped)
                {
                    return false;
                }

                runtimeJobId = currentRuntimeJobId;
                job.LastSnapshot = orchestrator.GetJobSnapshot(runtimeJobId) ?? job.LastSnapshot;
                job.State = ManagedJobState.Cancelling;
                job.ErrorMessage = null;
            }

            var stopped = await orchestrator.TryRemoveJob(runtimeJobId);

            lock (job.SyncRoot)
            {
                job.RuntimeJobId = null;
                job.State = stopped ? ManagedJobState.Stopped : ManagedJobState.Failed;
                job.ErrorMessage = stopped ? null : "Не удалось корректно остановить задание.";
            }

            return stopped;
        }
        finally
        {
            job.OperationLock.Release();
        }
    }

    /// <summary>
    /// Отменяет выполняющееся задание, если это необходимо, и удаляет его карточку.
    /// </summary>
    public async Task<bool> CancelAsync(Guid jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
            return false;

        await job.OperationLock.WaitAsync();

        try
        {
            Guid? runtimeJobId;

            lock (job.SyncRoot)
            {
                runtimeJobId = job.RuntimeJobId;
                job.State = ManagedJobState.Cancelling;
            }

            if (runtimeJobId is not null)
                await orchestrator.TryRemoveJob(runtimeJobId.Value);

            lock (job.SyncRoot)
            {
                job.RuntimeJobId = null;
                job.State = ManagedJobState.Stopped;
            }

            return _jobs.TryRemove(jobId, out _);
        }
        finally
        {
            job.OperationLock.Release();
        }
    }

    /// <summary>
    /// Запускает новую попытку выполнения ранее загруженного задания.
    /// </summary>
    public async Task<bool> StartJobAsync(Guid jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
            return false;

        await job.OperationLock.WaitAsync();

        try
        {
            Guid? previousRuntimeJobId;

            lock (job.SyncRoot)
            {
                if (job.State is ManagedJobState.Running or ManagedJobState.Starting or ManagedJobState.Cancelling)
                    return false;

                previousRuntimeJobId = job.RuntimeJobId;
                job.State = ManagedJobState.Starting;
                job.ErrorMessage = null;
            }

            if (previousRuntimeJobId is not null)
                await orchestrator.TryRemoveJob(previousRuntimeJobId.Value);

            try
            {
                var instance = (IJob)Activator.CreateInstance(job.JobType)!;
                var result = orchestrator.TryAddJob(instance);

                lock (job.SyncRoot)
                {
                    if (result.JobId is not { } runtimeJobId)
                    {
                        job.RuntimeJobId = null;
                        job.State = ManagedJobState.Failed;
                        job.ErrorMessage = result.ErrorMessage ?? "Ошибка запуска задания.";
                        return false;
                    }

                    job.RuntimeJobId = runtimeJobId;
                    job.StartedAt = DateTimeOffset.Now;
                    job.State = ManagedJobState.Running;
                    job.LastSnapshot = null;
                    return true;
                }
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to restart job type {JobType}", job.JobType.FullName);

                lock (job.SyncRoot)
                {
                    job.RuntimeJobId = null;
                    job.State = ManagedJobState.Failed;
                    job.ErrorMessage = exception.GetBaseException().Message;
                }

                return false;
            }
        }
        finally
        {
            job.OperationLock.Release();
        }
    }

    /// <summary>
    /// Загружает сборки и запускает найденные реализации заданий.
    /// </summary>
    private StartJobsResult LoadAndStart(IReadOnlyList<string> assemblyPaths, string uploadDirectory)
    {
        var errors = new List<string>();
        var startedJobs = new List<RunningJobInfo>();
        var loadContext = new JobAssemblyLoadContext(uploadDirectory);
        var assemblies = new List<Assembly>();

        foreach (var path in assemblyPaths)
        {
            if (string.Equals(
                    Path.GetFileNameWithoutExtension(path),
                    typeof(IJob).Assembly.GetName().Name,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                assemblies.Add(loadContext.LoadFromAssemblyPath(path));
            }
            catch (BadImageFormatException)
            {
                errors.Add($"{Path.GetFileName(path)} не является .NET-сборкой.");
            }
            catch (FileLoadException exception)
            {
                errors.Add($"Не удалось загрузить {Path.GetFileName(path)}: {exception.Message}");
            }
        }

        var jobTypes = assemblies
            .SelectMany(assembly => GetLoadableTypes(assembly, errors))
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IJob).IsAssignableFrom(type))
            .Distinct()
            .ToArray();

        if (jobTypes.Length == 0)
        {
            errors.Add("В выбранных сборках не найдена реализация IJob.");
            return new StartJobsResult(startedJobs, errors);
        }

        foreach (var jobType in jobTypes)
        {
            if (jobType.GetConstructor(Type.EmptyTypes) is null)
            {
                errors.Add($"Для {jobType.FullName} нужен публичный конструктор без параметров.");
                continue;
            }

            try
            {
                var instance = (IJob)Activator.CreateInstance(jobType)!;
                var status = orchestrator.TryAddJob(instance);

                if (status.JobId is not { } runtimeJobId)
                {
                    errors.Add($"{jobType.FullName}: {status.ErrorMessage ?? "ошибка запуска"}.");
                    continue;
                }

                var managedJob = new ManagedJob(
                    runtimeJobId,
                    jobType,
                    jobType.Assembly.GetName().Name ?? Path.GetFileNameWithoutExtension(jobType.Assembly.Location),
                    DateTimeOffset.Now);

                _jobs[managedJob.Id] = managedJob;
                startedJobs.Add(CreateInfo(managedJob));
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to start job type {JobType}", jobType.FullName);
                errors.Add($"{jobType.FullName}: {exception.GetBaseException().Message}");
            }
        }

        return new StartJobsResult(startedJobs, errors);
    }

    /// <summary>
    /// Создает неизменяемую модель карточки и актуализирует ее по данным оркестратора.
    /// </summary>
    private RunningJobInfo CreateInfo(ManagedJob job)
    {
        Guid? runtimeJobId;

        lock (job.SyncRoot)
            runtimeJobId = job.RuntimeJobId;

        var currentSnapshot = runtimeJobId is null
            ? null
            : orchestrator.GetJobSnapshot(runtimeJobId.Value);

        lock (job.SyncRoot)
        {
            if (currentSnapshot is not null)
            {
                job.LastSnapshot = currentSnapshot;

                if (job.State is not (ManagedJobState.Starting or ManagedJobState.Cancelling))
                    job.State = MapState(currentSnapshot.State);

                job.ErrorMessage = currentSnapshot.ErrorMessage;
            }

            var snapshot = job.LastSnapshot;

            return new RunningJobInfo(
                job.Id,
                job.JobType.FullName ?? job.JobType.Name,
                job.AssemblyName,
                job.StartedAt)
            {
                RuntimeJobId = job.RuntimeJobId,
                Status = job.State,
                ProcessedCount = snapshot?.ProcessedCount ?? 0,
                LastProcessedAt = snapshot?.LastProcessedAt,
                LastProcessedMessage = snapshot?.LastProcessedMessage,
                States = snapshot?.States ?? [],
                ErrorMessage = job.ErrorMessage
            };
        }
    }

    private static ManagedJobState MapState(JobExecutionState state) => state switch
    {
        JobExecutionState.Running => ManagedJobState.Running,
        JobExecutionState.Cancelling => ManagedJobState.Cancelling,
        JobExecutionState.Completed => ManagedJobState.Completed,
        JobExecutionState.Cancelled => ManagedJobState.Stopped,
        JobExecutionState.Failed => ManagedJobState.Failed,
        _ => ManagedJobState.Failed
    };

    private static string? Validate(IReadOnlyCollection<IBrowserFile> files)
    {
        if (files.Count == 0)
            return "Выберите хотя бы одну DLL.";

        if (files.Count > MaxFileCount)
            return $"Можно загрузить не больше {MaxFileCount} файлов.";

        if (files.Any(file => !string.Equals(Path.GetExtension(file.Name), ".dll", StringComparison.OrdinalIgnoreCase)))
            return "Поддерживаются только файлы DLL.";

        if (files.Sum(file => file.Size) > MaxPackageSize)
            return "Общий размер файлов не должен превышать 100 МБ.";

        var duplicateName = files
            .GroupBy(file => Path.GetFileName(file.Name), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)?.Key;

        return duplicateName is null
            ? null
            : $"Файл с именем {duplicateName} выбран несколько раз.";
    }

    private static async Task<IReadOnlyList<string>> SaveFilesAsync(
        IEnumerable<IBrowserFile> files,
        string uploadDirectory,
        CancellationToken cancellationToken)
    {
        var paths = new List<string>();

        foreach (var file in files)
        {
            var path = Path.Combine(uploadDirectory, Path.GetFileName(file.Name));
            await using var source = file.OpenReadStream(MaxPackageSize, cancellationToken);
            await using var destination = new FileStream(
                path,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan
                });

            await source.CopyToAsync(destination, cancellationToken);
            paths.Add(path);
        }

        return paths;
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly, ICollection<string> errors)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            var details = exception.LoaderExceptions
                .Where(loaderException => loaderException is not null)
                .Select(loaderException => loaderException!.Message)
                .Distinct()
                .Take(2);

            errors.Add($"Не все типы из {assembly.GetName().Name} загружены: {string.Join("; ", details)}");
            return exception.Types.OfType<Type>();
        }
    }

    private sealed class JobAssemblyLoadContext(string directory) : AssemblyLoadContext
    {
        /// <inheritdoc />
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (string.Equals(
                    assemblyName.Name,
                    typeof(IJob).Assembly.GetName().Name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return typeof(IJob).Assembly;
            }

            var dependencyPath = Path.Combine(directory, $"{assemblyName.Name}.dll");
            return File.Exists(dependencyPath)
                ? LoadFromAssemblyPath(dependencyPath)
                : null;
        }
    }

    private sealed class ManagedJob(
        Guid id,
        Type jobType,
        string assemblyName,
        DateTimeOffset startedAt)
    {
        internal Guid Id { get; } = id;
        internal Type JobType { get; } = jobType;
        internal string AssemblyName { get; } = assemblyName;
        internal object SyncRoot { get; } = new();
        internal SemaphoreSlim OperationLock { get; } = new(1, 1);
        internal DateTimeOffset StartedAt { get; set; } = startedAt;
        internal Guid? RuntimeJobId { get; set; } = id;
        internal ManagedJobState State { get; set; } = ManagedJobState.Running;
        internal JobExecutionSnapshot? LastSnapshot { get; set; }
        internal string? ErrorMessage { get; set; }
    }
}

/// <summary>
/// Состояние управляемого задания в веб-интерфейсе.
/// </summary>
public enum ManagedJobState
{
    Starting,
    Running,
    Cancelling,
    Stopped,
    Completed,
    Failed
}

/// <summary>
/// Сведения о загруженном задании и его последней попытке выполнения.
/// </summary>
public sealed record RunningJobInfo(
    Guid JobId,
    string JobType,
    string AssemblyName,
    DateTimeOffset StartedAt)
{
    public Guid? RuntimeJobId { get; init; }
    public ManagedJobState Status { get; init; }
    public long ProcessedCount { get; init; }
    public DateTimeOffset? LastProcessedAt { get; init; }
    public string? LastProcessedMessage { get; init; }
    public IReadOnlyList<JobStateSnapshot> States { get; init; } = [];
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Результат запуска заданий из загруженных сборок.
/// </summary>
public sealed record StartJobsResult(
    IReadOnlyList<RunningJobInfo> StartedJobs,
    IReadOnlyList<string> Errors)
{
    public static StartJobsResult Failed(string error) => new([], [error]);
}
