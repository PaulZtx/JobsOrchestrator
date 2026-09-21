using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using Jobs;
using Jobs.Diagnostics;
using Jobs.JobsEntities.Interfaces;

namespace JobsOperator.Worker.Services;

/// <summary>
/// Загружает реализации заданий и управляет их пользовательским жизненным циклом
/// </summary>
/// <param name="orchestrator">Оркестратор попыток выполнения заданий</param>
/// <param name="logger">Журнал ошибок загрузки и запуска заданий</param>
internal sealed class WorkerJobManager(
    JobsOrchestrator orchestrator,
    ILogger<WorkerJobManager> logger)
{
    private readonly ConcurrentDictionary<Guid, ManagedJob> _jobs = [];

    /// <summary>
    /// Возвращает все известные задания с актуальными снимками выполнения
    /// </summary>
    /// <returns>Карточки заданий в порядке убывания времени запуска</returns>
    public IReadOnlyList<ManagedJobInfo> GetJobs() => _jobs.Values
        .Select(CreateInfo)
        .OrderByDescending(job => job.StartedAt)
        .ToArray();

    /// <summary>
    /// Возвращает одно известное задание с актуальным снимком выполнения
    /// </summary>
    /// <param name="jobId">Постоянный идентификатор карточки задания</param>
    /// <returns>Карточка задания или null при отсутствии идентификатора</returns>
    public ManagedJobInfo? GetJob(Guid jobId)
    {
        return _jobs.TryGetValue(jobId, out var job)
            ? CreateInfo(job)
            : null;
    }

    /// <summary>
    /// Загружает реализации IJob из опубликованного пакета и запускает их
    /// </summary>
    /// <param name="package">Описание опубликованного пакета</param>
    /// <param name="packageDirectory">Абсолютный путь каталога пакета</param>
    /// <returns>Запущенные задания и ошибки отдельных сборок или типов</returns>
    public StartPackageResult StartPackage(
        StoredJobPackage package,
        string packageDirectory)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);

        var errors = new List<string>();
        var startedJobs = new List<ManagedJobInfo>();
        var loadContext = new JobAssemblyLoadContext(packageDirectory);
        var assemblies = new List<Assembly>();

        foreach (var file in package.Files)
        {
            if (string.Equals(
                    Path.GetFileNameWithoutExtension(file.FileName),
                    typeof(IJob).Assembly.GetName().Name,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = Path.GetFullPath(Path.Combine(packageDirectory, file.FileName));

            try
            {
                assemblies.Add(loadContext.LoadFromAssemblyPath(path));
            }
            catch (BadImageFormatException)
            {
                errors.Add($"{file.FileName} is not a .NET assembly");
            }
            catch (FileLoadException exception)
            {
                errors.Add($"Could not load {file.FileName}: {exception.Message}");
            }
        }

        var jobTypes = assemblies
            .SelectMany(assembly => GetLoadableTypes(assembly, errors))
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IJob).IsAssignableFrom(type))
            .Distinct()
            .ToArray();

        if (jobTypes.Length == 0)
        {
            errors.Add("No IJob implementation was found in the package");
            return new StartPackageResult(startedJobs, errors);
        }

        foreach (var jobType in jobTypes)
        {
            if (jobType.GetConstructor(Type.EmptyTypes) is null)
            {
                errors.Add($"{jobType.FullName} requires a public parameterless constructor");
                continue;
            }

            try
            {
                var instance = (IJob)Activator.CreateInstance(jobType)!;
                var status = orchestrator.TryAddJob(instance);

                if (status.JobId is not { } runtimeJobId)
                {
                    errors.Add($"{jobType.FullName}: {status.ErrorMessage ?? "could not start"}");
                    continue;
                }

                var job = new ManagedJob(
                    runtimeJobId,
                    jobType,
                    jobType.Assembly.GetName().Name ?? Path.GetFileNameWithoutExtension(jobType.Assembly.Location),
                    DateTimeOffset.UtcNow,
                    loadContext);

                _jobs[job.Id] = job;
                startedJobs.Add(CreateInfo(job));
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to start job type {JobType}", jobType.FullName);
                errors.Add($"{jobType.FullName}: {exception.GetBaseException().Message}");
            }
        }

        return new StartPackageResult(startedJobs, errors);
    }

    /// <summary>
    /// Останавливает выполняемое задание и сохраняет его карточку
    /// </summary>
    /// <param name="jobId">Постоянный идентификатор карточки задания</param>
    /// <param name="cancellationToken">Токен отмены ожидания блокировки операции</param>
    /// <returns>Задача с результатом операции и актуальной карточкой</returns>
    public async Task<JobOperationResult> StopAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
            return new JobOperationResult(JobOperationStatus.NotFound, null, "Job was not found");

        await job.OperationLock.WaitAsync(cancellationToken);

        try
        {
            _ = CreateInfo(job);
            Guid runtimeJobId;

            lock (job.SyncRoot)
            {
                if (job.RuntimeJobId is not { } currentRuntimeJobId ||
                    job.State is not ManagedJobState.Running)
                {
                    return new JobOperationResult(
                        JobOperationStatus.Conflict,
                        CreateInfo(job),
                        "Job is not running");
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
                job.ErrorMessage = stopped ? null : "Could not stop job gracefully";
            }

            return stopped
                ? new JobOperationResult(JobOperationStatus.Success, CreateInfo(job), null)
                : new JobOperationResult(JobOperationStatus.Failed, CreateInfo(job), job.ErrorMessage);
        }
        finally
        {
            job.OperationLock.Release();
        }
    }

    /// <summary>
    /// Запускает новую попытку ранее загруженного задания
    /// </summary>
    /// <param name="jobId">Постоянный идентификатор карточки задания</param>
    /// <param name="cancellationToken">Токен отмены ожидания блокировки операции</param>
    /// <returns>Задача с результатом запуска и актуальной карточкой</returns>
    public async Task<JobOperationResult> StartAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
            return new JobOperationResult(JobOperationStatus.NotFound, null, "Job was not found");

        await job.OperationLock.WaitAsync(cancellationToken);

        try
        {
            _ = CreateInfo(job);
            Guid? previousRuntimeJobId;

            lock (job.SyncRoot)
            {
                if (job.State is ManagedJobState.Running or ManagedJobState.Starting or ManagedJobState.Cancelling)
                {
                    return new JobOperationResult(
                        JobOperationStatus.Conflict,
                        CreateInfo(job),
                        "Job is already active");
                }

                previousRuntimeJobId = job.RuntimeJobId;
                job.State = ManagedJobState.Starting;
                job.ErrorMessage = null;
            }

            if (previousRuntimeJobId is not null)
                await orchestrator.TryRemoveJob(previousRuntimeJobId.Value);

            try
            {
                var instance = (IJob)Activator.CreateInstance(job.JobType)!;
                var status = orchestrator.TryAddJob(instance);

                lock (job.SyncRoot)
                {
                    if (status.JobId is not { } runtimeJobId)
                    {
                        job.RuntimeJobId = null;
                        job.State = ManagedJobState.Failed;
                        job.ErrorMessage = status.ErrorMessage ?? "Could not start job";
                        return new JobOperationResult(
                            JobOperationStatus.Failed,
                            CreateInfo(job),
                            job.ErrorMessage);
                    }

                    job.RuntimeJobId = runtimeJobId;
                    job.StartedAt = DateTimeOffset.UtcNow;
                    job.State = ManagedJobState.Running;
                    job.LastSnapshot = null;
                    job.ErrorMessage = null;
                }

                return new JobOperationResult(JobOperationStatus.Success, CreateInfo(job), null);
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

                return new JobOperationResult(
                    JobOperationStatus.Failed,
                    CreateInfo(job),
                    job.ErrorMessage);
            }
        }
        finally
        {
            job.OperationLock.Release();
        }
    }

    /// <summary>
    /// Останавливает задание при необходимости и удаляет его карточку
    /// </summary>
    /// <param name="jobId">Постоянный идентификатор карточки задания</param>
    /// <param name="cancellationToken">Токен отмены ожидания блокировки операции</param>
    /// <returns>Задача с результатом удаления</returns>
    public async Task<JobOperationResult> DeleteAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
            return new JobOperationResult(JobOperationStatus.NotFound, null, "Job was not found");

        await job.OperationLock.WaitAsync(cancellationToken);

        try
        {
            Guid? runtimeJobId;

            lock (job.SyncRoot)
            {
                runtimeJobId = job.RuntimeJobId;
                if (runtimeJobId is not null)
                    job.State = ManagedJobState.Cancelling;
            }

            if (runtimeJobId is not null)
                await orchestrator.TryRemoveJob(runtimeJobId.Value);

            return _jobs.TryRemove(jobId, out _)
                ? new JobOperationResult(JobOperationStatus.Success, null, null)
                : new JobOperationResult(JobOperationStatus.NotFound, null, "Job was already deleted");
        }
        finally
        {
            job.OperationLock.Release();
        }
    }

    /// <summary>
    /// Создает карточку и актуализирует ее по данным оркестратора
    /// </summary>
    /// <param name="job">Управляемое задание</param>
    /// <returns>Неизменяемая актуальная карточка задания</returns>
    private ManagedJobInfo CreateInfo(ManagedJob job)
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

            return new ManagedJobInfo(
                job.Id,
                job.RuntimeJobId,
                job.JobType.FullName ?? job.JobType.Name,
                job.AssemblyName,
                job.StartedAt,
                job.State,
                snapshot?.ProcessedCount ?? 0,
                snapshot?.LastProcessedAt,
                snapshot?.LastProcessedMessage,
                snapshot?.States ?? [],
                job.ErrorMessage);
        }
    }

    /// <summary>
    /// Преобразует состояние попытки оркестратора в состояние карточки задания
    /// </summary>
    /// <param name="state">Состояние попытки выполнения</param>
    /// <returns>Соответствующее состояние карточки</returns>
    private static ManagedJobState MapState(JobExecutionState state) => state switch
    {
        JobExecutionState.Running => ManagedJobState.Running,
        JobExecutionState.Cancelling => ManagedJobState.Cancelling,
        JobExecutionState.Completed => ManagedJobState.Completed,
        JobExecutionState.Cancelled => ManagedJobState.Stopped,
        JobExecutionState.Failed => ManagedJobState.Failed,
        _ => ManagedJobState.Failed
    };

    /// <summary>
    /// Возвращает доступные типы сборки и регистрирует ошибки частичной загрузки
    /// </summary>
    /// <param name="assembly">Сборка для поиска доступных типов</param>
    /// <param name="errors">Коллекция для сообщений об ошибках загрузки типов</param>
    /// <returns>Все типы или успешно загруженные типы при частичной ошибке</returns>
    private static IEnumerable<Type> GetLoadableTypes(
        Assembly assembly,
        ICollection<string> errors)
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

            errors.Add($"Not all types from {assembly.GetName().Name} were loaded: {string.Join("; ", details)}");
            return exception.Types.OfType<Type>();
        }
    }

    /// <summary>
    /// Разрешает зависимости загруженных заданий из каталога пакета
    /// </summary>
    /// <param name="directory">Каталог поиска зависимостей</param>
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

    /// <summary>
    /// Хранит изменяемое состояние карточки задания
    /// </summary>
    /// <param name="id">Постоянный идентификатор карточки</param>
    /// <param name="jobType">Тип реализации задания</param>
    /// <param name="assemblyName">Имя сборки задания</param>
    /// <param name="startedAt">Время запуска первой попытки</param>
    /// <param name="loadContext">Контекст загруженной сборки</param>
    private sealed class ManagedJob(
        Guid id,
        Type jobType,
        string assemblyName,
        DateTimeOffset startedAt,
        AssemblyLoadContext loadContext)
    {
        internal Guid Id { get; } = id;
        internal Type JobType { get; } = jobType;
        internal string AssemblyName { get; } = assemblyName;
        internal AssemblyLoadContext LoadContext { get; } = loadContext;
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
/// Состояние пользовательской карточки задания
/// </summary>
internal enum ManagedJobState
{
    Starting,
    Running,
    Cancelling,
    Stopped,
    Completed,
    Failed
}

/// <summary>
/// Неизменяемая карточка задания Worker
/// </summary>
/// <param name="JobId">Постоянный идентификатор карточки</param>
/// <param name="RuntimeJobId">Идентификатор текущей попытки или null</param>
/// <param name="JobType">Полное имя типа задания</param>
/// <param name="AssemblyName">Имя сборки задания</param>
/// <param name="StartedAt">Время запуска последней попытки</param>
/// <param name="Status">Состояние карточки</param>
/// <param name="ProcessedCount">Количество обработанных сообщений</param>
/// <param name="LastProcessedAt">Время последнего обработанного сообщения</param>
/// <param name="LastProcessedMessage">Диагностическое представление последнего сообщения</param>
/// <param name="States">Снимки внутренних состояний задания</param>
/// <param name="ErrorMessage">Сообщение последней ошибки</param>
internal sealed record ManagedJobInfo(
    Guid JobId,
    Guid? RuntimeJobId,
    string JobType,
    string AssemblyName,
    DateTimeOffset StartedAt,
    ManagedJobState Status,
    long ProcessedCount,
    DateTimeOffset? LastProcessedAt,
    string? LastProcessedMessage,
    IReadOnlyList<JobStateSnapshot> States,
    string? ErrorMessage);

/// <summary>
/// Результат запуска реализаций задания из пакета
/// </summary>
/// <param name="StartedJobs">Успешно запущенные задания</param>
/// <param name="Errors">Ошибки загрузки и запуска</param>
internal sealed record StartPackageResult(
    IReadOnlyList<ManagedJobInfo> StartedJobs,
    IReadOnlyList<string> Errors);

/// <summary>
/// Результат команды над заданием
/// </summary>
/// <param name="Status">Категория результата</param>
/// <param name="Job">Актуальная карточка или null</param>
/// <param name="ErrorMessage">Описание ошибки или null</param>
internal sealed record JobOperationResult(
    JobOperationStatus Status,
    ManagedJobInfo? Job,
    string? ErrorMessage);

/// <summary>
/// Категория результата команды над заданием
/// </summary>
internal enum JobOperationStatus
{
    Success,
    NotFound,
    Conflict,
    Failed
}
