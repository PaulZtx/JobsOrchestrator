using System.Collections.Concurrent;
using System.Text.Json;
using Jobs.Diagnostics;
using Jobs.States.Interfaces;
using Jobs.States.Models;

namespace Jobs.States;

/// <summary>
/// Потокобезопасное состояние в виде неупорядоченной коллекции
/// </summary>
/// <typeparam name="T">Тип элементов состояния</typeparam>
/// <param name="name">Уникальное имя состояния</param>
internal sealed class BagState<T>(string name) : IState<T>, ICheckpointState, IInspectableState
{
    private const int CurrentSnapshotVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentBag<T> _collection = new();
    private readonly Lock _lock = new();

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public void Push(T item)
    {
        lock (_lock)
            _collection.Add(item);
    }

    /// <inheritdoc />
    public IEnumerable<T> GetByPredicate(Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        lock (_lock)
            return _collection.Where(predicate).ToArray();
    }

    /// <inheritdoc />
    JobStateSnapshot IInspectableState.CaptureInspection(int maxItems)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxItems);

        lock (_lock)
        {
            var items = _collection
                .Take(maxItems)
                .Select(DiagnosticValueFormatter.Format)
                .ToArray();

            return new JobStateSnapshot(
                Name,
                typeof(T).FullName ?? typeof(T).Name,
                _collection.Count,
                items);
        }
    }

    /// <inheritdoc />
    ValueTask<StateSnapshot> ICheckpointState.CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        byte[] payload;

        lock (_lock)
            payload = JsonSerializer.SerializeToUtf8Bytes(_collection.ToArray(), JsonOptions);

        return ValueTask.FromResult(new StateSnapshot(CurrentSnapshotVersion, payload));
    }

    /// <inheritdoc />
    ValueTask ICheckpointState.RestoreAsync(
        StateSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Version != CurrentSnapshotVersion)
        {
            throw new InvalidDataException(
                $"State '{Name}' has unsupported snapshot version {snapshot.Version}");
        }

        var restoredItems = JsonSerializer.Deserialize<T[]>(snapshot.Payload, JsonOptions)
            ?? throw new InvalidDataException($"Snapshot for state '{Name}' contains null");

        lock (_lock)
        {
            _collection.Clear();

            foreach (var item in restoredItems)
                _collection.Add(item);
        }

        return ValueTask.CompletedTask;
    }
}
