using System.Collections.Concurrent;
using Jobs.Diagnostics;
using Jobs.States.Interfaces;
using Jobs.States.Models;

namespace Jobs.States;

/// <summary>
/// Реестр внутренних состояний задания
/// </summary>
internal sealed class StateRegistry
{
    private const int MaxInspectionItems = 50;
    private readonly ConcurrentDictionary<string, ICheckpointState> _states =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Регистрирует внутреннее состояние
    /// </summary>
    /// <param name="state">Регистрируемое состояние</param>
    public void Register(ICheckpointState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.Name);

        if (!_states.TryAdd(state.Name, state))
            throw new InvalidOperationException($"State '{state.Name}' is already registered");
    }

    /// <summary>
    /// Создает снимки всех зарегистрированных состояний
    /// </summary>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Снимки состояний по их наименованиям</returns>
    public async Task<Dictionary<string, StateSnapshot>> CaptureAllAsync(
        CancellationToken cancellationToken)
    {
        var snapshots = await Task.WhenAll(
            _states.Values.Select(async state =>
                new KeyValuePair<string, StateSnapshot>(
                    state.Name,
                    await state.CaptureAsync(cancellationToken))));

        return snapshots.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Восстанавливает зарегистрированные состояния из снимков
    /// </summary>
    /// <param name="snapshots">Снимки состояний по их наименованиям</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Задача завершения восстановления зарегистрированных состояний, для которых переданы снимки</returns>
    public async Task RestoreAllAsync(
        IReadOnlyDictionary<string, StateSnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        var restoreTasks = _states.Values
            .Where(state => snapshots.ContainsKey(state.Name))
            .Select(state => state.RestoreAsync(snapshots[state.Name], cancellationToken).AsTask());

        await Task.WhenAll(restoreTasks);
    }

    /// <summary>
    /// Создает диагностические снимки зарегистрированных состояний
    /// </summary>
    /// <returns>Упорядоченные по имени диагностические снимки состояний, поддерживающих просмотр</returns>
    internal IReadOnlyList<JobStateSnapshot> CaptureInspections() => _states.Values
        .OfType<IInspectableState>()
        .Select(state => state.CaptureInspection(MaxInspectionItems))
        .OrderBy(state => state.Name, StringComparer.Ordinal)
        .ToArray();
}
