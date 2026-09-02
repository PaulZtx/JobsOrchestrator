namespace Jobs.States.Interfaces;

/// <summary>
/// Базовый контракт внутреннего состояния задания
/// </summary>
public interface IState
{
    /// <summary>
    /// Наименование состояния
    /// </summary>
    string Name { get; }
}
