using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using Jobs;
using Jobs.JobsEntities.Interfaces;
using Microsoft.AspNetCore.Components.Forms;

namespace JobsOperator.Web.Services;

/// <summary>
/// Загружает сборки и управляет запущенными заданиями
/// </summary>
/// <param name="orchestrator">Оркестратор заданий</param>
/// <param name="environment">Окружение веб-приложения</param>
/// <param name="logger">Журнал сервиса</param>
public sealed class JobExecutionService(
    JobsOrchestrator orchestrator,
    IWebHostEnvironment environment,
    ILogger<JobExecutionService> logger)
{
    private const int MaxFileCount = 20;
    private const long MaxPackageSize = 100 * 1024 * 1024;
    private readonly ConcurrentDictionary<Guid, RunningJobInfo> _runningJobs = [];

    /// <summary>
    /// Возвращает запущенные задания в порядке времени запуска
    /// </summary>
    /// <returns>Сведения о запущенных заданиях</returns>
    public IReadOnlyList<RunningJobInfo> GetRunningJobs() => _runningJobs.Values
        .OrderByDescending(job => job.StartedAt)
        .ToArray();

    /// <summary>
    /// Сохраняет выбранные сборки и запускает найденные задания
    /// </summary>
    /// <param name="files">Выбранные файлы сборок</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Сведения о запущенных заданиях и ошибках</returns>
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
    /// Останавливает задание и удаляет его из списка запущенных
    /// </summary>
    /// <param name="jobId">Идентификатор задания</param>
    /// <returns>Признак успешной остановки</returns>
    public async Task<bool> StopAsync(Guid jobId)
    {
        var stopped = await orchestrator.TryRemoveJob(jobId);
        if (stopped)
            _runningJobs.TryRemove(jobId, out _);

        return stopped;
    }

    /// <summary>
    /// Загружает сборки и запускает найденные реализации заданий
    /// </summary>
    /// <param name="assemblyPaths">Пути к загруженным сборкам</param>
    /// <param name="uploadDirectory">Каталог загруженных файлов</param>
    /// <returns>Сведения о запущенных заданиях и ошибках</returns>
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
                var job = (IJob)Activator.CreateInstance(jobType)!;
                var status = orchestrator.TryAddJob(job);

                if (status.JobId is not { } jobId)
                {
                    errors.Add($"{jobType.FullName}: {status.ErrorMessage ?? "ошибка запуска"}.");
                    continue;
                }

                var info = new RunningJobInfo(
                    jobId,
                    jobType.FullName ?? jobType.Name,
                    jobType.Assembly.GetName().Name ?? Path.GetFileNameWithoutExtension(jobType.Assembly.Location),
                    DateTimeOffset.Now);

                _runningJobs[jobId] = info;
                startedJobs.Add(info);
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
    /// Проверяет выбранные файлы перед сохранением
    /// </summary>
    /// <param name="files">Выбранные файлы</param>
    /// <returns>Сообщение об ошибке или null при успешной проверке</returns>
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

    /// <summary>
    /// Сохраняет выбранные файлы в каталог загрузки
    /// </summary>
    /// <param name="files">Выбранные файлы</param>
    /// <param name="uploadDirectory">Каталог загрузки</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Пути к сохраненным файлам</returns>
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

    /// <summary>
    /// Возвращает доступные типы сборки и сохраняет ошибки загрузки
    /// </summary>
    /// <param name="assembly">Проверяемая сборка</param>
    /// <param name="errors">Коллекция сообщений об ошибках</param>
    /// <returns>Доступные типы сборки</returns>
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

    /// <summary>
    /// Загружает сборку задания и ее зависимости из одного каталога
    /// </summary>
    /// <param name="directory">Каталог сборки и зависимостей</param>
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
}

/// <summary>
/// Сведения о запущенном задании
/// </summary>
/// <param name="JobId">Идентификатор задания</param>
/// <param name="JobType">Полное имя типа задания</param>
/// <param name="AssemblyName">Имя сборки</param>
/// <param name="StartedAt">Время запуска</param>
public sealed record RunningJobInfo(
    Guid JobId,
    string JobType,
    string AssemblyName,
    DateTimeOffset StartedAt);

/// <summary>
/// Результат запуска заданий из загруженных сборок
/// </summary>
/// <param name="StartedJobs">Успешно запущенные задания</param>
/// <param name="Errors">Ошибки загрузки и запуска</param>
public sealed record StartJobsResult(
    IReadOnlyList<RunningJobInfo> StartedJobs,
    IReadOnlyList<string> Errors)
{
    /// <summary>
    /// Создает результат с одной ошибкой без запущенных заданий
    /// </summary>
    /// <param name="error">Сообщение об ошибке</param>
    /// <returns>Неуспешный результат запуска</returns>
    public static StartJobsResult Failed(string error) => new([], [error]);
}
