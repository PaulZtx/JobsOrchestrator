# REST API

## Общие сведения

Базовый локальный адрес HTTP-профиля:

```text
http://localhost:5244
```

Все endpoints имеют префикс `/api/v1`. Успешные ответы используют JSON в camelCase. Значения status enum сериализуются строками.

В Development:

- OpenAPI: `GET /openapi/v1.json`
- Swagger UI: `/swagger`

API не выполняет задания и не хранит карточки: он синхронно обращается к gRPC Worker для каждого запроса.

## Сводка endpoints

| Метод | Путь | Успех | Назначение |
|---|---|---|---|
| `GET` | `/api/v1/jobs` | 200 | Все карточки |
| `GET` | `/api/v1/jobs/{jobId}` | 200 | Одна карточка |
| `POST` | `/api/v1/job-packages` | 201 | Загрузить DLL и запустить найденные задания |
| `POST` | `/api/v1/jobs/{jobId}/start` | 200 | Создать новую попытку |
| `POST` | `/api/v1/jobs/{jobId}/stop` | 200 | Остановить текущую попытку |
| `DELETE` | `/api/v1/jobs/{jobId}` | 204 | Остановить и удалить карточку |

## Модель задания

```json
{
  "jobId": "8c12ed9a-33fb-4bd6-a3f4-2f7462321614",
  "runtimeJobId": "8c12ed9a-33fb-4bd6-a3f4-2f7462321614",
  "jobType": "Example.FileJob",
  "assemblyName": "Example.Jobs",
  "startedAt": "2026-09-19T10:00:00+00:00",
  "status": "Running",
  "processedCount": 42,
  "lastProcessedAt": "2026-09-19T10:00:12+00:00",
  "lastProcessedMessage": "{\"value\":\"example\"}",
  "states": [
    {
      "name": "received",
      "itemType": "Example.InputMessage",
      "itemCount": 42,
      "items": [
        "{\"value\":\"example\"}"
      ]
    }
  ],
  "errorMessage": null
}
```

| Поле | Тип | Описание |
|---|---|---|
| `jobId` | UUID | Стабильный идентификатор карточки |
| `runtimeJobId` | UUID или null | Текущая попытка выполнения |
| `jobType` | string | Полное имя CLR-типа |
| `assemblyName` | string | Простое имя сборки |
| `startedAt` | timestamp | Время запуска текущей или последней попытки |
| `status` | enum | Состояние карточки |
| `processedCount` | int64 | Успешно обработанные записи текущей/последней попытки |
| `lastProcessedAt` | timestamp или null | Последняя обработка |
| `lastProcessedMessage` | string или null | Диагностическое представление входной записи |
| `states` | array | Диагностические snapshots состояний |
| `errorMessage` | string или null | Последняя ошибка |

Допустимые `status`: `Starting`, `Running`, `Cancelling`, `Stopped`, `Completed`, `Failed`.

## Получение списка

```http
GET /api/v1/jobs HTTP/1.1
Host: localhost:5244
```

Ответ `200 OK`:

```json
[]
```

или массив моделей задания. Worker сортирует карточки по `startedAt` по убыванию.

```shell
curl http://localhost:5244/api/v1/jobs
```

## Получение карточки

```http
GET /api/v1/jobs/{jobId} HTTP/1.1
```

```shell
curl http://localhost:5244/api/v1/jobs/8c12ed9a-33fb-4bd6-a3f4-2f7462321614
```

Результаты:

- `200` — модель задания
- `404` — карточка отсутствует
- `400` — значение прошло route parsing, но Worker отклонил идентификатор

Маршрут содержит constraint `:guid`; некорректная строка GUID обычно не совпадет с endpoint.

## Загрузка пакета

```http
POST /api/v1/job-packages HTTP/1.1
Content-Type: multipart/form-data
```

Форма должна содержать одно или несколько полей `files`.

```shell
curl \
  -X POST \
  -F "files=@MyJobs/bin/Debug/net10.0/MyJobs.dll" \
  -F "files=@MyJobs/bin/Debug/net10.0/MyDependency.dll" \
  http://localhost:5244/api/v1/job-packages
```

