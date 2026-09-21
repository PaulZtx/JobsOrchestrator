# Документация JobsOperator

Документация описывает фактическое состояние исходного кода решения на .NET 10.

## Для первого знакомства

1. [Начало работы](getting-started.md)
2. [Архитектура](architecture.md)
3. [Разработка заданий](job-development.md)
4. [Runtime и checkpoints](runtime-and-checkpoints.md)

## Справочники

| Документ | Содержание |
|---|---|
| [Operator stack](operator-stack.md) | Границы ответственности Worker, API и Web UI |
| [REST API](rest-api.md) | HTTP endpoints, DTO, ошибки и примеры |
| [gRPC API](grpc-api.md) | Внутренний контракт API ↔ Worker |
| [Конфигурация](configuration.md) | JSON-параметры, URL, переменные окружения и приоритеты |
| [Kafka и Docker](kafka-and-docker.md) | Compose-стенд, темы и Kafka relay |
| [Эксплуатация и ограничения](operations-and-limitations.md) | Хранение данных, безопасность, сбои, ограничения и roadmap |

## Карта исходного кода

| Каталог | Документ |
|---|---|
| `Jobs/` | [Разработка заданий](job-development.md), [Runtime и checkpoints](runtime-and-checkpoints.md) |
| `JobsOperator/` | [Начало работы](getting-started.md), [Kafka и Docker](kafka-and-docker.md) |
| `JobsOperator.Protos/` | [gRPC API](grpc-api.md) |
| `JobsOperator.Worker/` | [Operator stack](operator-stack.md), [Конфигурация](configuration.md) |
| `JobsOperator.Api/` | [REST API](rest-api.md), [Конфигурация](configuration.md) |
| `JobsOperator.Web/` | [Operator stack](operator-stack.md), [Конфигурация](configuration.md) |
| `TestBuilds/` | [Разработка заданий](job-development.md), [Kafka и Docker](kafka-and-docker.md) |
| `compose.yaml` | [Kafka и Docker](kafka-and-docker.md) |

## Условные обозначения

- **карточка задания** — стабильная запись Worker, с которой работает пользователь
- **попытка выполнения** — конкретный запуск в `JobsOrchestrator` со своим `RuntimeJobId`
- **пакет** — набор DLL, опубликованный Worker в одном каталоге
- **позиция источника** — смещение следующего, еще не обработанного элемента
- **checkpoint** — единый JSON-снимок позиций источников и зарегистрированных состояний

[Вернуться в корневой README](../README.md)
