using System.Buffers;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using JobsOperator.Protos;
using Microsoft.AspNetCore.Mvc;
using GrpcStatusCode = Grpc.Core.StatusCode;
using ProtoJobResponse = JobsOperator.Protos.JobResponse;

namespace JobsOperator.Api.Controllers;

/// <summary>
/// Предоставляет REST API управления заданиями и их пакетами
/// </summary>
/// <param name="workerClient">gRPC-клиент Worker</param>
/// <param name="logger">Журнал ошибок взаимодействия с Worker</param>
[ApiController]
[Route("api/v1")]
public sealed class JobsController(
    JobService.JobServiceClient workerClient,
    ILogger<JobsController> logger) : ControllerBase
{
    private const int MaxFileCount = 20;
    private const int ChunkSize = 64 * 1024;
    private const long MaxPackageSize = 100 * 1024 * 1024;
    private const long MaxRequestSize = MaxPackageSize + 2 * 1024 * 1024;

    /// <summary>
    /// Возвращает текущие задания Worker
    /// </summary>
    /// <param name="cancellationToken">Токен отмены HTTP-запроса</param>
    /// <returns>Задача со списком актуальных карточек заданий или ProblemDetails</returns>
    [HttpGet("jobs")]
    [ProducesResponseType<IReadOnlyList<RunningJobResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<RunningJobResponse>>> GetJobsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await workerClient.GetJobsAsync(
                new Empty(),
                cancellationToken: cancellationToken);

            return Ok(response.Responses.Select(MapJob).ToArray());
        }
        catch (RpcException exception)
        {
            return CreateGrpcProblem(exception, "read jobs");
        }
    }

    /// <summary>
    /// Возвращает одну карточку задания Worker
    /// </summary>
    /// <param name="jobId">Постоянный идентификатор карточки задания</param>
    /// <param name="cancellationToken">Токен отмены HTTP-запроса</param>
    /// <returns>Задача с актуальной карточкой задания или ProblemDetails</returns>
    [HttpGet("jobs/{jobId:guid}")]
    [ProducesResponseType<RunningJobResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RunningJobResponse>> GetJobAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await workerClient.GetJobAsync(
                CreateJobRequest(jobId),
                cancellationToken: cancellationToken);

            return Ok(MapJob(response));
        }
        catch (RpcException exception)
        {
            return CreateGrpcProblem(exception, "read job");
        }
    }

    /// <summary>
    /// Запускает новую попытку выполнения ранее загруженного задания
    /// </summary>
    /// <param name="jobId">Постоянный идентификатор карточки задания</param>
    /// <param name="cancellationToken">Токен отмены HTTP-запроса</param>
    /// <returns>Задача с актуальной карточкой задания или ProblemDetails</returns>
    [HttpPost("jobs/{jobId:guid}/start")]
    [ProducesResponseType<RunningJobResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RunningJobResponse>> StartJobAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await workerClient.StartJobAsync(
                CreateJobRequest(jobId),
                cancellationToken: cancellationToken);

            return Ok(MapJob(response));
        }
        catch (RpcException exception)
        {
            return CreateGrpcProblem(exception, "start job");
        }
    }

    /// <summary>
    /// Останавливает выполняющееся задание
    /// </summary>
    /// <param name="jobId">Постоянный идентификатор карточки задания</param>
    /// <param name="cancellationToken">Токен отмены HTTP-запроса</param>
    /// <returns>Задача с остановленной карточкой задания или ProblemDetails</returns>
    [HttpPost("jobs/{jobId:guid}/stop")]
    [ProducesResponseType<RunningJobResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RunningJobResponse>> StopJobAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await workerClient.StopJobAsync(
                CreateJobRequest(jobId),
                cancellationToken: cancellationToken);

            return Ok(MapJob(response));
        }
        catch (RpcException exception)
        {
            return CreateGrpcProblem(exception, "stop job");
        }
    }

    /// <summary>
    /// Останавливает задание при необходимости и удаляет его карточку
    /// </summary>
    /// <param name="jobId">Постоянный идентификатор карточки задания</param>
    /// <param name="cancellationToken">Токен отмены HTTP-запроса</param>
    /// <returns>Задача завершения удаления или ProblemDetails</returns>
    [HttpDelete("jobs/{jobId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteJobAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        try
        {
            await workerClient.DeleteJobAsync(
                CreateJobRequest(jobId),
                cancellationToken: cancellationToken);

            return NoContent();
        }
        catch (RpcException exception)
        {
            return CreateGrpcProblem(exception, "delete job");
        }
    }

    /// <summary>
    /// Потоково передает пакет DLL в Worker и запускает найденные задания
    /// </summary>
    /// <param name="files">Файлы основной сборки задания и ее зависимостей</param>
    /// <param name="cancellationToken">Токен отмены HTTP-запроса</param>
    /// <returns>Задача с описанием пакета, запущенными заданиями и ошибками</returns>
    [HttpPost("job-packages")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<UploadJobPackageResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    [RequestSizeLimit(MaxRequestSize)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestSize)]
    public async Task<ActionResult<UploadJobPackageResponse>> UploadPackageAsync(
        [FromForm] List<IFormFile> files,
        CancellationToken cancellationToken)
    {
        var validationFailure = ValidateFiles(files);
        if (validationFailure is not null)
        {
            return Problem(
                statusCode: validationFailure.StatusCode,
                title: validationFailure.Title,
                detail: validationFailure.Detail);
        }

        try
        {
            using var call = workerClient.UploadPackage(cancellationToken: cancellationToken);
            var operationId = Guid.NewGuid();

            await call.RequestStream.WriteAsync(new UploadPackageRequest
            {
                PackageHeader = new PackageHeader
                {
                    OperationId = operationId.ToString()
                }
            });

            var buffer = ArrayPool<byte>.Shared.Rent(ChunkSize);

            try
            {
                foreach (var file in files)
                {
                    await call.RequestStream.WriteAsync(new UploadPackageRequest
                    {
                        FileHeader = new FileHeader
                        {
                            FileName = file.FileName,
                            Length = file.Length
                        }
                    });

                    await using var stream = file.OpenReadStream();

                    while (true)
                    {
                        var bytesRead = await stream.ReadAsync(
                            buffer.AsMemory(0, ChunkSize),
                            cancellationToken);
                        if (bytesRead == 0)
                            break;

                        await call.RequestStream.WriteAsync(new UploadPackageRequest
                        {
                            Content = ByteString.CopyFrom(buffer, 0, bytesRead)
                        });
                    }
                }

                await call.RequestStream.CompleteAsync();
                var uploaded = await call.ResponseAsync;

                if (!Guid.TryParse(uploaded.PackageId, out var packageId))
                {
                    logger.LogError(
                        "Worker returned invalid package id {PackageId} for operation {OperationId}",
                        uploaded.PackageId,
                        operationId);
                    return Problem(
                        statusCode: StatusCodes.Status502BadGateway,
                        title: "Invalid Worker response",
                        detail: "Worker returned an invalid package identifier");
                }

                var response = new UploadJobPackageResponse(
                    packageId,
                    uploaded.TotalBytes,
                    uploaded.Files.Select(file => new UploadedJobPackageFileResponse(
                        file.FileName,
                        file.Length,
                        file.Sha256)).ToArray(),
                    uploaded.StartedJobs.Select(MapJob).ToArray(),
                    uploaded.Errors.ToArray());

                return StatusCode(StatusCodes.Status201Created, response);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RpcException exception) when (
            exception.StatusCode == GrpcStatusCode.Cancelled &&
            cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                "Package upload was cancelled",
                exception,
                cancellationToken);
        }
        catch (RpcException exception)
        {
            return CreateGrpcProblem(exception, "upload job package");
        }
    }

    /// <summary>
    /// Создает gRPC-запрос с идентификатором задания
    /// </summary>
    /// <param name="jobId">Постоянный идентификатор карточки задания</param>
    /// <returns>Запрос к Worker</returns>
    private static JobRequest CreateJobRequest(Guid jobId) => new()
    {
        JobId = jobId.ToString()
    };

    /// <summary>
    /// Преобразует карточку Worker в REST-модель
    /// </summary>
    /// <param name="job">Карточка из gRPC-ответа Worker</param>
    /// <returns>Карточка публичного REST API</returns>
    private static RunningJobResponse MapJob(ProtoJobResponse job)
    {
        if (!Guid.TryParse(job.JobId, out var jobId))
            throw new InvalidOperationException("Worker returned an invalid job identifier");

        Guid? runtimeJobId = null;
        if (!string.IsNullOrWhiteSpace(job.RuntimeJobId))
        {
            if (!Guid.TryParse(job.RuntimeJobId, out var parsedRuntimeJobId))
                throw new InvalidOperationException("Worker returned an invalid runtime job identifier");

            runtimeJobId = parsedRuntimeJobId;
        }

        return new RunningJobResponse(
            jobId,
            runtimeJobId,
            job.JobType,
            job.AssemblyName,
            job.StartedAt.ToDateTimeOffset(),
            MapStatus(job.Status),
            job.ProcessedCount,
            job.LastProcessedAt is not null ? job.LastProcessedAt.ToDateTimeOffset() : null,
            EmptyToNull(job.LastProcessedMessage),
            job.States.Select(state => new JobStateResponseDto(
                state.Name,
                state.ItemType,
                state.ItemCount,
                state.Items.ToArray())).ToArray(),
            EmptyToNull(job.ErrorMessage));
    }

    /// <summary>
    /// Преобразует gRPC-состояние задания в REST-перечисление
    /// </summary>
    /// <param name="status">Состояние задания Worker</param>
    /// <returns>Соответствующее состояние REST API</returns>
    private static ManagedJobStateResponse MapStatus(JobStatus status) => status switch
    {
        JobStatus.Starting => ManagedJobStateResponse.Starting,
        JobStatus.Running => ManagedJobStateResponse.Running,
        JobStatus.Cancelling => ManagedJobStateResponse.Cancelling,
        JobStatus.Stopped => ManagedJobStateResponse.Stopped,
        JobStatus.Completed => ManagedJobStateResponse.Completed,
        JobStatus.Failed => ManagedJobStateResponse.Failed,
        _ => ManagedJobStateResponse.Failed
    };

    /// <summary>
    /// Преобразует пустую строку контракта в null
    /// </summary>
    /// <param name="value">Строка из gRPC-контракта</param>
    /// <returns>Исходная строка или null для пустого значения</returns>
    private static string? EmptyToNull(string value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>
    /// Проверяет состав, имена и суммарный размер файлов пакета
    /// </summary>
    /// <param name="files">Проверяемые файлы пакета</param>
    /// <returns>Первое найденное нарушение или null для корректного пакета</returns>
    private static PackageValidationFailure? ValidateFiles(IReadOnlyCollection<IFormFile> files)
    {
        if (files.Count == 0)
        {
            return new PackageValidationFailure(
                StatusCodes.Status400BadRequest,
                "Empty job package",
                "Select at least one DLL");
        }

        if (files.Count > MaxFileCount)
        {
            return new PackageValidationFailure(
                StatusCodes.Status400BadRequest,
                "Too many files",
                $"A package can contain at most {MaxFileCount} files");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;

        foreach (var file in files)
        {
            if (string.IsNullOrWhiteSpace(file.FileName) ||
                file.FileName.IndexOfAny(['/', '\\']) >= 0 ||
                file.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                !string.Equals(file.FileName, Path.GetFileName(file.FileName), StringComparison.Ordinal))
            {
                return new PackageValidationFailure(
                    StatusCodes.Status400BadRequest,
                    "Invalid file name",
                    "Package file names cannot contain path elements");
            }

            if (!string.Equals(Path.GetExtension(file.FileName), ".dll", StringComparison.OrdinalIgnoreCase))
            {
                return new PackageValidationFailure(
                    StatusCodes.Status400BadRequest,
                    "Invalid file type",
                    $"File '{file.FileName}' is not a DLL");
            }

            if (file.Length <= 0)
            {
                return new PackageValidationFailure(
                    StatusCodes.Status400BadRequest,
                    "Empty file",
                    $"File '{file.FileName}' is empty");
            }

            if (!names.Add(file.FileName))
            {
                return new PackageValidationFailure(
                    StatusCodes.Status400BadRequest,
                    "Duplicate file",
                    $"File '{file.FileName}' occurs more than once");
            }

            if (file.Length > MaxPackageSize - totalBytes)
            {
                return new PackageValidationFailure(
                    StatusCodes.Status413PayloadTooLarge,
                    "Job package is too large",
                    "The total package size cannot exceed 100 MB");
            }

            totalBytes += file.Length;
        }

        return null;
    }

    /// <summary>
    /// Преобразует ошибку Worker в HTTP ProblemDetails
    /// </summary>
    /// <param name="exception">Ошибка gRPC-вызова Worker</param>
    /// <param name="operation">Имя операции для журнала</param>
    /// <returns>HTTP-ответ с соответствующим status code</returns>
    private ObjectResult CreateGrpcProblem(RpcException exception, string operation)
    {
        var statusCode = exception.StatusCode switch
        {
            GrpcStatusCode.InvalidArgument => StatusCodes.Status400BadRequest,
            GrpcStatusCode.NotFound => StatusCodes.Status404NotFound,
            GrpcStatusCode.AlreadyExists or GrpcStatusCode.FailedPrecondition or GrpcStatusCode.Aborted =>
                StatusCodes.Status409Conflict,
            GrpcStatusCode.ResourceExhausted => StatusCodes.Status413PayloadTooLarge,
            GrpcStatusCode.Unavailable => StatusCodes.Status503ServiceUnavailable,
            GrpcStatusCode.DeadlineExceeded => StatusCodes.Status504GatewayTimeout,
            _ => StatusCodes.Status502BadGateway
        };
        var knownFailure = statusCode is StatusCodes.Status400BadRequest
            or StatusCodes.Status404NotFound
            or StatusCodes.Status409Conflict
            or StatusCodes.Status413PayloadTooLarge;

        if (knownFailure)
        {
            logger.LogWarning(
                "Worker rejected operation {Operation} with status {StatusCode}: {Detail}",
                operation,
                exception.StatusCode,
                exception.Status.Detail);
        }
        else
        {
            logger.LogError(
                exception,
                "Worker operation {Operation} failed with status {StatusCode}",
                operation,
                exception.StatusCode);
        }

        return Problem(
            statusCode: statusCode,
            title: knownFailure ? "Worker rejected the operation" : "Worker is unavailable",
            detail: knownFailure
                ? exception.Status.Detail
                : "The operation could not be completed by Worker");
    }
}

/// <summary>
/// Ответ успешной загрузки и запуска пакета задания
/// </summary>
/// <param name="PackageId">Идентификатор опубликованного пакета</param>
/// <param name="TotalBytes">Суммарный размер файлов в байтах</param>
/// <param name="Files">Опубликованные файлы</param>
/// <param name="StartedJobs">Карточки успешно запущенных заданий</param>
/// <param name="Errors">Ошибки загрузки типов и запуска заданий</param>
public sealed record UploadJobPackageResponse(
    Guid PackageId,
    long TotalBytes,
    IReadOnlyList<UploadedJobPackageFileResponse> Files,
    IReadOnlyList<RunningJobResponse> StartedJobs,
    IReadOnlyList<string> Errors);

/// <summary>
/// Опубликованный файл пакета задания
/// </summary>
/// <param name="FileName">Имя файла</param>
/// <param name="Length">Размер файла в байтах</param>
/// <param name="Sha256">Контрольная сумма SHA-256</param>
public sealed record UploadedJobPackageFileResponse(
    string FileName,
    long Length,
    string Sha256);

/// <summary>
/// Публичная карточка задания REST API
/// </summary>
/// <param name="JobId">Постоянный идентификатор карточки</param>
/// <param name="RuntimeJobId">Идентификатор текущей попытки или null</param>
/// <param name="JobType">Полное имя типа задания</param>
/// <param name="AssemblyName">Имя сборки задания</param>
/// <param name="StartedAt">Время запуска последней попытки</param>
/// <param name="Status">Состояние карточки</param>
/// <param name="ProcessedCount">Количество обработанных сообщений</param>
/// <param name="LastProcessedAt">Время последнего сообщения или null</param>
/// <param name="LastProcessedMessage">Последнее диагностическое сообщение или null</param>
/// <param name="States">Снимки внутренних состояний</param>
/// <param name="ErrorMessage">Сообщение ошибки или null</param>
public sealed record RunningJobResponse(
    Guid JobId,
    Guid? RuntimeJobId,
    string JobType,
    string AssemblyName,
    DateTimeOffset StartedAt,
    ManagedJobStateResponse Status,
    long ProcessedCount,
    DateTimeOffset? LastProcessedAt,
    string? LastProcessedMessage,
    IReadOnlyList<JobStateResponseDto> States,
    string? ErrorMessage);

/// <summary>
/// Снимок внутреннего состояния задания REST API
/// </summary>
/// <param name="Name">Имя состояния</param>
/// <param name="ItemType">Тип элементов</param>
/// <param name="ItemCount">Полное количество элементов</param>
/// <param name="Items">Сериализованные элементы предпросмотра</param>
public sealed record JobStateResponseDto(
    string Name,
    string ItemType,
    int ItemCount,
    IReadOnlyList<string> Items);

/// <summary>
/// Состояние пользовательской карточки задания REST API
/// </summary>
public enum ManagedJobStateResponse
{
    Starting,
    Running,
    Cancelling,
    Stopped,
    Completed,
    Failed
}

/// <summary>
/// Ошибка проверки входного пакета
/// </summary>
/// <param name="StatusCode">HTTP status code</param>
/// <param name="Title">Краткое описание ошибки</param>
/// <param name="Detail">Подробное описание ошибки</param>
internal sealed record PackageValidationFailure(
    int StatusCode,
    string Title,
    string Detail);
