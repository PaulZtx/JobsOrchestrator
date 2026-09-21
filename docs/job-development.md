# Разработка заданий

## Создание проекта

Создайте библиотеку классов для `net10.0` и добавьте ссылку на ядро:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../Jobs/Jobs.csproj" />
  </ItemGroup>
</Project>
```

Для загрузки через Worker реализация должна быть:

- классом, реализующим `IJob`
- конкретной, то есть не `abstract`
- создаваемой через публичный конструктор без параметров
- совместимой с контрактом `Jobs.dll`, предоставленным Worker

Не включайте собственную копию `Jobs.dll` в пакет: Worker пропускает такой файл и связывает задание со своей сборкой контракта.

## DSL задания

Точка входа — метод `IJob.Configure(IJobBuilder)`. Полный pipeline всегда имеет форму:

```csharp
builder
    .Source<TInput>(sourceName, sourceFactory)
    .Process<TOutput>(processName, processor)
    .ConnectToSink(sinkName, sinkFactory);
```

Если обработчик не производит полезного результата:

```csharp
builder
    .Source("source", _ => CreateSource())
    .Process(
        "observe",
        async (value, context, cancellationToken) =>
        {
            await ObserveAsync(value, cancellationToken);
        })
    .Discard();
```

`Discard()` подключает внутренний sink, который успешно принимает каждый элемент.

### Правила построения

- имя источника не должно быть пустым и должно быть уникальным в пределах задания
- имя зарегистрированного состояния должно быть уникальным
- для источника можно задать только один `Process`
- pipeline необходимо завершить ровно одним `ConnectToSink` или `Discard`
- все pipeline должны быть завершены до выхода из `Configure`
- фабрики source и sink не должны возвращать `null`
- имена process и sink проверяются на пустоту, но глобальная уникальность для них не контролируется
- после сборки определения builder изменять нельзя

## Полный пример

```csharp
using Jobs.Connectors;
using Jobs.JobsEntities;
using Jobs.JobsEntities.Interfaces;

public sealed record InputMessage(string Value);
public sealed record OutputMessage(string Value, int Length);

public sealed class FileTransformationJob : IJob
{
    public void Configure(IJobBuilder builder)
    {
        var received = builder.RegisterState<InputMessage>("received");

        builder.ConfigureCheckpoints(new CheckpointOptions
        {
            Enabled = true,
            DelayMillisecond = 5_000,
            PathToCheckpoint = "checkpoints/file-transformation.json"
        });

        builder
            .Source(
                "input-file",
                _ => new JsonFileConnectorSource<InputMessage>("input.jsonl"))
            .Process(
                "normalize",
                (message, context, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    received.Push(message);

                    var normalized = message.Value.Trim();
                    return ValueTask.FromResult(
                        new OutputMessage(normalized, normalized.Length));
                })
            .Discard();
    }
}
```

Относительные пути разрешаются относительно `AppContext.BaseDirectory` процесса Worker или console host. Для переносимости можно строить путь явно через `Path.Combine(AppContext.BaseDirectory, ...)`.

## Контракты ядра

### `IJob`

| Член | Назначение |
|---|---|
| `void Configure(IJobBuilder builder)` | Синхронно объявляет pipeline, состояния и checkpoint options |

### `IJobBuilder`

| Член | Результат |
|---|---|
| `Source<T>(name, factory)` | Начинает новый pipeline |
| `RegisterState<T>(name)` | Возвращает потокобезопасное `IState<T>` |
| `ConfigureCheckpoints(options)` | Устанавливает настройки checkpoint для всего задания |

### Стадии pipeline

| Стадия | Доступные операции |
|---|---|
| `ISourceStage<TInput>` | `Process<TOutput>` или `Process` без результата |
| `IProcessedStage<TOutput>` | `ConnectToSink` или `Discard` |

`ProcessContext` содержит неизменяемые `SourceName`, `ProcessName` и `SinkName`. Обработчик получает тот же экземпляр контекста для всех сообщений pipeline.

## Пользовательский source

Реализуйте `IConnectorSource<T>`:

```csharp
public sealed class MySource : IConnectorSource<MyMessage>
{
    public bool TryConnect()
    {
        return true;
    }

    public async IAsyncEnumerable<SourceRecord<MyMessage>> ReadNextAsync(
        SourcePosition position,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var offset = position.Offset;

        while (!cancellationToken.IsCancellationRequested)
        {
            var message = await ReadExternalAsync(offset, cancellationToken);
            offset++;
            yield return new SourceRecord<MyMessage>(
                message,
                new SourcePosition(offset));
        }
    }

