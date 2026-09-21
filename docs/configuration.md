# Конфигурация

## Источники конфигурации ASP.NET Core

`JobsOperator.Worker`, `JobsOperator.Api` и `JobsOperator.Web` используют `WebApplication.CreateBuilder(args)`. Стандартные providers включают:

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. переменные окружения
4. аргументы командной строки

Более поздний provider переопределяет более ранний. Для вложенных ключей в переменных окружения используйте двойное подчеркивание, например `Worker__Address`.

Секреты в текущих настройках отсутствуют. При добавлении секретов не сохраняйте их в repository appsettings; используйте Secret Manager в development и внешний secret store в deployed среде.

## JobsOperator.Api

`JobsOperator.Api/appsettings.json`:

| Ключ | Значение по умолчанию | Назначение |
|---|---|---|
| `Worker:Address` | `http://localhost:5093` | Base address gRPC Worker |
| `Logging:LogLevel:Default` | `Information` | Общий уровень журнала |
| `Logging:LogLevel:Microsoft.AspNetCore` | `Warning` | Уровень ASP.NET Core |
| `AllowedHosts` | `*` | Host filtering |

Пример override:

```shell
Worker__Address=http://worker:5093 dotnet run --project JobsOperator.Api
```

`Worker:Address` обязателен и должен быть абсолютным URI. Для plaintext gRPC endpoint нужен HTTP/2-capable Worker.

## JobsOperator.Worker

| Ключ | Значение по умолчанию | Назначение |
|---|---|---|
| `JobPackages:Path` | `App_Data/packages` | Каталог опубликованных пакетов |
| `Kestrel:EndpointDefaults:Protocols` | `Http2` | Протокол gRPC endpoints |
| `Logging:LogLevel:Default` | `Information` | Общий уровень журнала |
| `Logging:LogLevel:Microsoft.AspNetCore` | `Warning` | Уровень ASP.NET Core |
| `AllowedHosts` | `*` | Host filtering |

Относительный `JobPackages:Path` разрешается относительно `IHostEnvironment.ContentRootPath`, затем нормализуется в абсолютный путь.

Пример:

```shell
JobPackages__Path=/var/lib/jobsoperator/packages \
ASPNETCORE_URLS=http://0.0.0.0:5093 \
dotnet run --project JobsOperator.Worker
```

Не меняйте endpoint Worker на HTTP/1.1: gRPC требует HTTP/2.

## JobsOperator.Web

| Ключ | Значение по умолчанию | Назначение |
|---|---|---|
| `JobsApi:BaseAddress` | `http://localhost:5244` | Base address REST API |
| `Logging:LogLevel:Default` | `Information` | Общий уровень журнала |
| `Logging:LogLevel:Microsoft.AspNetCore` | `Warning` | Уровень ASP.NET Core |
| `AllowedHosts` | `*` | Host filtering |

`JobsApi:BaseAddress` обязателен. Typed `HttpClient` имеет timeout 10 минут, чтобы крупный пакет успел загрузиться.

```shell
JobsApi__BaseAddress=http://api:5244 dotnet run --project JobsOperator.Web
```

Web использует server-side HTTP client, поэтому адрес API должен быть доступен от процесса Web, а не обязательно непосредственно из браузера.

## Launch profiles

| Проект | HTTP | HTTPS + HTTP |
|---|---|---|
| Worker | `http://localhost:5093` | `https://localhost:7203;http://localhost:5093` |
| API | `http://localhost:5244` | `https://localhost:7083;http://localhost:5244` |
| Web | `http://localhost:5088` | `https://localhost:7220;http://localhost:5088` |

Профили задают `ASPNETCORE_ENVIRONMENT=Development`. Они применяются при `dotnet run --launch-profile <name>`, но не являются production-конфигурацией.

## Console JobsOperator и KafkaRelayJob

Console host читает переменные напрямую через `Environment.GetEnvironmentVariable`:

| Переменная | Default | Назначение |
|---|---|---|
| `JOB_MODE` | `sample` | `kafka-relay` выбирает `KafkaRelayJob`; любое другое значение запускает `SampleJob` |
| `KAFKA_BOOTSTRAP_SERVERS` | `localhost:9092,localhost:9093,localhost:9094` | Kafka bootstrap list |
| `KAFKA_INPUT_TOPIC` | `jobs-input` | Входная тема relay |
| `KAFKA_OUTPUT_TOPIC` | `jobs-output` | Выходная тема relay |
| `KAFKA_CHECKPOINT_PATH` | `<base>/checkpoints/kafka-relay.json` | Стабильный checkpoint relay |

Пустое или состоящее из пробелов значение Kafka-переменной заменяется default.

Пример локального запуска relay без Docker host-контейнера:

```shell
JOB_MODE=kafka-relay \
KAFKA_BOOTSTRAP_SERVERS=localhost:9092,localhost:9093,localhost:9094 \
dotnet run --project JobsOperator
```

## Checkpoint options задания

Checkpoint настраивается кодом задания, а не appsettings Worker:

```csharp
builder.ConfigureCheckpoints(new CheckpointOptions
{
    Enabled = true,
    DelayMillisecond = 1_000,
    PathToCheckpoint = "/var/lib/jobsoperator/checkpoints/my-job.json",
    RestoreMode = CheckpointRestoreMode.ResumeOrCreate
});
```

В deployed среде используйте устойчивый абсолютный путь на persistent volume. У процесса должны быть права создания родительского каталога, временного файла и замены целевого файла.

## Docker Compose variables

Compose поддерживает подстановку из shell или `.env`:

| Переменная | Default | Используется |
|---|---|---|
| `KAFKA_INPUT_TOPIC` | `jobs-input` | topic init и relay |
| `KAFKA_OUTPUT_TOPIC` | `jobs-output` | topic init и relay |
| `KAFKA_TOPIC` | `jobs-load-test` | load-test topic |
| `KAFKA_PARTITIONS` | `12` | partition count load-test topic |
| `KAFKA_NUM_RECORDS` | `100000` | число сообщений perf producer |
| `KAFKA_RECORD_SIZE` | `1024` | размер сообщения в байтах |
| `KAFKA_THROUGHPUT` | `-1` | лимит сообщений/с; `-1` без лимита |

`.env` исключен из Git. Не помещайте туда данные, которые должны храниться в source control.

## Production checklist конфигурации

- используйте TLS между клиентом, API и Worker
- задайте постоянные каталоги пакетов и checkpoints
- не используйте `AllowedHosts: *` без осознанной сетевой границы
- добавьте authentication/authorization перед публикацией API
- ограничьте права и ресурсы процесса Worker
- настройте централизованные logs, health checks и наблюдаемость
- валидируйте конфигурацию при старте, прежде чем принимать трафик

См. также [Эксплуатация и ограничения](operations-and-limitations.md).
