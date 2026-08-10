namespace Jobs.Sources.Interfaces;

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