using System.Collections.Concurrent;
using Jobs.States.Interfaces;
using Jobs.States.Models;

namespace Jobs.States;

/// <summary>
/// Реестр внутренних состояний задания
/// </summary>
internal sealed class StateRegistry
{
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
}
