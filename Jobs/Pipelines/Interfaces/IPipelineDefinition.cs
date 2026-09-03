using Jobs.Connectors;

namespace Jobs.Pipelines.Interfaces;

/// <summary>
/// Описывает полностью сконфигурированный конвейер задания.
/// </summary>
internal interface IPipelineDefinition
{
    string SourceName { get; }

    IPipelineRunner CreateRunner(
        IServiceProvider serviceProvider,
        SourcePosition sourcePosition);
}
