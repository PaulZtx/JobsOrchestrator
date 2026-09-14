using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.Forms;

namespace JobsOperator.Web.Services;

/// <summary>
/// Предоставляет веб-интерфейсу типизированный доступ к Jobs REST API
/// </summary>
/// <param name="httpClient">Настроенный HTTP-клиент Jobs API</param>
public sealed class JobsApiClient(HttpClient httpClient)
{
    private const int MaxFileCount = 20;
    private const long MaxPackageSize = 100 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    /// <summary>
    /// Возвращает актуальные карточки заданий
    /// </summary>
    /// <param name="cancellationToken">Токен отмены HTTP-запроса</param>
    /// <returns>Задача со списком карточек заданий</returns>
    public async Task<IReadOnlyList<RunningJobInfo>> GetJobsAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/jobs");
        return await SendAsync<RunningJobInfo[]>(request, cancellationToken);
    }

    /// <summary>
    /// Загружает выбранные DLL в API и запускает найденные задания
    /// </summary>
    /// <param name="files">Выбранные в браузере файлы сборок</param>
    /// <param name="cancellationToken">Токен отмены HTTP-запроса</param>
    /// <returns>Задача с запущенными заданиями и ошибками отдельных типов</returns>
    public async Task<StartJobsResult> UploadAndStartAsync(
        IReadOnlyCollection<IBrowserFile> files,
        CancellationToken cancellationToken = default)
    {
        var validationError = Validate(files);
        if (validationError is not null)
            return StartJobsResult.Failed(validationError);

        using var multipart = new MultipartFormDataContent();

        foreach (var file in files)
        {
            var stream = file.OpenReadStream(MaxPackageSize, cancellationToken);
            var content = new StreamContent(stream);
            content.Headers.ContentLength = file.Size;
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                "application/octet-stream");
            multipart.Add(content, "files", Path.GetFileName(file.Name));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/job-packages")
        {
            Content = multipart
        };
        var response = await SendAsync<UploadJobPackageResponse>(request, cancellationToken);

        return new StartJobsResult(response.StartedJobs, response.Errors);
    }

    /// <summary>
    /// Запускает новую попытку выполнения задания
    /// </summary>
    /// <param name="jobId">Постоянный идентификатор карточки задания</param>
    /// <param name="cancellationToken">Токен отмены HTTP-запроса</param>
    /// <returns>Задача с актуальной карточкой задания</returns>
    public async Task<RunningJobInfo> StartJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"api/v1/jobs/{jobId}/start");
        return await SendAsync<RunningJobInfo>(request, cancellationToken);
    }

    /// <summary>
    /// Останавливает выполняющееся задание
    /// </summary>
    /// <param name="jobId">Постоянный идентификатор карточки задания</param>
    /// <param name="cancellationToken">Токен отмены HTTP-запроса</param>
    /// <returns>Задача с остановленной карточкой задания</returns>
    public async Task<RunningJobInfo> StopJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"api/v1/jobs/{jobId}/stop");
        return await SendAsync<RunningJobInfo>(request, cancellationToken);
    }

    /// <summary>
    /// Удаляет карточку задания и останавливает текущую попытку при необходимости
    /// </summary>
    /// <param name="jobId">Постоянный идентификатор карточки задания</param>
    /// <param name="cancellationToken">Токен отмены HTTP-запроса</param>
    /// <returns>Задача завершения удаления</returns>
    public async Task DeleteJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"api/v1/jobs/{jobId}");
        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw await CreateApiExceptionAsync(response, cancellationToken);
    }

    /// <summary>
    /// Выполняет HTTP-запрос и читает обязательное JSON-тело успешного ответа
    /// </summary>
    /// <typeparam name="TResponse">Тип модели успешного ответа</typeparam>
    /// <param name="request">Подготовленный HTTP-запрос</param>
    /// <param name="cancellationToken">Токен отмены операции</param>
    /// <returns>Задача с десериализованной моделью ответа</returns>
    private async Task<TResponse> SendAsync<TResponse>(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await CreateApiExceptionAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken)
            ?? throw new JobsApiException("API вернул пустой ответ");
    }

    /// <summary>
    /// Создает пользовательскую ошибку из HTTP ProblemDetails
    /// </summary>
    /// <param name="response">Неуспешный HTTP-ответ</param>
    /// <param name="cancellationToken">Токен отмены чтения ответа</param>
    /// <returns>Задача с ошибкой, содержащей доступное описание причины</returns>
    private static async Task<JobsApiException> CreateApiExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        ApiProblemDetails? problem = null;

        try
        {
            problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>(
                JsonOptions,
                cancellationToken);
        }
        catch (JsonException)
        {
            // Ответ инфраструктуры может не соответствовать ProblemDetails
        }

        var message = problem?.Detail ?? problem?.Title;
        if (string.IsNullOrWhiteSpace(message))
            message = $"Jobs API вернул HTTP {(int)response.StatusCode}";

        return new JobsApiException(message);
    }

    /// <summary>
    /// Проверяет выбранные файлы до отправки в API
    /// </summary>
    /// <param name="files">Выбранные в браузере файлы сборок</param>
    /// <returns>Описание первого нарушения или null</returns>
    private static string? Validate(IReadOnlyCollection<IBrowserFile> files)
    {
        if (files.Count == 0)
            return "Выберите хотя бы одну DLL";

        if (files.Count > MaxFileCount)
            return $"Можно загрузить не больше {MaxFileCount} файлов";

        if (files.Any(file =>
                !string.Equals(Path.GetExtension(file.Name), ".dll", StringComparison.OrdinalIgnoreCase)))
        {
            return "Поддерживаются только файлы DLL";
        }

        long totalSize = 0;
        foreach (var file in files)
        {
            if (file.Size > MaxPackageSize - totalSize)
                return "Общий размер файлов не должен превышать 100 МБ";

            totalSize += file.Size;
        }

        var duplicateName = files
            .GroupBy(file => Path.GetFileName(file.Name), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)?.Key;

        return duplicateName is null
            ? null
            : $"Файл с именем {duplicateName} выбран несколько раз";
    }

    /// <summary>
    /// Создает настройки JSON с поддержкой строковых перечислений REST API
    /// </summary>
    /// <returns>Настройки сериализации ответов API</returns>
    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

