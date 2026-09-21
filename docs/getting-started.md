# Начало работы

## Требования

- .NET SDK 10.0
- Docker с Docker Compose для Kafka-сценария
- Bash, zsh, PowerShell или другая оболочка, способная запускать `dotnet` и `docker`

Проверка SDK:

```shell
dotnet --version
```

Решение и все проекты используют `net10.0`, nullable reference types и implicit usings.

## Восстановление и сборка

Из корня репозитория:

```shell
dotnet restore JobsOperator.sln
dotnet build JobsOperator.sln
```

В решение входят семь проектов: `Jobs`, `JobsOperator`, `JobsOperator.Protos`, `JobsOperator.Worker`, `JobsOperator.Api`, `JobsOperator.Web` и `TestBuilds`.

Автоматический тестовый проект сейчас отсутствует. Минимальная проверка изменения — успешная сборка решения и ручной запуск затронутого сценария.

## Консольный пример с JSON Lines

```shell
dotnet run --project JobsOperator
```

Если `JOB_MODE` не задан, `JobsOperator` создает `TestBuilds.SampleJob`. Он:

1. читает `JobsOperator/Test.json` через `JsonFileConnectorSource<T>`
2. добавляет сообщения в состояние `test-state`
3. пишет их в `ConsoleSink<T>`
4. создает checkpoint примерно раз в секунду

Файл имеет формат JSON Lines: один JSON-объект на строку.

```json
{"Name":"Nastya"}
{"Name":"Pasha"}
```

После достижения конца файла источник не завершается, а проверяет появление новых строк каждые 250 мс. Нажмите любую клавишу, чтобы консольный host завершился.

## Полный локальный Operator stack

Запустите процессы в указанном порядке и оставьте каждый работающим в своем терминале.

Worker:

```shell
dotnet run --project JobsOperator.Worker --launch-profile http
```

REST API:

```shell
dotnet run --project JobsOperator.Api --launch-profile http
```

Blazor Web App:

```shell
dotnet watch --project JobsOperator.Web --launch-profile http
```

Адреса HTTP-профилей:

| Компонент | Адрес | Протокол |
|---|---|---|
| Web UI | `http://localhost:5088` | HTTP/1.1 или HTTP/2 |
| REST API | `http://localhost:5244` | HTTP/1.1 или HTTP/2 |
| Worker | `http://localhost:5093` | HTTP/2, gRPC |

Откройте `http://localhost:5088`, выберите DLL задания и DLL-зависимости, затем нажмите **Добавить и запустить**. Для пробы подойдет `TestBuilds/bin/Debug/net10.0/TestBuilds.dll`, созданный сборкой решения.

Один пакет может содержать не более 20 непустых DLL общим размером до 100 МБ. Все конкретные реализации `IJob` с публичным конструктором без параметров будут запущены независимо. Частичная ошибка одного типа не отменяет уже запущенные типы.

## REST API без UI

После запуска Worker и API:

```shell
curl http://localhost:5244/api/v1/jobs
```

Загрузка одной сборки:

```shell
curl \
  -X POST \
  -F "files=@TestBuilds/bin/Debug/net10.0/TestBuilds.dll" \
  http://localhost:5244/api/v1/job-packages
```

В Development окружении OpenAPI JSON доступен по `/openapi/v1.json`, а Swagger UI — по `/swagger`.

## Kafka-сценарий

Полный Kafka-стенд запускается одной командой:

```shell
docker compose up -d --build
```

Она запускает три брокера, создает темы, поднимает Kafka UI и контейнерный `JobsOperator` в режиме `kafka-relay`. Подробности — в [Kafka и Docker](kafka-and-docker.md).

## Остановка

Приложения `dotnet run` остановите `Ctrl+C`.

Kafka-стенд без удаления данных:

```shell
docker compose down
```

Удаление named volumes также удаляет данные Kafka и checkpoint контейнерного задания:

```shell
docker compose down -v
```

## Следующие шаги

- [Архитектура](architecture.md)
- [Разработка заданий](job-development.md)
- [Конфигурация](configuration.md)
- [Эксплуатация и ограничения](operations-and-limitations.md)
