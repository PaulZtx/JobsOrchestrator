namespace Jobs.Connectors;

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

/// <summary>
/// Конфигурация Job
/// </summary>
public interface IConfiguration
{
    int GetOffset();
}