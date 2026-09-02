using System.Runtime.ExceptionServices;
using Jobs.Connectors;
using Jobs.JobsEntities.Interfaces;
using Jobs.Sources.Interfaces;

namespace Jobs.JobsEntities;

/// <summary>
/// Управляет выполнением задания и восстановлением его состояния
/// </summary>
/// <param name="definition">Описание задания</param>
/// <param name="serviceProvider">Провайдер сервисов</param>
/// <param name="jobStartOptions">Параметры запуска задания</param>
internal sealed class BasicJobRuntime(
    JobDefinition definition,
    IServiceProvider serviceProvider,
    JobStartOptions? jobStartOptions) : IJobRuntime
{
    private IReadOnlyList<ISourceRunner> _sourceRunners = [];

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var runtimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var checkpointCoordinator = CreateCheckpointCoordinator();
        var restoredCheckpoint = checkpointCoordinator is null
            ? null
            : await checkpointCoordinator.RestoreAsync(runtimeCancellation.Token);

        if (restoredCheckpoint is not null)
        {
            await definition.StateRegistry.RestoreAllAsync(
                restoredCheckpoint.States,
                runtimeCancellation.Token);
        }

        _sourceRunners = definition.Sources
            .Select(source => source.CreateRunner(
                serviceProvider,
                restoredCheckpoint?.Sources.GetValueOrDefault(source.Name) ?? new SourcePosition(0)))
            .ToArray();

        var sourceTasks = _sourceRunners
            .Select(source => source.RunAsync(runtimeCancellation.Token))
            .ToList();

        var coordinatorTask = checkpointCoordinator?.RunAsync(
            _sourceRunners,
            definition.StateRegistry,
            runtimeCancellation.Token);

        try
        {
            while (sourceTasks.Count > 0)
            {
                var tasksToObserve = coordinatorTask is null
                    ? sourceTasks
                    : sourceTasks.Append(coordinatorTask);

                var completed = await Task.WhenAny(tasksToObserve);

                if (ReferenceEquals(completed, coordinatorTask))
                {
                    await runtimeCancellation.CancelAsync();
                    await ObserveFailuresAsync(sourceTasks);

                    if (completed.IsFaulted)
                        ExceptionDispatchInfo.Capture(completed.Exception!.GetBaseException()).Throw();

                    await completed;
                    throw new InvalidOperationException("Checkpoint coordinator stopped unexpectedly.");
                }

                sourceTasks.Remove(completed);

                if (completed.IsFaulted || completed.IsCanceled)
                {
                    await runtimeCancellation.CancelAsync();
                    await ObserveFailuresAsync(
                        coordinatorTask is null
                            ? sourceTasks
                            : sourceTasks.Append(coordinatorTask));

                    if (completed.IsFaulted)
                        ExceptionDispatchInfo.Capture(completed.Exception!.GetBaseException()).Throw();
                }

                await completed;
            }
        }
        finally
        {
            await runtimeCancellation.CancelAsync();

            if (coordinatorTask is not null)
                await ObserveFailuresAsync([coordinatorTask]);
        }
    }

    /// <summary>
    /// Создает координатор при включенных контрольных точках
    /// </summary>
    /// <returns>Координатор или null при отключенных контрольных точках</returns>
    private CheckpointCoordinator? CreateCheckpointCoordinator()
    {
        if (definition.CheckpointOptions is not { Enabled: true } checkpointOptions)
            return null;

        if (jobStartOptions is null)
            throw new InvalidOperationException("Checkpoint start options were not configured.");

        return new CheckpointCoordinator(new CheckpointCoordinatorOptions
        {
            CheckpointOptions = checkpointOptions,
            JobStartOptions = jobStartOptions
        });
    }

    /// <summary>
    /// Ожидает завершения задач и подавляет уже наблюдаемые ошибки
    /// </summary>
    /// <param name="tasks">Задачи для ожидания</param>
    private static async Task ObserveFailuresAsync(IEnumerable<Task> tasks)
    {
        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
            // Первая ошибка источника или координатора обрабатывается в основном цикле
        }
    }
}
