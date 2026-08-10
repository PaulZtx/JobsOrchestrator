namespace Jobs.JobsEntities.Interfaces.Sources;

/// <summary>
/// Регистратор источников
/// </summary>
internal interface ISourceRegistration
{
    /// <summary>
    /// Сборка итогового источника данных
    /// </summary>
    /// <returns></returns>
    ISourceDefinition Build();
}