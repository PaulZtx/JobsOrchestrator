namespace Jobs.Connectors.Interfaces;

/// <summary>
/// Коннектор для внешних источников
/// </summary>
public interface IConnector
{
    /// <summary>
    /// Устанавливает соединение с внешним узлом
    /// </summary>
    /// <returns>Признак успешного соединения</returns>
    bool TryConnect();
}
