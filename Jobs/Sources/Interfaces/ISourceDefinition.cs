using Jobs.Connectors;

namespace Jobs.Sources.Interfaces;

/// <summary>
/// Описывает источник данных задания
/// </summary>
internal interface ISourceDefinition
{
    /// <summary>
    /// Наименование источника
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Создает обработчик источника с заданной начальной позицией
    /// </summary>
    /// <param name="serviceProvider">Провайдер сервисов</param>
    /// <param name="sourcePosition">Начальная позиция источника</param>
    /// <returns>Обработчик источника</returns>
    ISourceRunner CreateRunner(IServiceProvider serviceProvider, SourcePosition sourcePosition);
}
