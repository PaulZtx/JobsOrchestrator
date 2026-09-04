using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Jobs.Connectors.Interfaces;

namespace Jobs.Connectors;

/// <summary>
/// Читает JSON-значения из построчного файла
/// </summary>
/// <typeparam name="T">Тип читаемых значений</typeparam>
public class JsonFileConnectorSource<T> : IConnectorSource<T>
{
    private readonly string _fileName;
    private readonly byte[] _buffer;
    private StreamReader _streamReader = null!;
    
    
    /// <summary>
    /// Создает источник для заданного файла
    /// </summary>
    /// <param name="fileName">Путь к файлу</param>
    public JsonFileConnectorSource(string fileName)
    {
        _fileName = fileName;
        _buffer = new byte[1024];
    }
    
    /// <inheritdoc />
    public async IAsyncEnumerable<SourceRecord<T>> ReadNextAsync(
        SourcePosition position,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int lineNumber = 0;

        while (lineNumber < position.Offset)
        {
            if (await _streamReader.ReadLineAsync(cancellationToken) is null)
                throw new InvalidDataException("Checkpoint is outside the file.");

            lineNumber++;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line = await _streamReader.ReadLineAsync(cancellationToken);

            if (line is null)
            {
                await Task.Delay(250, cancellationToken);
                continue;
            }

            T value = JsonSerializer.Deserialize<T>(line) ?? throw new InvalidDataException($"JSON at line {lineNumber + 1} contains null.");

            lineNumber++;

            // Позиция указывает следующую строку для чтения
            yield return new SourceRecord<T>(value, new SourcePosition(lineNumber));
        }
    }

    /// <inheritdoc />
    public Task CommitAsync(SourcePosition position, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public bool TryConnect()
    {
        var fileStream = File.Open(_fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        
        _streamReader = new StreamReader(fileStream);
        
        return true;
    }
}
