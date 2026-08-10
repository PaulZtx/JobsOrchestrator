using System.Text;
using System.Text.Json;

namespace Jobs.JobsEntities.Interfaces;

public interface ISerializer<T>
{
    T? Deserialize(ReadOnlySpan<byte> data);
    byte[] Serialize(T data);
}

public class CustomJsonSerializer<T> : ISerializer<T>
{
    public T? Deserialize(ReadOnlySpan<byte> data)
    {
        return JsonSerializer.Deserialize<T>(data);
    }

    public byte[] Serialize(T data)
    {
        var str = JsonSerializer.Serialize(data);
        return Encoding.UTF8.GetBytes(str);
    }
}