using System.Runtime.ExceptionServices;
using Jobs.Connectors;
using Jobs.Diagnostics;
using Jobs.JobsEntities.Interfaces;
using Jobs.Pipelines.Interfaces;

namespace Jobs.JobsEntities;

/// <summary>
/// Управляет выполнением задания и восстановлением его состояния
/// </summary>
/// <param name="definition">Описание задания</param>
/// <param name="serviceProvider">Провайдер сервисов</param>
internal sealed class BasicJobRuntime(
    JobDefinition definition,
    IServiceProvider serviceProvider) : IJobRuntime
{
    private IPipelineRunner[] _pipelineRunners = [];

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

        var pipelineRunners = definition.Pipelines
            .Select(pipeline => pipeline.CreateRunner(
                serviceProvider,
                restoredCheckpoint?.Sources.GetValueOrDefault(pipeline.SourceName) ?? new SourcePosition(0)))
            .ToArray();

        Volatile.Write(ref _pipelineRunners, pipelineRunners);

        var pipelineTasks = pipelineRunners
            .Select(pipeline => pipeline.RunAsync(runtimeCancellation.Token))
            .ToList();

        var coordinatorTask = checkpointCoordinator?.RunAsync(
            pipelineRunners,
            definition.StateRegistry,
            runtimeCancellation.Token);

        try
        {
            while (pipelineTasks.Count > 0)
            {
                var tasksToObserve = coordinatorTask is null
                    ? pipelineTasks
                    : pipelineTasks.Append(coordinatorTask);

                var completed = await Task.WhenAny(tasksToObserve);

                if (ReferenceEquals(completed, coordinatorTask))
                {
                    await runtimeCancellation.CancelAsync();
                    await ObserveFailuresAsync(pipelineTasks);

                    if (completed.IsFaulted)
                        ExceptionDispatchInfo.Capture(completed.Exception!.GetBaseException()).Throw();

                    await completed;
                    throw new InvalidOperationException("Checkpoint coordinator stopped unexpectedly.");
                }

                pipelineTasks.Remove(completed);

                if (completed.IsFaulted || completed.IsCanceled)
                {
                    await runtimeCancellation.CancelAsync();
                    await ObserveFailuresAsync(
                        coordinatorTask is null
                            ? pipelineTasks
                            : pipelineTasks.Append(coordinatorTask));

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

        return new CheckpointCoordinator(new CheckpointCoordinatorOptions
        {
            CheckpointOptions = checkpointOptions
        });
    }

    /// <summary>
    /// Возвращает агрегированные показатели выполнения и состояние задания.
    /// </summary>
    internal JobRuntimeSnapshot CaptureDiagnostics()
    {
        var pipelineSnapshots = Volatile.Read(ref _pipelineRunners)
            .Select(pipeline => pipeline.CaptureDiagnostics())
            .ToArray();

        var processedCount = pipelineSnapshots.Sum(snapshot => snapshot.ProcessedCount);
        var lastProcessed = pipelineSnapshots
            .Where(snapshot => snapshot.LastProcessedAt is not null)
            .MaxBy(snapshot => snapshot.LastProcessedAt);

        return new JobRuntimeSnapshot(
            processedCount,
            lastProcessed?.LastProcessedAt,
            lastProcessed?.LastProcessedMessage,
            definition.StateRegistry.CaptureInspections());
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
            // Первая ошибка конвейера или координатора обрабатывается в основном цикле
        }
    }
}
