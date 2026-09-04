using Jobs.Connectors;

namespace Jobs.Pipelines.Interfaces;

/// <summary>
/// Описывает полностью настроенный конвейер задания
/// </summary>
internal interface IPipelineDefinition
{
    /// <summary>
    /// Имя источника
    /// </summary>
    string SourceName { get; }

    /// <summary>
    /// Создает среду выполнения конвейера
    /// </summary>
    /// <param name="serviceProvider">Провайдер сервисов</param>
    /// <param name="sourcePosition">Начальная позиция источника</param>
    /// <returns>Среда выполнения конвейера</returns>
    IPipelineRunner CreateRunner(
        IServiceProvider serviceProvider,
        SourcePosition sourcePosition);
}
