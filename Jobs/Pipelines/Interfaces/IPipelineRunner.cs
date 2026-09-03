using Jobs.Connectors;

namespace Jobs.Pipelines.Interfaces;

/// <summary>
/// Управляет чтением, обработкой и записью одного конвейера.
/// </summary>
internal interface IPipelineRunner
{
    string SourceName { get; }

    Task RunAsync(CancellationToken cancellationToken);

    Task PauseAsync(CancellationToken cancellationToken);

    Task ResumeAsync();

    Task StopAsync();

    Task<SourcePosition> CaptureStateAsync();
}
