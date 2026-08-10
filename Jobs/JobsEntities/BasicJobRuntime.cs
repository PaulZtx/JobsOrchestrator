using System.Runtime.ExceptionServices;
using Jobs.JobsEntities.Interfaces;
using Jobs.Sources.Interfaces;

namespace Jobs.JobsEntities;

/// <summary>
/// Базовая реализация Runtime для управления Job
/// </summary>
/// <param name="definition"></param>
/// <param name="serviceProvider"></param>
internal sealed class BasicJobRuntime(
    JobDefinition definition,
    IServiceProvider serviceProvider,
    CheckpointCoordinatorOptions? checkpointCoordinatorOptions = null) : IJobRuntime
{
    private IReadOnlyList<ISourceRunner> _sourceRunners = [];

    private CheckpointCoordinator? _checkpointCoordinator;

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var runtimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _sourceRunners = definition.Sources
            .Select(source => source.CreateRunner(serviceProvider))
            .ToArray();

        var sourceTasks = _sourceRunners
            .Select(source => source.RunAsync(runtimeCancellation.Token))
            .ToList();

        try
        {
            if (checkpointCoordinatorOptions is not null)
            {
                checkpointCoordinatorOptions.CheckpointOptions = definition.CheckpointOptions;
                
                _checkpointCoordinator = new CheckpointCoordinator(checkpointCoordinatorOptions, _sourceRunners);
                var coordinatorTask = _checkpointCoordinator.RunAsync(runtimeCancellation.Token);
                sourceTasks.Add(coordinatorTask);
            }

            while (sourceTasks.Count > 0)
            {
                var completed = await Task.WhenAny(sourceTasks);
                sourceTasks.Remove(completed);

                if (completed.IsFaulted)
                {
                    await runtimeCancellation.CancelAsync();
                    await ObserveFailuresAsync(sourceTasks);
                    ExceptionDispatchInfo.Capture(completed.Exception!.GetBaseException()).Throw();
                }

                await completed;
            }
        }
        finally
        {
            await runtimeCancellation.CancelAsync();
        }
    }

    private static async Task ObserveFailuresAsync(IEnumerable<Task> tasks)
    {
        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
            // The first source failure is reported to the orchestrator.
        }
    }
}
