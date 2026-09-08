namespace Jobs.Connectors;

/// <summary>
/// Элемент, прочитанный из источника вместе с позицией следующего чтения
/// </summary>
/// <typeparam name="T">Тип значения</typeparam>
/// <param name="Value">Прочитанное значение</param>
/// <param name="Position">Позиция следующего чтения</param>
public sealed record SourceRecord<T>(T Value, SourcePosition Position);

/// <summary>
/// Элемент, передаваемый в принимающий узел
/// </summary>
/// <typeparam name="T">Тип значения</typeparam>
/// <param name="Value">Передаваемое значение</param>
public sealed record SinkRecord<T>(T Value);

/// <summary>
/// Позиция следующего элемента источника
/// </summary>
/// <param name="Offset">Смещение следующего элемента</param>
public sealed record SourcePosition(long Offset);
