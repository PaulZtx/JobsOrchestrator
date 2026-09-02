namespace Jobs.States.Interfaces;

/// <summary>
/// Контракт внутреннего состояния для элементов заданного типа
/// </summary>
/// <typeparam name="T">Тип элементов состояния</typeparam>
public interface IState<T> : IState
{
    /// <summary>
    /// Добавляет элемент в состояние
    /// </summary>
    /// <param name="item">Добавляемый элемент</param>
    void Push(T item);

    /// <summary>
    /// Возвращает элементы по заданному условию
    /// </summary>
    /// <param name="predicate">Условие отбора</param>
    /// <returns>Отобранные элементы</returns>
    IEnumerable<T> GetByPredicate(Func<T, bool> predicate);
}
