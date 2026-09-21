# Эксплуатация и ограничения

## Текущий статус

JobsOperator — работающий прототип, а не готовая production-платформа. Он демонстрирует типизированный pipeline, согласование позиции со state, динамическую загрузку и отдельный управляющий stack. Гарантии совместимости, доставки и восстановления еще не формализованы тестами и версионированием.

## Наблюдаемость

Доступная диагностика:

- стандартные ASP.NET Core logs Worker, API и Web
- статус попытки
- число успешно обработанных записей
- время и содержимое последней входной записи
- количество и preview зарегистрированных состояний
- сообщение базового исключения
- Kafka UI в Compose-стенде

Ограничения диагностики:

- последнее значение обрезается до 4000 символов
- preview состояния ограничен 50 элементами
- нет `ILogger` в core runtime и console runner
- нет метрик OpenTelemetry
- нет distributed tracing и correlation от REST до сообщения pipeline
- нет health, readiness и liveness endpoints
- история попыток не сохраняется

Web UI опрашивает API раз в секунду. Это polling, а не push/SignalR.

## Хранение и восстановление

### Пакеты

Worker атомарно публикует пакет и manifest в `JobPackages:Path`. Перезапуск Worker не удаляет файлы, но текущая реализация не сканирует каталог при старте и не восстанавливает карточки.

Удаление карточки через API не удаляет пакет. Автоматическая retention/garbage collection отсутствует.

### Checkpoints

Checkpoint может восстановить source positions и состояния при новом запуске только если задание использует тот же стабильный путь. Файл создается и читается пользовательским runtime; Worker не ведет отдельный каталог метаданных checkpoint.

Рекомендации:

- размещайте checkpoints на устойчивом диске
- резервируйте их вместе с соответствующей версией assembly
- не редактируйте файл во время выполнения
- перед обновлением типа state проверяйте совместимость JSON
- для критичных данных тестируйте crash/restart и поврежденный файл

### Kafka

Kafka checkpoint фреймворка — источник истины. Удаление checkpoint при сохраненных Kafka records приводит к чтению с начальной переданной позиции, обычно offset 0.

## Поведение при сбоях

| Сбой | Текущее поведение |
|---|---|
| Processor выбросил исключение | pipeline и все задание завершаются ошибкой |
| Sink вернул `false` | задание завершается ошибкой |
| Source завершился успешно | consumer дочитывает канал; pipeline может завершиться |
| Один из нескольких pipeline упал | остальные отменяются |
| Checkpoint не удалось записать | coordinator падает, все pipeline отменяются |
| Checkpoint содержит неверный JSON/версию | запуск завершается ошибкой |
| API недоступен | Worker продолжает выполнение |
| Worker недоступен | API возвращает 503/502; задания процесса Worker недоступны |
| Worker завершился | выполняемые задания прекращаются, карточки теряются |
| Web потерял Blazor-соединение | показывается reconnect UI; Worker не затрагивается |

Retry и автоматический restart задания отсутствуют.

## Безопасность

Критические ограничения:

- authentication и authorization отсутствуют
- загруженные DLL выполняются как доверенный код
- process boundary Worker не изолирует вредоносную сборку
- нет CPU, memory, filesystem или network quotas для задания
- нет allowlist, подписи assembly или publisher verification
- API принимает пакеты до 100 МБ и не имеет rate limiting
- локальные HTTP-профили не шифруют трафик
- secrets management не интегрирован

До устранения ограничений используйте систему только в доверенной локальной или изолированной среде.

## Ограничения модели pipeline

- ровно `Source -> Process -> Sink`
- одна process stage
- нет ветвления, объединения, фильтра и окон
- один последовательный consumer worker на pipeline
- channel capacity жестко задана как 128
- parallelism не настраивается
- batching отсутствует
- порядок между разными pipeline не определен
- sink и state не участвуют в общей транзакции

## Ограничения коннекторов

### JSON Lines

- файл держится открытым до остановки
- source наблюдает EOF бесконечно
- `CommitAsync` не реализован
- произвольное изменение уже прочитанных строк не отслеживается
- truncation/rotation файла явно не обрабатываются

### Kafka

- по умолчанию и в примере используется только partition 0
- нет consumer group assignment/rebalance
- offsets Kafka не коммитятся
- нет transactional consume-transform-produce
- нет retry/DLQ для poison messages

## Ограничения state/checkpoint

- только bag state
- удаление и update элементов отсутствуют
- порядок не гарантируется
- весь state хранится в памяти
- весь state сериализуется при каждом checkpoint
- формат checkpoint версии 1 не имеет мигратора
- финальный checkpoint при stop отсутствует
- поврежденный checkpoint не восстанавливается из backup автоматически

## Ограничения Operator stack

- каталог карточек только в памяти
- package load contexts не collectible
- пакеты не удаляются через API
- native и не-DLL dependencies загрузить нельзя
- нет multi-worker scheduling
- нет leases, leader election или распределенного ownership
- нет истории запусков и аудита команд
- API version в URL есть, но политика эволюции контракта не определена
- UI состоит из одной административной страницы

## Проверка изменений

Базовая проверка:

```shell
dotnet restore JobsOperator.sln
dotnet build JobsOperator.sln
```

Рекомендуемые ручные сценарии:

1. console `SampleJob`: чтение, state и остановка
2. restart с checkpoint: позиция и state восстановлены
3. upload через Web: package manifest и карточки созданы
4. stop/start/delete: переходы состояний корректны
5. restart API: Worker продолжает и карточки снова читаются
6. Kafka relay: input преобразован в output
7. отказ sink/processor: карточка переходит в `Failed`
8. неверная DLL и частично валидный пакет: ошибки изолированы

Автоматического test project и coverage threshold нет.

## Roadmap

### Стабилизация runtime

- [ ] Формально определить семантику доставки и checkpoint invariants
- [ ] Добавить unit, integration и crash/recovery tests
- [ ] Реализовать корректный lifecycle всех source/sink ресурсов
- [ ] Добавить retry, backoff, timeout и dead-letter handling
- [ ] Создавать финальный checkpoint при штатной остановке
- [ ] Версионировать публичный API и checkpoint format

### Масштабирование

- [ ] Поддержать несколько Kafka partitions
- [ ] Добавить настраиваемый parallelism с управлением порядком
- [ ] Поддержать несколько process stages, filter, branch и merge
- [ ] Добавить batch processing и настройку buffer capacity
- [ ] Поддержать внешнее или инкрементальное state storage

### Эксплуатация

- [ ] Добавить structured logging, OpenTelemetry metrics и tracing
- [ ] Добавить health/readiness/liveness endpoints
- [ ] Создать устойчивый каталог заданий и историю попыток
- [ ] Добавить конфигурацию задания без пересборки DLL
- [ ] Ввести validation конфигурации и управление secrets
- [ ] Добавить миграцию и диагностику checkpoint
- [ ] Реализовать retention пакетов и checkpoints

### Безопасность и поставка

- [ ] Добавить authentication и role-based authorization
- [ ] Изолировать пользовательский код и ограничить ресурсы
- [ ] Ввести доверие к артефактам: подписи, allowlist или registry
- [ ] Защитить внешние соединения TLS
- [ ] Добавить audit log административных действий
- [ ] Подготовить production deployment manifests и CI

## Связанные документы

- [Архитектура](architecture.md)
- [Runtime и checkpoints](runtime-and-checkpoints.md)
- [Operator stack](operator-stack.md)
- [Конфигурация](configuration.md)
