using System.Security.Cryptography;
using System.Text.Json;

namespace JobsOperator.Worker.Services;

/// <summary>
/// Создает сессии потоковой загрузки пакетов заданий
/// </summary>
/// <param name="environment">Окружение Worker с корневым каталогом приложения</param>
/// <param name="configuration">Конфигурация расположения хранилища пакетов</param>
/// <param name="logger">Журнал операций с хранилищем пакетов</param>
internal sealed class JobPackageStorage(
    IHostEnvironment environment,
    IConfiguration configuration,
    ILogger<JobPackageStorage> logger)
{
    private const int MaxFileCount = 20;
    private const long MaxPackageSize = 100 * 1024 * 1024;

    private readonly string _packagesDirectory = ResolvePackagesDirectory(environment, configuration);

    /// <summary>
    /// Начинает изолированную загрузку пакета
    /// </summary>
    /// <param name="packageId">Идентификатор создаваемого пакета</param>
    /// <returns>Сессия последовательной записи файлов пакета</returns>
    public JobPackageUpload BeginUpload(Guid packageId)
    {
        if (packageId == Guid.Empty)
            throw new ArgumentException("Package id cannot be empty", nameof(packageId));

        return new JobPackageUpload(
            _packagesDirectory,
            packageId,
            MaxFileCount,
            MaxPackageSize,
            logger);
    }

    /// <summary>
    /// Возвращает каталог опубликованного пакета
    /// </summary>
    /// <param name="packageId">Идентификатор опубликованного пакета</param>
    /// <returns>Абсолютный путь каталога пакета</returns>
    public string GetPackageDirectory(Guid packageId)
    {
        if (packageId == Guid.Empty)
            throw new ArgumentException("Package id cannot be empty", nameof(packageId));

        var path = Path.Combine(_packagesDirectory, packageId.ToString("N"));
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Package '{packageId}' was not found");

        return path;
    }

    /// <summary>
    /// Определяет абсолютный путь каталога пакетов
    /// </summary>
    /// <param name="environment">Окружение Worker с корневым каталогом приложения</param>
    /// <param name="configuration">Конфигурация расположения хранилища пакетов</param>
    /// <returns>Абсолютный путь каталога пакетов</returns>
    private static string ResolvePackagesDirectory(
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        var configuredPath = configuration["JobPackages:Path"];
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(environment.ContentRootPath, "App_Data", "packages")
            : Path.GetFullPath(configuredPath, environment.ContentRootPath);

        return Path.GetFullPath(path);
    }
}

