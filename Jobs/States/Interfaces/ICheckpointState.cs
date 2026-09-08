using Jobs.States.Models;

namespace Jobs.States.Interfaces;

/// <summary>
/// Контракт состояния с поддержкой создания и восстановления снимка
/// </summary>
internal interface ICheckpointState
{
    /// <summary>
    /// Наименование состояния
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Создает снимок состояния
    /// </summary>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Снимок состояния</returns>
    ValueTask<StateSnapshot> CaptureAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Восстанавливает состояние из снимка
    /// </summary>
    /// <param name="snapshot">Снимок состояния</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Операция, завершающаяся после восстановления состояния из снимка</returns>
    ValueTask RestoreAsync(StateSnapshot snapshot, CancellationToken cancellationToken);
}
