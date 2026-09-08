using System.Text.Json;

namespace Jobs.Diagnostics;

/// <summary>
/// Форматирует произвольные значения для диагностического интерфейса.
/// </summary>
internal static class DiagnosticValueFormatter
{
    private const int MaxLength = 4_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    /// <summary>
    /// Сериализует значение в компактную и ограниченную по размеру строку.
    /// </summary>
    public static string Format<T>(T value)
    {
        string text;

        try
        {
            text = JsonSerializer.Serialize(value, JsonOptions);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            try
            {
                text = value?.ToString() ?? "null";
            }
            catch (Exception fallbackException) when (fallbackException is not OutOfMemoryException)
            {
                text = $"<{typeof(T).FullName ?? typeof(T).Name}>";
            }
        }

        return text.Length <= MaxLength
            ? text
            : $"{text[..MaxLength]}…";
    }
}
