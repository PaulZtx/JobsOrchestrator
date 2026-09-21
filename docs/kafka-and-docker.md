# Kafka и Docker

## Состав Compose-стенда

`compose.yaml` поднимает локальный Kafka KRaft-кластер без ZooKeeper:

| Сервис | Назначение |
|---|---|
| `kafka-volume-init` | Выставляет владельца каталогов named volumes |
| `kafka-1`, `kafka-2`, `kafka-3` | Три broker/controller узла Kafka 4.3.1 |
| `kafka-init` | После health checks создает необходимые темы |
| `kafka-ui` | Kafbat UI для локального просмотра |
| `jobsoperator` | Console runner в режиме `kafka-relay` |
| `kafka-load` | Опциональный perf producer профиля `load` |

Compose запускает Kafka-сценарий console runner, а не Worker/API/Web stack.

## Запуск

```shell
docker compose up -d --build
```

Проверка:

```shell
docker compose ps
docker compose logs jobsoperator
```

Kafka UI доступен только на loopback:

```text
http://localhost:8080
```

## Сетевые адреса

| Узел | С host | Внутри Compose |
|---|---|---|
| Broker 1 | `localhost:9092` | `kafka-1:19092` |
| Broker 2 | `localhost:9093` | `kafka-2:19092` |
| Broker 3 | `localhost:9094` | `kafka-3:19092` |

Controller quorum использует внутренние адреса `kafka-1:9093`, `kafka-2:9093`, `kafka-3:9093`.

Кластер настроен с replication factor 3 и `min.insync.replicas=2` для создаваемых тем. Auto topic creation отключен.

## Темы

`kafka-init` создает:

| Тема | Partitions | Назначение |
|---|---:|---|
| `jobs-input` | 1 | Вход `KafkaRelayJob` |
| `jobs-output` | 1 | Выход `KafkaRelayJob` |
| `jobs-load-test` | 12 по умолчанию | Нагрузочный producer |

Имена можно изменить переменными `KAFKA_INPUT_TOPIC`, `KAFKA_OUTPUT_TOPIC` и `KAFKA_TOPIC`.

## Kafka relay

`KafkaRelayJob`:

1. читает UTF-8 string из input topic, partition 0
2. вычисляет `value.Length`
3. сериализует `int` как JSON
4. пишет результат в output topic, partition 0
5. создает checkpoint каждую секунду

Consumer group:

```text
jobs-orchestrator-kafka-relay
```

Consumer использует `AutoOffsetReset.Earliest`, но после назначения конкретного offset основным источником позиции является checkpoint.

Container environment:

```text
JOB_MODE=kafka-relay
KAFKA_BOOTSTRAP_SERVERS=kafka-1:19092,kafka-2:19092,kafka-3:19092
KAFKA_INPUT_TOPIC=jobs-input
KAFKA_OUTPUT_TOPIC=jobs-output
KAFKA_CHECKPOINT_PATH=/app/checkpoints/kafka-relay.json
```

Checkpoint хранится в named volume `jobsoperator-checkpoints`.

## Отправка и чтение сообщений

Откройте producer:

```shell
docker compose exec kafka-1 \
  /opt/kafka/bin/kafka-console-producer.sh \
  --bootstrap-server kafka-1:19092,kafka-2:19092,kafka-3:19092 \
  --topic jobs-input
```

Введите несколько строк и завершите `Ctrl+C`.

Прочитайте длины:

```shell
docker compose exec kafka-1 \
  /opt/kafka/bin/kafka-console-consumer.sh \
  --bootstrap-server kafka-1:19092,kafka-2:19092,kafka-3:19092 \
  --topic jobs-output \
  --from-beginning
```

Так как output сериализован как JSON integer, для строки `hello` будет записано `5`.

## Генератор нагрузки

```shell
docker compose --profile load run --rm kafka-load
```

Настройка примера:

```shell
KAFKA_NUM_RECORDS=1000000 \
KAFKA_RECORD_SIZE=512 \
KAFKA_THROUGHPUT=50000 \
docker compose --profile load run --rm kafka-load
```

Producer использует `acks=all` и idempotence. Нагрузочная тема не обрабатывается `KafkaRelayJob`: она предназначена для отдельного тестирования Kafka.

## Данные и остановка

```shell
docker compose down
```

Останавливает контейнеры, сохраняя:

- `kafka-1-data`
- `kafka-2-data`
- `kafka-3-data`
- `jobsoperator-checkpoints`

```shell
docker compose down -v
```

Удаляет перечисленные volumes, все Kafka records и checkpoint relay. Операция необратима без внешней резервной копии.

## Docker image JobsOperator

`JobsOperator/Dockerfile` использует multi-stage build:

1. restore и build на `mcr.microsoft.com/dotnet/sdk:10.0`
2. publish без app host
3. запуск на `mcr.microsoft.com/dotnet/runtime:10.0`
4. создание `/app/checkpoints` с владельцем `APP_UID`
5. запуск `dotnet JobsOperator.dll` не-root пользователем

Ручная сборка:

```shell
docker build -f JobsOperator/Dockerfile -t jobsoperator .
```

## Диагностика

```shell
docker compose logs kafka-init
docker compose logs kafka-1
docker compose logs jobsoperator
```

Если relay повторно обрабатывает сообщения, проверьте:

- доступность и содержимое volume checkpoint
- значение `KAFKA_CHECKPOINT_PATH`
- имена input/output topics
- offsets в checkpoint
- не был ли checkpoint удален через `down -v`

Текущие Kafka-коннекторы работают с одним явно указанным partition, по умолчанию `0`; consumer group rebalancing и распределение partitions runtime не реализует.
