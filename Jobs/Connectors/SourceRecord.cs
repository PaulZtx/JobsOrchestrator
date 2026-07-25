namespace Jobs.Connectors;

public sealed record SourceRecord<T>(T Value, SourcePosition Position);

public sealed record SinkRecord<T>(T Value);
    

public sealed record SourcePosition(int Offset );