/// <summary>
/// Ошибка выполнения команды Jobs REST API
/// </summary>
/// <param name="message">Пользовательское описание ошибки</param>
public sealed class JobsApiException(string message) : Exception(message);

/// <summary>
/// Минимальная модель HTTP ProblemDetails
/// </summary>
/// <param name="Title">Краткое описание ошибки</param>
/// <param name="Detail">Подробное описание ошибки</param>
internal sealed record ApiProblemDetails(string? Title, string? Detail);

/// <summary>
/// Ответ API после загрузки пакета
/// </summary>
/// <param name="PackageId">Идентификатор опубликованного пакета</param>
/// <param name="TotalBytes">Суммарный размер файлов</param>
/// <param name="Files">Опубликованные файлы</param>
/// <param name="StartedJobs">Карточки запущенных заданий</param>
/// <param name="Errors">Ошибки загрузки или запуска отдельных типов</param>
internal sealed record UploadJobPackageResponse(
    Guid PackageId,
    long TotalBytes,
    IReadOnlyList<UploadedJobPackageFile> Files,
    IReadOnlyList<RunningJobInfo> StartedJobs,
    IReadOnlyList<string> Errors);

/// <summary>
/// Файл опубликованного пакета
/// </summary>
/// <param name="FileName">Имя файла</param>
/// <param name="Length">Размер файла</param>
/// <param name="Sha256">Контрольная сумма SHA-256</param>
internal sealed record UploadedJobPackageFile(
    string FileName,
    long Length,
    string Sha256);

/// <summary>
/// Состояние управляемого задания в веб-интерфейсе
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
/// Сведения о загруженном задании и его последней попытке выполнения
/// </summary>
/// <param name="JobId">Постоянный идентификатор карточки задания</param>
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
public sealed record RunningJobInfo(
    Guid JobId,
    Guid? RuntimeJobId,
    string JobType,
    string AssemblyName,
    DateTimeOffset StartedAt,
    ManagedJobState Status,
    long ProcessedCount,
    DateTimeOffset? LastProcessedAt,
    string? LastProcessedMessage,
    IReadOnlyList<JobStateInfo> States,
    string? ErrorMessage);

/// <summary>
/// Диагностический снимок внутреннего состояния задания
/// </summary>
/// <param name="Name">Имя состояния</param>
/// <param name="ItemType">Тип элементов</param>
/// <param name="ItemCount">Полное количество элементов</param>
/// <param name="Items">Сериализованные элементы предпросмотра</param>
public sealed record JobStateInfo(
    string Name,
    string ItemType,
    int ItemCount,
    IReadOnlyList<string> Items);

/// <summary>
/// Результат запуска заданий из загруженного пакета
/// </summary>
/// <param name="StartedJobs">Карточки успешно запущенных заданий</param>
/// <param name="Errors">Сообщения об ошибках загрузки и запуска</param>
public sealed record StartJobsResult(
    IReadOnlyList<RunningJobInfo> StartedJobs,
    IReadOnlyList<string> Errors)
{
    /// <summary>
    /// Создает результат неудачной локальной проверки
    /// </summary>
    /// <param name="error">Описание причины неудачного запуска</param>
    /// <returns>Результат без запущенных заданий с указанной ошибкой</returns>
    public static StartJobsResult Failed(string error) => new([], [error]);
}
