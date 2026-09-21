using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using JobsOperator.Protos;

namespace JobsOperator.Worker.Services;

/// <summary>
/// Принимает управляющие gRPC-запросы и потоковые пакеты заданий
/// </summary>
/// <param name="jobManager">Сервис пользовательского жизненного цикла заданий</param>
/// <param name="packageStorage">Хранилище загружаемых пакетов</param>
/// <param name="logger">Журнал непредвиденных ошибок обработки запросов</param>
internal sealed class JobService(
    WorkerJobManager jobManager,
    JobPackageStorage packageStorage,
    ILogger<JobService> logger) : JobsOperator.Protos.JobService.JobServiceBase
{
    /// <inheritdoc />
    public override Task<JobResponse> GetJob(JobRequest request, ServerCallContext context)
    {
        var jobId = ParseJobId(request.JobId);
        var job = jobManager.GetJob(jobId)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Job was not found"));

        return Task.FromResult(MapJob(job));
    }

    /// <inheritdoc />
    public override Task<JobsResponse> GetJobs(Empty request, ServerCallContext context)
    {
        var response = new JobsResponse();
        response.Responses.AddRange(jobManager.GetJobs().Select(MapJob));
        return Task.FromResult(response);
    }

    /// <inheritdoc />
    public override async Task<JobResponse> StartJob(JobRequest request, ServerCallContext context)
    {
        var result = await jobManager.StartAsync(
            ParseJobId(request.JobId),
            context.CancellationToken);

        return MapOperationResult(result);
    }

    /// <inheritdoc />
    public override async Task<JobResponse> StopJob(JobRequest request, ServerCallContext context)
    {
        var result = await jobManager.StopAsync(
            ParseJobId(request.JobId),
            context.CancellationToken);

        return MapOperationResult(result);
    }

    /// <inheritdoc />
    public override async Task<Empty> DeleteJob(JobRequest request, ServerCallContext context)
    {
        var result = await jobManager.DeleteAsync(
            ParseJobId(request.JobId),
            context.CancellationToken);

        if (result.Status is not JobOperationStatus.Success)
            throw CreateOperationException(result);

        return new Empty();
    }

    /// <inheritdoc />
    public override async Task<UploadPackageResponse> UploadPackage(
        IAsyncStreamReader<UploadPackageRequest> requestStream,
        ServerCallContext context)
    {
        JobPackageUpload? upload = null;

        try
        {
            await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
            {
                switch (request.FrameCase)
                {
                    case UploadPackageRequest.FrameOneofCase.PackageHeader:
                    {
                        if (upload is not null)
                            throw InvalidPackage("Package header occurs more than once");

                        if (!Guid.TryParse(request.PackageHeader.OperationId, out var packageId) ||
                            packageId == Guid.Empty)
                        {
                            throw InvalidPackage("Operation id is invalid");
                        }

                        upload = packageStorage.BeginUpload(packageId);
                        break;
                    }
                    case UploadPackageRequest.FrameOneofCase.FileHeader:
                    {
                        var activeUpload = upload
                            ?? throw InvalidPackage("File header was received before package header");

                        await activeUpload.StartFileAsync(
                            request.FileHeader.FileName,
                            request.FileHeader.Length,
                            context.CancellationToken);
                        break;
                    }
                    case UploadPackageRequest.FrameOneofCase.Content:
                    {
                        var activeUpload = upload
                            ?? throw InvalidPackage("File content was received before package header");

                        await activeUpload.WriteAsync(
                            request.Content.Memory,
                            context.CancellationToken);
                        break;
                    }
                    case UploadPackageRequest.FrameOneofCase.None:
                        throw InvalidPackage("Upload frame is empty");
                    default:
                        throw InvalidPackage("Upload frame type is unknown");
                }
            }

            if (upload is null)
                throw InvalidPackage("Package header was not received");

            var package = await upload.CompleteAsync(context.CancellationToken);
            var startResult = jobManager.StartPackage(
                package,
                packageStorage.GetPackageDirectory(package.PackageId));
            var response = new UploadPackageResponse
            {
                PackageId = package.PackageId.ToString(),
                TotalBytes = package.TotalBytes
            };

            response.Files.AddRange(package.Files.Select(file => new UploadedPackageFile
            {
                FileName = file.FileName,
                Length = file.Length,
                Sha256 = file.Sha256
            }));
            response.StartedJobs.AddRange(startResult.StartedJobs.Select(MapJob));
            response.Errors.AddRange(startResult.Errors);

            return response;
        }
        catch (JobPackageUploadException exception)
        {
            throw new RpcException(
                new Status(MapStatusCode(exception.Error), exception.Message));
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not upload and start job package");
            throw new RpcException(
                new Status(StatusCode.Internal, "Could not upload and start job package"));
        }
        finally
        {
            if (upload is not null)
                await upload.DisposeAsync();
        }
    }

    /// <summary>
    /// Проверяет и преобразует строковый идентификатор задания
    /// </summary>
    /// <param name="value">Строковое представление идентификатора</param>
    /// <returns>Проверенный идентификатор задания</returns>
    private static Guid ParseJobId(string value)
    {
        if (!Guid.TryParse(value, out var jobId) || jobId == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Job id is invalid"));

        return jobId;
    }

    /// <summary>
    /// Преобразует результат команды в gRPC-модель задания
    /// </summary>
    /// <param name="result">Результат команды Worker</param>
    /// <returns>Актуальная карточка задания</returns>
    private static JobResponse MapOperationResult(JobOperationResult result)
    {
        if (result.Status is not JobOperationStatus.Success || result.Job is null)
            throw CreateOperationException(result);

        return MapJob(result.Job);
    }

    /// <summary>
    /// Создает gRPC-ошибку для неуспешного результата команды
    /// </summary>
    /// <param name="result">Неуспешный результат команды Worker</param>
    /// <returns>Ошибка с подходящим gRPC status code</returns>
    private static RpcException CreateOperationException(JobOperationResult result)
    {
        var statusCode = result.Status switch
        {
            JobOperationStatus.NotFound => StatusCode.NotFound,
            JobOperationStatus.Conflict => StatusCode.FailedPrecondition,
            _ => StatusCode.Internal
        };

        return new RpcException(new Status(
            statusCode,
            result.ErrorMessage ?? "Job operation failed"));
    }

    /// <summary>
    /// Преобразует внутреннюю карточку задания в публичный gRPC-контракт
    /// </summary>
    /// <param name="job">Карточка задания Worker</param>
    /// <returns>Публичное представление задания</returns>
    private static JobResponse MapJob(ManagedJobInfo job)
    {
        var response = new JobResponse
        {
            JobId = job.JobId.ToString(),
            RuntimeJobId = job.RuntimeJobId?.ToString() ?? string.Empty,
            JobType = job.JobType,
            AssemblyName = job.AssemblyName,
            StartedAt = Timestamp.FromDateTimeOffset(job.StartedAt),
            Status = MapStatus(job.Status),
            ProcessedCount = job.ProcessedCount,
            LastProcessedMessage = job.LastProcessedMessage ?? string.Empty,
            ErrorMessage = job.ErrorMessage ?? string.Empty
        };

        if (job.LastProcessedAt is not null)
            response.LastProcessedAt = Timestamp.FromDateTimeOffset(job.LastProcessedAt.Value);

        response.States.AddRange(job.States.Select(state =>
        {
            var mapped = new JobStateResponse
            {
                Name = state.Name,
                ItemType = state.ItemType,
                ItemCount = state.ItemCount
            };
            mapped.Items.AddRange(state.Items);
            return mapped;
        }));

        return response;
    }

    /// <summary>
    /// Преобразует внутреннее состояние карточки в gRPC-перечисление
    /// </summary>
    /// <param name="state">Состояние карточки Worker</param>
    /// <returns>Соответствующее значение публичного контракта</returns>
    private static JobStatus MapStatus(ManagedJobState state) => state switch
    {
        ManagedJobState.Starting => JobStatus.Starting,
        ManagedJobState.Running => JobStatus.Running,
        ManagedJobState.Cancelling => JobStatus.Cancelling,
        ManagedJobState.Stopped => JobStatus.Stopped,
        ManagedJobState.Completed => JobStatus.Completed,
        ManagedJobState.Failed => JobStatus.Failed,
        _ => JobStatus.Unspecified
    };

    /// <summary>
    /// Создает ошибку некорректного содержимого пакета
    /// </summary>
    /// <param name="message">Описание нарушения протокола загрузки</param>
    /// <returns>Ошибка загрузки с категорией некорректного пакета</returns>
    private static JobPackageUploadException InvalidPackage(string message)
    {
        return new JobPackageUploadException(JobPackageUploadError.InvalidPackage, message);
    }

    /// <summary>
    /// Преобразует категорию ошибки загрузки в gRPC status code
    /// </summary>
    /// <param name="error">Категория ошибки загрузки</param>
    /// <returns>Соответствующий gRPC status code</returns>
    private static StatusCode MapStatusCode(JobPackageUploadError error) => error switch
    {
        JobPackageUploadError.InvalidPackage => StatusCode.InvalidArgument,
        JobPackageUploadError.LimitExceeded => StatusCode.ResourceExhausted,
        JobPackageUploadError.AlreadyExists => StatusCode.AlreadyExists,
        _ => StatusCode.Internal
    };
}
