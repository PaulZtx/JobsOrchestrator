namespace Jobs.JobsEntities.Interfaces.Sources;

/// <summary>
/// Определение источника данных
/// </summary>
internal interface ISourceDefinition
{
    /// <summary>
    /// Формирование раннера для обработки данных с источника
    /// </summary>
    /// <param name="serviceProvider"></param>
    /// <returns></returns>
    ISourceRunner CreateRunner(IServiceProvider serviceProvider);
}