Ограничения:

| Ограничение | Значение |
|---|---|
| Количество | 1–20 файлов |
| Расширение | только `.dll`, без учета регистра |
| Размер отдельного файла | больше 0 |
| Суммарный размер DLL | не более 100 МБ |
| HTTP request limit | 102 МБ с запасом на multipart |
| Имена | без пути, допустимые для файловой системы, уникальные без учета регистра |

API пересылает файл Worker блоками до 64 КБ. Успешная публикация пакета возвращает `201 Created` даже при наличии ошибок загрузки отдельных типов или если `startedJobs` пуст.

Пример ответа:

```json
{
  "packageId": "96eb2aad-8c78-47b0-b764-6fc248c0f4b4",
  "totalBytes": 24576,
  "files": [
    {
      "fileName": "MyJobs.dll",
      "length": 24576,
      "sha256": "6c0f...d3a1"
    }
  ],
  "startedJobs": [],
  "errors": [
    "No IJob implementation was found in the package"
  ]
}
```

Ошибки:

- `400` — пустая форма, недопустимый файл или нарушенный протокол пакета
- `409` — конфликт публикации, например повтор package ID на Worker
- `413` — превышен размер
- `502` — неожиданный ответ или внутренняя ошибка Worker
- `503` — Worker недоступен
- `504` — истек gRPC deadline

## Запуск новой попытки

```http
POST /api/v1/jobs/{jobId}/start HTTP/1.1
```

```shell
curl -X POST http://localhost:5244/api/v1/jobs/8c12ed9a-33fb-4bd6-a3f4-2f7462321614/start
```

Успех `200` возвращает обновленную карточку. Worker создает новый экземпляр того же CLR-типа; `jobId` сохраняется, `runtimeJobId` и `startedAt` меняются, diagnostics сбрасываются.

Ошибки:

- `404` — карточка не найдена
- `409` — карточка уже активна
- `502` — создать новую попытку не удалось

## Остановка

```http
POST /api/v1/jobs/{jobId}/stop HTTP/1.1
```

```shell
curl -X POST http://localhost:5244/api/v1/jobs/8c12ed9a-33fb-4bd6-a3f4-2f7462321614/stop
```

Успех `200` возвращает карточку со статусом `Stopped` и `runtimeJobId: null`. Последний snapshot сохраняется в карточке Worker.

Ошибки:

- `404` — карточка не найдена
- `409` — задание не находится в `Running`
- `502` — graceful stop не удался

## Удаление

```http
DELETE /api/v1/jobs/{jobId} HTTP/1.1
```

```shell
curl -i -X DELETE http://localhost:5244/api/v1/jobs/8c12ed9a-33fb-4bd6-a3f4-2f7462321614
```

Worker останавливает активную попытку и удаляет карточку. Ответ — `204 No Content`. Файлы опубликованного пакета и checkpoint не удаляются.

## Формат ошибок

Ошибки возвращаются как `application/problem+json`:

```json
{
  "type": "about:blank",
  "title": "Worker rejected the operation",
  "status": 409,
  "detail": "Job is already active",
  "traceId": "00-..."
}
```

Соответствие gRPC → HTTP:

| gRPC status | HTTP |
|---|---|
| `InvalidArgument` | 400 |
| `NotFound` | 404 |
| `AlreadyExists`, `FailedPrecondition`, `Aborted` | 409 |
| `ResourceExhausted` | 413 |
| `Unavailable` | 503 |
| `DeadlineExceeded` | 504 |
| остальные | 502 |

Для ожидаемых 4xx detail Worker передается клиенту. Для инфраструктурных ошибок detail скрывается общей формулировкой, а исключение записывается в server log.

## Безопасность API

В текущей версии отсутствуют:

- authentication и authorization
- rate limiting
- CORS policy для внешних browser clients
- TLS enforcement в HTTP-профиле
- антивирусная или signature-проверка DLL

Не выставляйте API в публичную сеть без внешнего защитного слоя.

Следующий уровень контракта: [gRPC API](grpc-api.md).
