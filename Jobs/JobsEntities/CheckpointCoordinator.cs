using System.Text.Json;
using Jobs.Connectors;
using Jobs.Enums;
using Jobs.Sources.Interfaces;
using Jobs.States;
using Jobs.States.Models;

namespace Jobs.JobsEntities;

/// <summary>
/// Координирует восстановление и сохранение контрольных точек
/// </summary>
internal sealed class CheckpointCoordinator
{
    private const int CurrentFormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly CheckpointCoordinatorOptions _options;
    private readonly string _checkpointPath;

    /// <summary>
    /// Создает координатор контрольных точек
    /// </summary>
    /// <param name="options">Параметры координатора</param>
    public CheckpointCoordinator(CheckpointCoordinatorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var checkpointPath = options.JobStartOptions.CheckpointPath;
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointPath);

        if (!options.CheckpointOptions.Enabled)
            throw new ArgumentException("Checkpoint coordinator cannot be created when checkpoints are disabled.", nameof(options));

        if (options.CheckpointOptions.DelayMillisecond < 1)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Checkpoint interval must be at least 1 millisecond.");

        _options = options;
        _checkpointPath = checkpointPath;
    }

    /// <summary>
    /// Восстанавливает контрольную точку из файла
    /// </summary>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Восстановленная контрольная точка</returns>
    public async Task<CheckpointDocument> RestoreAsync(CancellationToken cancellationToken)
    {
        var path = _checkpointPath;
        var restoreMode = _options.JobStartOptions.RestoreMode;
        var checkpointExists = File.Exists(path);

        if (restoreMode == CheckpointRestoreMode.CreateNew)
        {
            if (checkpointExists)
                throw new IOException($"Checkpoint file '{path}' already exists.");

            return EmptyCheckpoint();
        }

        if (!checkpointExists)
        {
            if (restoreMode == CheckpointRestoreMode.ResumeOnly)
                throw new FileNotFoundException("Checkpoint file was not found.", path);

            return EmptyCheckpoint();
        }

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var checkpoint = await JsonSerializer.DeserializeAsync<CheckpointDocument>(
                stream,
                JsonOptions,
                cancellationToken)
                ?? throw new InvalidDataException($"Checkpoint file '{path}' is empty.");

            if (checkpoint.Version != CurrentFormatVersion)
            {
                throw new InvalidDataException(
                    $"Checkpoint file '{path}' has unsupported format version {checkpoint.Version}.");
            }

            var sourcePositions = ValidatePositions(path, checkpoint.Sources);
            var stateSnapshots = ValidateStateSnapshots(path, checkpoint.States);

            return new CheckpointDocument
            {
                Version = checkpoint.Version,
                CreatedAtUtc = checkpoint.CreatedAtUtc,
                Sources = sourcePositions,
                States = stateSnapshots
            };
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Checkpoint file '{path}' contains invalid JSON.", exception);
        }
    }

    /// <summary>
    /// Периодически сохраняет контрольные точки источников
    /// </summary>
    /// <param name="sourceRunners">Обработчики источников</param>
    /// <param name="stateRegistry">Реестр внутренних состояний</param>
    /// <param name="cancellationToken">Токен отмены</param>
    public async Task RunAsync(
        IReadOnlyList<ISourceRunner> sourceRunners,
        StateRegistry stateRegistry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceRunners);
        ArgumentNullException.ThrowIfNull(stateRegistry);

        using var timer = new PeriodicTimer(
            TimeSpan.FromMilliseconds(_options.CheckpointOptions.DelayMillisecond));

        while (await timer.WaitForNextTickAsync(cancellationToken))
            await CaptureAsync(sourceRunners, stateRegistry, cancellationToken);
    }

    /// <summary>
    /// Приостанавливает источники и фиксирует их согласованные позиции
    /// </summary>
    /// <param name="sourceRunners">Обработчики источников</param>
    /// <param name="stateRegistry">Реестр внутренних состояний</param>
    /// <param name="cancellationToken">Токен отмены</param>
    private async Task CaptureAsync(
        IReadOnlyList<ISourceRunner> sourceRunners,
        StateRegistry stateRegistry,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.WhenAll(sourceRunners.Select(runner => runner.PauseAsync(cancellationToken)));

            var positions = await Task.WhenAll(
                sourceRunners.Select(async runner =>
                    new KeyValuePair<string, SourcePosition>(
                        runner.Name,
                        await runner.CaptureStateAsync())));

            var sourcePositions = positions.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);

            var stateSnapshots = await stateRegistry.CaptureAllAsync(cancellationToken);

            ValidatePositions(_checkpointPath, sourcePositions);
            ValidateStateSnapshots(_checkpointPath, stateSnapshots);
            await SaveAsync(sourcePositions, stateSnapshots, cancellationToken);
        }
        finally
        {
            await Task.WhenAll(sourceRunners.Select(runner => runner.ResumeAsync()));
        }
    }

    /// <summary>
    /// Атомарно сохраняет контрольную точку в файл
    /// </summary>
    /// <param name="sourcePositions">Позиции источников</param>
    /// <param name="stateSnapshots">Снимки внутренних состояний</param>
    /// <param name="cancellationToken">Токен отмены</param>
    private async Task SaveAsync(
        IReadOnlyDictionary<string, SourcePosition> sourcePositions,
        IReadOnlyDictionary<string, StateSnapshot> stateSnapshots,
        CancellationToken cancellationToken)
    {
        var checkpointPath = _checkpointPath;
        var directory = Path.GetDirectoryName(checkpointPath)
            ?? throw new InvalidOperationException($"Checkpoint path '{checkpointPath}' has no parent directory.");

        Directory.CreateDirectory(directory);

        var checkpoint = new CheckpointDocument
        {
            Version = CurrentFormatVersion,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Sources = new Dictionary<string, SourcePosition>(sourcePositions, StringComparer.Ordinal),
            States = new Dictionary<string, StateSnapshot>(stateSnapshots, StringComparer.Ordinal)
        };

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(checkpointPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, checkpoint, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, checkpointPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // Ошибка очистки временного файла не должна скрывать ошибку сохранения
            }
        }
    }

    /// <summary>
    /// Проверяет корректность позиций источников
    /// </summary>
    /// <param name="path">Путь к контрольной точке</param>
    /// <param name="positions">Позиции источников</param>
    /// <returns>Проверенные позиции источников</returns>
    private static Dictionary<string, SourcePosition> ValidatePositions(
        string path,
        Dictionary<string, SourcePosition>? positions)
    {
        if (positions is null)
            throw new InvalidDataException($"Checkpoint file '{path}' does not contain source positions.");

        foreach (var (sourceName, position) in positions)
        {
            if (string.IsNullOrWhiteSpace(sourceName))
                throw new InvalidDataException($"Checkpoint file '{path}' contains an empty source name.");

            if (position is null || position.Offset < 0)
                throw new InvalidDataException($"Checkpoint for source '{sourceName}' contains an invalid position.");
        }

        return new Dictionary<string, SourcePosition>(positions, StringComparer.Ordinal);
    }

    /// <summary>
    /// Проверяет корректность снимков внутренних состояний
    /// </summary>
    /// <param name="path">Путь к контрольной точке</param>
    /// <param name="snapshots">Снимки внутренних состояний</param>
    /// <returns>Проверенные снимки внутренних состояний</returns>
    private static Dictionary<string, StateSnapshot> ValidateStateSnapshots(
        string path,
        Dictionary<string, StateSnapshot>? snapshots)
    {
        if (snapshots is null)
            throw new InvalidDataException($"Checkpoint file '{path}' does not contain state snapshots");

        foreach (var (stateName, snapshot) in snapshots)
        {
            if (string.IsNullOrWhiteSpace(stateName))
                throw new InvalidDataException($"Checkpoint file '{path}' contains an empty state name");

            if (snapshot is null || snapshot.Version < 1 || snapshot.Payload is null)
                throw new InvalidDataException($"Checkpoint for state '{stateName}' contains an invalid snapshot");
        }

        return new Dictionary<string, StateSnapshot>(snapshots, StringComparer.Ordinal);
    }

    /// <summary>
    /// Создает пустую контрольную точку
    /// </summary>
    /// <returns>Пустая контрольная точка</returns>
    private static CheckpointDocument EmptyCheckpoint()
    {
        return new CheckpointDocument
        {
            Version = CurrentFormatVersion,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }
}