/// <summary>
/// Последовательно записывает один пакет и публикует его после полной проверки
/// </summary>
internal sealed class JobPackageUpload : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly Guid _packageId;
    private readonly int _maxFileCount;
    private readonly long _maxPackageSize;
    private readonly ILogger _logger;
    private readonly string _temporaryDirectory;
    private readonly string _finalDirectory;
    private readonly HashSet<string> _fileNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<StoredJobPackageFile> _files = [];

    private FileStream? _currentStream;
    private IncrementalHash? _currentHash;
    private string? _currentFileName;
    private long _currentExpectedLength;
    private long _currentWrittenLength;
    private long _declaredTotalBytes;
    private long _totalBytes;
    private bool _completed;
    private bool _disposed;

    /// <summary>
    /// Создает сессию записи во временный каталог
    /// </summary>
    /// <param name="packagesDirectory">Корневой каталог опубликованных пакетов</param>
    /// <param name="packageId">Идентификатор создаваемого пакета</param>
    /// <param name="maxFileCount">Максимальное число файлов в пакете</param>
    /// <param name="maxPackageSize">Максимальный суммарный размер файлов в байтах</param>
    /// <param name="logger">Журнал ошибок очистки временных данных</param>
    internal JobPackageUpload(
        string packagesDirectory,
        Guid packageId,
        int maxFileCount,
        long maxPackageSize,
        ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagesDirectory);
        ArgumentNullException.ThrowIfNull(logger);

        _packageId = packageId;
        _maxFileCount = maxFileCount;
        _maxPackageSize = maxPackageSize;
        _logger = logger;

        Directory.CreateDirectory(packagesDirectory);

        var uploadsDirectory = Path.Combine(packagesDirectory, ".uploads");
        Directory.CreateDirectory(uploadsDirectory);

        _temporaryDirectory = Path.Combine(
            uploadsDirectory,
            $"{packageId:N}.{Guid.NewGuid():N}.tmp");
        _finalDirectory = Path.Combine(packagesDirectory, packageId.ToString("N"));

        if (Directory.Exists(_finalDirectory))
        {
            throw new JobPackageUploadException(
                JobPackageUploadError.AlreadyExists,
                $"Package '{packageId}' already exists");
        }

        Directory.CreateDirectory(_temporaryDirectory);
    }

    /// <summary>
    /// Начинает прием следующего файла пакета
    /// </summary>
    /// <param name="fileName">Имя файла без элементов пути</param>
    /// <param name="length">Ожидаемый размер файла в байтах</param>
    /// <param name="cancellationToken">Токен отмены операции</param>
    /// <returns>Задача завершения предыдущего файла и открытия следующего</returns>
    public async Task StartFileAsync(
        string fileName,
        long length,
        CancellationToken cancellationToken)
    {
        EnsureWritable();
        await FinishCurrentFileAsync(cancellationToken);
        ValidateFile(fileName, length);

        var path = Path.Combine(_temporaryDirectory, fileName);
        _currentStream = new FileStream(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });
        _currentHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        _currentFileName = fileName;
        _currentExpectedLength = length;
        _currentWrittenLength = 0;
        _declaredTotalBytes += length;
        _fileNames.Add(fileName);
    }

    /// <summary>
    /// Записывает очередной блок текущего файла
    /// </summary>
    /// <param name="content">Бинарный блок файла</param>
    /// <param name="cancellationToken">Токен отмены операции</param>
    /// <returns>Задача завершения записи блока</returns>
    public async Task WriteAsync(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        EnsureWritable();

        if (_currentStream is null || _currentHash is null)
        {
            throw new JobPackageUploadException(
                JobPackageUploadError.InvalidPackage,
                "File content was received before its header");
        }

        if (content.IsEmpty)
            return;

        if (content.Length > _currentExpectedLength - _currentWrittenLength)
        {
            throw new JobPackageUploadException(
                JobPackageUploadError.InvalidPackage,
                $"File '{_currentFileName}' contains more bytes than declared");
        }

        if (content.Length > _maxPackageSize - _totalBytes)
        {
            throw new JobPackageUploadException(
                JobPackageUploadError.LimitExceeded,
                "Package size exceeds 100 MB");
        }

        await _currentStream.WriteAsync(content, cancellationToken);
        _currentHash.AppendData(content.Span);
        _currentWrittenLength += content.Length;
        _totalBytes += content.Length;
    }

    /// <summary>
    /// Завершает проверку и атомарно публикует пакет
    /// </summary>
    /// <param name="cancellationToken">Токен отмены операции</param>
    /// <returns>Задача с описанием опубликованного пакета</returns>
    public async Task<StoredJobPackage> CompleteAsync(CancellationToken cancellationToken)
    {
        EnsureWritable();
        await FinishCurrentFileAsync(cancellationToken);

        if (_files.Count == 0)
        {
            throw new JobPackageUploadException(
                JobPackageUploadError.InvalidPackage,
                "Package does not contain files");
        }

        var package = new StoredJobPackage(
            _packageId,
            DateTimeOffset.UtcNow,
            _totalBytes,
            _files.ToArray());
        var manifestPath = Path.Combine(_temporaryDirectory, "package.json");

        await using (var stream = new FileStream(
                         manifestPath,
                         new FileStreamOptions
                         {
                             Mode = FileMode.CreateNew,
                             Access = FileAccess.Write,
                             Share = FileShare.None,
                             Options = FileOptions.Asynchronous | FileOptions.WriteThrough
                         }))
        {
            await JsonSerializer.SerializeAsync(stream, package, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        try
        {
            Directory.Move(_temporaryDirectory, _finalDirectory);
        }
        catch (IOException exception) when (Directory.Exists(_finalDirectory))
        {
            throw new JobPackageUploadException(
                JobPackageUploadError.AlreadyExists,
                $"Package '{_packageId}' already exists",
                exception);
        }

        _completed = true;
        return package;
    }

    /// <summary>
    /// Закрывает открытый файл и удаляет неопубликованные временные данные
    /// </summary>
    /// <returns>Задача завершения освобождения файловых ресурсов</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_currentStream is not null)
            await _currentStream.DisposeAsync();

        _currentHash?.Dispose();

        if (_completed || !Directory.Exists(_temporaryDirectory))
            return;

        try
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                exception,
                "Could not remove temporary package directory {Directory}",
                _temporaryDirectory);
        }
    }

    /// <summary>
    /// Завершает запись текущего файла и добавляет его описание в пакет
    /// </summary>
    /// <param name="cancellationToken">Токен отмены операции</param>
    /// <returns>Задача завершения записи и вычисления контрольной суммы</returns>
    private async Task FinishCurrentFileAsync(CancellationToken cancellationToken)
    {
        if (_currentStream is null || _currentHash is null || _currentFileName is null)
            return;

        await _currentStream.FlushAsync(cancellationToken);
        await _currentStream.DisposeAsync();
        _currentStream = null;

        var hash = Convert.ToHexString(_currentHash.GetHashAndReset()).ToLowerInvariant();
        _currentHash.Dispose();
        _currentHash = null;

        if (_currentWrittenLength != _currentExpectedLength)
        {
            throw new JobPackageUploadException(
                JobPackageUploadError.InvalidPackage,
                $"File '{_currentFileName}' has {_currentWrittenLength} bytes instead of {_currentExpectedLength}");
        }

        _files.Add(new StoredJobPackageFile(
            _currentFileName,
            _currentWrittenLength,
            hash));
        _currentFileName = null;
        _currentExpectedLength = 0;
        _currentWrittenLength = 0;
    }

    /// <summary>
    /// Проверяет имя, размер и лимиты нового файла
    /// </summary>
    /// <param name="fileName">Проверяемое имя файла</param>
    /// <param name="length">Объявленный размер файла</param>
    private void ValidateFile(string fileName, long length)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.IndexOfAny(['/', '\\']) >= 0 ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
        {
            throw new JobPackageUploadException(
                JobPackageUploadError.InvalidPackage,
                "File name is invalid");
        }

        if (!string.Equals(Path.GetExtension(fileName), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new JobPackageUploadException(
                JobPackageUploadError.InvalidPackage,
                $"File '{fileName}' is not a DLL");
        }

        if (length <= 0)
        {
            throw new JobPackageUploadException(
                JobPackageUploadError.InvalidPackage,
                $"File '{fileName}' is empty");
        }

        if (_fileNames.Count >= _maxFileCount)
        {
            throw new JobPackageUploadException(
                JobPackageUploadError.LimitExceeded,
                $"Package contains more than {_maxFileCount} files");
        }

        if (_fileNames.Contains(fileName))
        {
            throw new JobPackageUploadException(
                JobPackageUploadError.InvalidPackage,
                $"File '{fileName}' occurs more than once");
        }

        if (length > _maxPackageSize - _declaredTotalBytes)
        {
            throw new JobPackageUploadException(
                JobPackageUploadError.LimitExceeded,
                "Package size exceeds 100 MB");
        }
    }

    /// <summary>
    /// Проверяет доступность сессии для записи
    /// </summary>
    private void EnsureWritable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_completed)
            throw new InvalidOperationException("Package upload has already completed");
    }
}

