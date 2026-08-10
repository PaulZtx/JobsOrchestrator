namespace Jobs.Connectors.Interfaces;

/// <summary>
/// Коннектор для внешних источников
/// </summary>
public interface IConnector
{
    /// <summary>
    /// Попытка соединения
    /// </summary>
    /// <returns></returns>
    bool TryConnect();
}