using Jobs.Diagnostics;

namespace Jobs.States.Interfaces;

/// <summary>
/// Внутренний контракт получения безопасного снимка состояния для диагностики.
/// </summary>
internal interface IInspectableState
{
    /// <summary>
    /// Создает диагностический снимок, ограничивая количество возвращаемых элементов.
    /// </summary>
    JobStateSnapshot CaptureInspection(int maxItems);
}