/// <summary>
/// Опубликованный пакет задания
/// </summary>
/// <param name="PackageId">Идентификатор пакета</param>
/// <param name="CreatedAtUtc">Время публикации пакета</param>
/// <param name="TotalBytes">Суммарный размер файлов в байтах</param>
/// <param name="Files">Файлы пакета</param>
internal sealed record StoredJobPackage(
    Guid PackageId,
    DateTimeOffset CreatedAtUtc,
    long TotalBytes,
    IReadOnlyList<StoredJobPackageFile> Files);

/// <summary>
/// Файл опубликованного пакета
/// </summary>
/// <param name="FileName">Имя файла</param>
/// <param name="Length">Размер файла в байтах</param>
/// <param name="Sha256">Контрольная сумма SHA-256</param>
internal sealed record StoredJobPackageFile(
    string FileName,
    long Length,
    string Sha256);

/// <summary>
/// Категория ошибки загрузки пакета
/// </summary>
internal enum JobPackageUploadError
{
    InvalidPackage,
    LimitExceeded,
    AlreadyExists
}

/// <summary>
/// Ошибка проверки или публикации пакета задания
/// </summary>
internal sealed class JobPackageUploadException : Exception
{
    /// <summary>
    /// Создает ошибку загрузки пакета
    /// </summary>
    /// <param name="error">Категория ошибки</param>
    /// <param name="message">Описание ошибки</param>
    /// <param name="innerException">Исходная ошибка или null при ее отсутствии</param>
    public JobPackageUploadException(
        JobPackageUploadError error,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
    }

    /// <summary>
    /// Категория ошибки
    /// </summary>
    public JobPackageUploadError Error { get; }
}