    public Task CommitAsync(
        SourcePosition position,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
```

Инварианты source:

- `SourceRecord.Position` — позиция следующего еще не обработанного элемента, а не позиция текущего
- `ReadNextAsync` должен начинать с переданного offset
- цикл чтения должен регулярно учитывать `CancellationToken`
- `TryConnect` вызывается один раз перед чтением и должен вернуть `false` при невозможности работать
- текущий runtime не вызывает `CommitAsync`; контракт оставлен для подтверждения источника

Если коннектор владеет ресурсами, учитывайте, что runtime явно освобождает sink, но не вызывает `Dispose` у произвольного source. Встроенный Kafka source закрывает consumer в `finally` своего async iterator.

## Пользовательский sink

Реализуйте `IConnectorSink<T>`:

```csharp
public sealed class MySink : IConnectorSink<OutputMessage>, IAsyncDisposable
{
    public bool TryConnect() => true;

    public async Task<bool> WriteAsync(
        SinkRecord<OutputMessage> record,
        CancellationToken cancellationToken)
    {
        await WriteExternalAsync(record.Value, cancellationToken);
        return true;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
```

`WriteAsync` должен вернуть `true` только после успешной фиксации результата. `false` считается отказом sink и завершает задание ошибкой. Runtime освобождает sink после остановки: сначала через `IAsyncDisposable`, иначе через `IDisposable`.

## Встроенные коннекторы и sinks

### `JsonFileConnectorSource<T>`

- открывает файл на чтение с `FileShare.ReadWrite`
- пропускает число строк, заданное checkpoint offset
- десериализует каждую новую строку через `System.Text.Json`
- при EOF ждет 250 мс и продолжает следить за файлом
- выбрасывает ошибку, если checkpoint указывает за текущий конец файла
- `CommitAsync` пока выбрасывает `NotImplementedException`, но runtime его не вызывает

### `KafkaConnectorSource<T>`

- принимает `ISerializer<T>`, `ConsumerConfig`, topic и partition
- по умолчанию читает partition `0`
- принудительно отключает `EnableAutoCommit` и `EnableAutoOffsetStore`
- делает `Assign` с offset из checkpoint
- закрывает и освобождает consumer при завершении iterator

### `KafkaSink<T>`

- принимает `ISerializer<T>`, `ProducerConfig`, topic и partition
- по умолчанию пишет в partition `0`
- принудительно включает `Acks.All` и `EnableIdempotence`
- считает запись успешной только при `PersistenceStatus.Persisted`
- при освобождении ждет flush до 10 секунд

### Сериализация

`ISerializer<T>` определяет `Serialize(T)` и `Deserialize(ReadOnlySpan<byte>)`. `CustomJsonSerializer<T>` использует `System.Text.Json` и UTF-8.

## Состояние `IState<T>`

```csharp
var state = builder.RegisterState<InputMessage>("received");

state.Push(message);
var matches = state.GetByPredicate(item => item.Value.Length > 10);
```

Текущая реализация — потокобезопасный неупорядоченный bag:

- `Push` добавляет элемент
- `GetByPredicate` возвращает отдельный снимок подходящих элементов
- порядок элементов не гарантирован
- состояние полностью сериализуется в checkpoint
- диагностика показывает полное количество и не более 50 значений

Тип `T` должен корректно сериализоваться и десериализоваться `System.Text.Json`, если checkpoints включены.

## Внедрение зависимостей

Фабрики source и sink получают `IServiceProvider`, переданный в конструктор `JobsOrchestrator`:

```csharp
.Source(
    "source",
    services => new MySource(
        services.GetRequiredService<MyDependency>()))
```

Console host создает `JobsOrchestrator` без provider, поэтому в нем доступен только пустой provider. В Worker оркестратор создается встроенным DI-контейнером ASP.NET Core и получает корневой provider. Фабрики загруженного задания могут разрешать зарегистрированные singleton/transient services; использовать scoped service из корневого provider без явной scope-модели не следует.

## Сборка пакета

```shell
dotnet build MyJobs/MyJobs.csproj
```

Загрузите основную DLL и необходимые сторонние DLL из output-каталога. Файлы `.deps.json`, `.pdb`, native libraries и вложенные каталоги протокол загрузки не принимает. Зависимости разрешаются по имени `<AssemblyName>.dll` из одного каталога пакета.

## Примеры в репозитории

| Тип | Сценарий |
|---|---|
| `SampleJob` | JSON Lines → состояние → console sink |
| `DiscardSampleJob` | JSON Lines → наблюдение → `Discard` |
| `KafkaRelayJob` | Kafka string → длина строки → Kafka integer |

Следующий документ: [Runtime и checkpoints](runtime-and-checkpoints.md).
