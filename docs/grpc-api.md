# gRPC API

## Назначение

`JobsOperator.Protos/Protos/job.proto` — внутренний контракт между REST API и Worker. Проект `JobsOperator.Protos` генерирует client и server types для `net10.0`.

Локальный HTTP-профиль Worker:

```text
http://localhost:5093
```

Kestrel Worker использует HTTP/2. Обычный GET `/` возвращает только справочный текст.

## Сервис `jobs.JobService`

| RPC | Тип | Назначение |
|---|---|---|
| `GetJob(JobRequest)` | unary | Получить одну карточку |
| `GetJobs(Empty)` | unary | Получить все карточки |
| `StartJob(JobRequest)` | unary | Запустить новую попытку |
| `StopJob(JobRequest)` | unary | Остановить попытку |
| `DeleteJob(JobRequest)` | unary | Удалить карточку |
| `UploadPackage(stream UploadPackageRequest)` | client streaming | Передать пакет DLL и запустить задания |

## Основные сообщения

### `JobRequest`

| Поле | Номер | Тип | Описание |
|---|---:|---|---|
| `job_id` | 1 | string | Непустой GUID карточки |

### `JobResponse`

| Поле | Номер | Тип | Описание |
|---|---:|---|---|
| `job_id` | 1 | string | Стабильный GUID карточки |
| `runtime_job_id` | 2 | string | GUID попытки или пустая строка |
| `job_type` | 3 | string | Полное имя типа |
| `assembly_name` | 4 | string | Имя сборки |
| `started_at` | 5 | Timestamp | Время последнего запуска |
| `status` | 6 | JobStatus | Состояние карточки |
| `processed_count` | 7 | int64 | Счетчик обработки |
| `last_processed_at` | 8 | Timestamp | Опциональное время |
| `last_processed_message` | 9 | string | Значение или пустая строка |
| `states` | 10 | repeated JobStateResponse | Диагностика состояний |
| `error_message` | 11 | string | Ошибка или пустая строка |

### `JobStatus`

```text
UNSPECIFIED = 0
STARTING    = 1
RUNNING     = 2
CANCELLING  = 3
STOPPED     = 4
COMPLETED   = 5
FAILED      = 6
```

REST API преобразует неизвестный gRPC status в `Failed`.

## Протокол `UploadPackage`

`UploadPackageRequest` содержит `oneof frame`:

- `package_header`
- `file_header`
- `content`

Допустимая последовательность:

```text
PackageHeader
FileHeader
Content*
FileHeader
Content*
...
end of stream
```

### Package header

```protobuf
message PackageHeader {
  string operation_id = 1;
}
```

`operation_id` должен быть непустым GUID. Он становится `package_id` и именем каталога пакета. Заголовок должен быть первым и встречаться ровно один раз.

### File header

```protobuf
message FileHeader {
  string file_name = 1;
  int64 length = 2;
}
```

Новый file header неявно завершает предыдущий файл. Worker проверяет, что фактически принято ровно `length` байтов.

### Content

`content` — произвольный bytes block текущего файла. API использует блоки до 64 КБ. Пустой блок допустим и игнорируется, но сам файл должен иметь положительную объявленную длину.

### Завершение

После client half-close Worker:

1. проверяет последний файл
2. записывает manifest
3. атомарно публикует каталог
4. загружает типы `IJob`
5. запускает подходящие типы
6. возвращает `UploadPackageResponse`

Ответ:

| Поле | Тип | Описание |
|---|---|---|
| `package_id` | string | GUID опубликованного пакета |
| `total_bytes` | int64 | Фактически принятые байты DLL |
| `files` | repeated UploadedPackageFile | имя, размер и SHA-256 |
| `started_jobs` | repeated JobResponse | успешно запущенные карточки |
| `errors` | repeated string | частичные ошибки загрузки/запуска |

Частичные ошибки содержатся в успешном gRPC response и не меняют gRPC status.

## Валидация потока

Worker отклоняет:

- frame без выбранного `oneof`
- file header или content до package header
- второй package header
- некорректный operation ID
- content длиннее объявленного файла
- преждевременное завершение файла
- пустой пакет
- больше 20 файлов
- общий размер больше 100 МБ
- path traversal, не-DLL, пустой файл или повтор имени

## Ошибки RPC

| Ситуация | gRPC status |
|---|---|
| Некорректный job ID или пакет | `InvalidArgument` |
| Карточка не найдена | `NotFound` |
| Недопустимое состояние start/stop | `FailedPrecondition` |
| Пакет уже существует | `AlreadyExists` |
| Превышен лимит пакета | `ResourceExhausted` |
| Внутренняя ошибка операции | `Internal` |

При отмене server call незавершенный временный пакет удаляется в `DisposeAsync`.

## Совместимость

При изменении `job.proto`:

- не переиспользуйте удаленные field numbers
- добавляйте новые значения enum в конец и сохраняйте нулевое unspecified
- сохраняйте поведение пустых строк для отсутствующих scalar string полей
- обновляйте mapping одновременно в Worker и API
- проверяйте client streaming на старых и новых клиентах

Проект пока не объявляет политику версионирования gRPC-контракта.
