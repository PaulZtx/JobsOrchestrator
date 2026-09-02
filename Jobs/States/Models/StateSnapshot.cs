namespace Jobs.States.Models;

/// <summary>
/// Снимок внутреннего состояния задания
/// </summary>
/// <param name="Version">Версия формата снимка</param>
/// <param name="Payload">Сериализованные данные состояния</param>
internal sealed record StateSnapshot(int Version, byte[] Payload);
