using System.Text;
using System.Text.Json;

namespace Jobs.JobsEntities.Interfaces;

/// <summary>
/// Преобразует значения заданного типа в байты и обратно
/// </summary>
/// <typeparam name="T">Тип сериализуемых значений</typeparam>
public interface ISerializer<T>
{
    /// <summary>
    /// Восстанавливает значение из набора байтов
    /// </summary>
    /// <param name="data">Сериализованные данные</param>
    /// <returns>Восстановленное значение</returns>
    T? Deserialize(ReadOnlySpan<byte> data);

    /// <summary>
    /// Преобразует значение в набор байтов
    /// </summary>
    /// <param name="data">Сериализуемое значение</param>
    /// <returns>Сериализованные данные</returns>
    byte[] Serialize(T data);
}

/// <summary>
/// Сериализует значения в формате JSON
/// </summary>
/// <typeparam name="T">Тип сериализуемых значений</typeparam>
public class CustomJsonSerializer<T> : ISerializer<T>
{
    /// <inheritdoc />
    public T? Deserialize(ReadOnlySpan<byte> data)
    {
        return JsonSerializer.Deserialize<T>(data);
    }

    /// <inheritdoc />
    public byte[] Serialize(T data)
    {
        var str = JsonSerializer.Serialize(data);
        return Encoding.UTF8.GetBytes(str);
    }
}
