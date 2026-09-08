using Jobs.Diagnostics;

namespace Jobs.States.Interfaces;

/// <summary>
/// Внутренний контракт получения безопасного снимка состояния для диагностики
/// </summary>
internal interface IInspectableState
{
    /// <summary>
    /// Создает диагностический снимок, ограничивая количество возвращаемых элементов
    /// </summary>
    /// <param name="maxItems">Максимальное количество элементов в предпросмотре, не меньше нуля</param>
    /// <returns>Снимок с общим количеством элементов и ограниченным предпросмотром их значений</returns>
    JobStateSnapshot CaptureInspection(int maxItems);
}
