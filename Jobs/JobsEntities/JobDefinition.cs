using Jobs.Pipelines.Interfaces;
using Jobs.States;

namespace Jobs.JobsEntities;

/// <summary>
/// Неизменяемое определение задания
/// </summary>
/// <param name="pipelines">Коллекция конвейеров</param>
/// <param name="checkpointOptions">Параметры контрольных точек</param>
/// <param name="stateRegistry">Реестр внутренних состояний</param>
internal sealed class JobDefinition(
    IReadOnlyCollection<IPipelineDefinition> pipelines,
    CheckpointOptions? checkpointOptions,
    StateRegistry stateRegistry)
{
    /// <summary>
    /// Параметры создания контрольных точек
    /// </summary>
    internal CheckpointOptions? CheckpointOptions { get; } = checkpointOptions;

    /// <summary>
    /// Настроенные конвейеры
    /// </summary>
    internal IReadOnlyCollection<IPipelineDefinition> Pipelines { get; } = pipelines;

    /// <summary>
    /// Реестр внутренних состояний
    /// </summary>
    internal StateRegistry StateRegistry { get; } = stateRegistry;
}
