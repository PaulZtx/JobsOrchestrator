using Jobs.Pipelines.Interfaces;
using Jobs.States;

namespace Jobs.JobsEntities;

/// <summary>
/// Определение конкретной Job.
/// </summary>
/// <param name="pipelines">Коллекция конвейеров</param>
/// <param name="checkpointOptions">Параметры контрольных точек</param>
/// <param name="stateRegistry">Реестр внутренних состояний</param>
internal sealed class JobDefinition(
    IReadOnlyCollection<IPipelineDefinition> pipelines,
    CheckpointOptions? checkpointOptions,
    StateRegistry stateRegistry)
{
    internal CheckpointOptions? CheckpointOptions { get; } = checkpointOptions;

    internal IReadOnlyCollection<IPipelineDefinition> Pipelines { get; } = pipelines;

    internal StateRegistry StateRegistry { get; } = stateRegistry;
}
