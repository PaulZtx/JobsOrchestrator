# JobsOperator

JobsOperator — прототип платформы на .NET 10 для описания, запуска и наблюдения за потоковыми заданиями. Задание реализует `IJob` и собирает один или несколько типизированных конвейеров:

```text
Source -> Process -> Sink
```

Платформа поддерживает JSON Lines и Kafka, пользовательское состояние, согласованные файловые checkpoints, динамическую загрузку DLL, REST API, отдельный gRPC Worker и Blazor Web App для управления заданиями.

> Проект находится на стадии прототипа. Публичный API, формат checkpoint и эксплуатационные гарантии пока не зафиксированы как стабильные.

## Быстрый старт

Требуется .NET SDK 10.0. Docker нужен только для Kafka-сценария.

```shell
dotnet restore JobsOperator.sln
dotnet build JobsOperator.sln
dotnet run --project JobsOperator
```

Консольный пример читает `JobsOperator/Test.json`, сохраняет сообщения в `IState<T>` и выводит их в консоль. Источник следит за дописываемыми строками, поэтому для завершения нажмите любую клавишу.

Для полного стека запустите в отдельных терминалах:

```shell
dotnet run --project JobsOperator.Worker --launch-profile http
dotnet run --project JobsOperator.Api --launch-profile http
dotnet watch --project JobsOperator.Web --launch-profile http
```

Откройте `http://localhost:5088`. REST API будет доступен на `http://localhost:5244`, Worker — на `http://localhost:5093` по HTTP/2.

## Состав решения

| Проект | Назначение |
|---|---|
| `Jobs` | Ядро: DSL задания, runtime, коннекторы, sinks, состояния, checkpoints и диагностика |
| `JobsOperator` | Консольный host встроенных примеров |
| `JobsOperator.Protos` | Общий protobuf/gRPC-контракт API и Worker |
| `JobsOperator.Worker` | Выполнение заданий, загрузка DLL и оперативный каталог карточек |
| `JobsOperator.Api` | Публичный REST API и gRPC-клиент Worker |
| `JobsOperator.Web` | Интерактивная серверная Blazor-панель |
| `TestBuilds` | Примеры `IJob` для JSON Lines, `Discard` и Kafka relay |

Основной поток управления:

```mermaid
flowchart LR
    Browser[Blazor Web App] -->|HTTP/JSON| API[REST API]
    API -->|gRPC / HTTP2| Worker[gRPC Worker]
    Worker --> Manager[WorkerJobManager]
    Manager --> Orchestrator[JobsOrchestrator]
    Orchestrator --> Runtime[Pipeline runtime]
    Runtime --> Source[Source]
    Source --> Process[Process]
    Process --> Sink[Sink]
```

## Минимальное задание

```csharp
using Jobs.Connectors;
using Jobs.JobsEntities.Interfaces;

public sealed record InputMessage(string Value);

public sealed class FileJob : IJob
{
    public void Configure(IJobBuilder builder)
    {
        builder
            .Source(
                "input-file",
                _ => new JsonFileConnectorSource<InputMessage>("input.jsonl"))
            .Process(
                "observe",
                (message, context, cancellationToken) =>
                {
                    Console.WriteLine(message.Value);
                    return ValueTask.CompletedTask;
                })
            .Discard();
    }
}
```

Проект задания должен ссылаться на `Jobs/Jobs.csproj`, собираться для `net10.0`, а загружаемый тип должен быть конкретной реализацией `IJob` с публичным конструктором без параметров.

## Документация

Полная документация находится в каталоге [`docs`](docs/README.md):

- [Начало работы](docs/getting-started.md) — требования, сборка и способы запуска
- [Архитектура](docs/architecture.md) — компоненты, зависимости и потоки данных
- [Разработка заданий](docs/job-development.md) — DSL, контракты, коннекторы, sinks и состояния
- [Runtime и checkpoints](docs/runtime-and-checkpoints.md) — исполнение, backpressure, восстановление и диагностика
- [Operator stack](docs/operator-stack.md) — Worker, API, Web UI и жизненный цикл карточки
- [REST API](docs/rest-api.md) — endpoints, модели, статусы и примеры запросов
- [gRPC API](docs/grpc-api.md) — сервис, сообщения и протокол потоковой загрузки
- [Конфигурация](docs/configuration.md) — параметры приложений и переменные окружения
- [Kafka и Docker](docs/kafka-and-docker.md) — локальный кластер и relay-сценарий
- [Эксплуатация и ограничения](docs/operations-and-limitations.md) — данные, безопасность, диагностика и roadmap

## Важное о безопасности

Worker загружает DLL в собственный процесс, но не создает sandbox и не ограничивает права пользовательского кода. Аутентификация и авторизация отсутствуют. Загружайте только доверенные сборки и не публикуйте текущую конфигурацию в недоверенной сети.
