namespace InfraStream.Core.Configuration;

/// <summary>
/// Configuration options for <c>KafkaExporter</c>.
/// Bound from the <c>InfraStream:Exporters:Kafka</c> section of
/// <c>appsettings.json</c> or via environment variables using the standard
/// ASP.NET Core convention.
/// </summary>
/// <remarks>
/// Example <c>appsettings.json</c>:
/// <code>
/// {
///   "InfraStream": {
///     "Exporters": {
///       "Kafka": {
///         "BootstrapServers": "kafka:9092",
///         "Topic": "telemetry",
///         "DeadLetterTopic": "telemetry-dead-letter",
///         "InMemoryBufferSize": 1000,
///         "DiskSpillPath": "./spill/",
///         "MaxSpillFileSizeBytes": 104857600,
///         "FlushIntervalMs": 500,
///         "KafkaFlushTimeoutMs": 5000,
///         "BlockTimeoutMs": 30000
///       }
///     }
///   }
/// }
/// </code>
/// Environment override example:
/// <code>
/// InfraStream__Exporters__Kafka__BootstrapServers=kafka:9092
/// </code>
/// </remarks>
public sealed record KafkaExporterOptions
{
    /// <summary>
    /// Configuration section name in <c>appsettings.json</c>.
    /// </summary>
    public const string SectionName = "InfraStream:Exporters:Kafka";

    /// <summary>
    /// Comma-separated list of Kafka bootstrap servers.
    /// </summary>
    /// <remarks>
    /// Example: <c>"kafka:9092"</c> or <c>"broker1:9092,broker2:9092"</c>.
    /// </remarks>
    public string BootstrapServers { get; init; } = string.Empty;

    /// <summary>
    /// Target Kafka topic for the telemetry stream.
    /// </summary>
    public string Topic { get; init; } = "telemetry";

    /// <summary>
    /// Kafka topic used as a dead-letter queue for messages rejected
    /// by the broker (e.g. validation or serialization failures).
    /// </summary>
    public string DeadLetterTopic { get; init; } = "telemetry-dead-letter";

    /// <summary>
    /// Maximum number of messages held in the in-memory buffer before
    /// the exporter spills to disk.
    /// </summary>
    /// <remarks>
    /// Larger values reduce disk I/O but increase RAM usage. The default
    /// of 1000 messages is roughly 1 MB for typical telemetry items.
    /// </remarks>
    public int InMemoryBufferSize { get; init; } = 1000;

    /// <summary>
    /// Directory used for disk spill files when the in-memory buffer is full.
    /// </summary>
    /// <remarks>
    /// Relative paths are resolved against the current working directory.
    /// In containers, mount a volume here to avoid losing data on restart.
    /// </remarks>
    public string DiskSpillPath { get; init; } = "./spill/";

    /// <summary>
    /// Maximum size of a single spill file before rotation.
    /// </summary>
    /// <remarks>
    /// Default: 100 MB (104 857 600 bytes).
    /// </remarks>
    public long MaxSpillFileSizeBytes { get; init; } = 104_857_600;

    /// <summary>
    /// Interval between flush iterations of the background flush loop.
    /// </summary>
    /// <remarks>
    /// Default: 500 ms.
    /// </remarks>
    public int FlushIntervalMs { get; init; } = 500;

    /// <summary>
    /// Maximum time to wait for the Kafka producer to flush in-flight
    /// messages before returning.
    /// </summary>
    /// <remarks>
    /// Default: 5000 ms (5 s).
    /// </remarks>
    public int KafkaFlushTimeoutMs { get; init; } = 5000;

    /// <summary>
    /// Maximum time a worker waits on <c>Monitor.Wait</c> when both the
    /// in-memory buffer and the disk spill area are full.
    /// </summary>
    /// <remarks>
    /// After this timeout the worker proceeds and drops the message,
    /// recording a metric. This avoids hanging the entire pipeline
    /// indefinitely if the downstream never recovers.
    /// Default: 30000 ms (30 s).
    /// </remarks>
    public int BlockTimeoutMs { get; init; } = 30_000;
}
