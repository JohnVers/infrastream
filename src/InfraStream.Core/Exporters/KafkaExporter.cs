using InfraStream.Core.Models;

namespace InfraStream.Core.Exporters;

/// <summary>
/// Exporter for Apache Kafka.
/// </summary>
/// <remarks>
/// <para>
/// TODO: implement.
/// <list type="bullet">
///   <item>Confluent.Kafka producer.</item>
///   <item>Batching (batch size, linger, compression).</item>
///   <item>Retry on failures.</item>
///   <item>Partitioning (by NodeId? by TraceId?).</item>
/// </list>
/// </para>
/// <para>
/// IMPORTANT: <see cref="TelemetryItem"/> contains
/// <see cref="ReadOnlyMemory{T}"/> values that are slices of the batch
/// buffer. The exporter MUST copy the data before sending if the producer
/// operates asynchronously (which it normally does).
/// </para>
/// </remarks>
public sealed class KafkaExporter : ITelemetryExporter
{
    private readonly string _bootstrapServers;
    private readonly string _topic;

    /// <inheritdoc />
    public string Name => "Kafka";

    /// <summary>
    /// Initializes a new instance of the <see cref="KafkaExporter"/> class.
    /// </summary>
    /// <param name="bootstrapServers">
    /// Comma-separated list of Kafka bootstrap servers
    /// (e.g. <c>kafka:9092</c>).
    /// </param>
    /// <param name="topic">Target Kafka topic for the telemetry stream.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="bootstrapServers"/> or
    /// <paramref name="topic"/> is null, empty, or whitespace.
    /// </exception>
    public KafkaExporter(string bootstrapServers, string topic)
    {
        if (string.IsNullOrWhiteSpace(bootstrapServers))
            throw new ArgumentException("Bootstrap servers must not be empty.", nameof(bootstrapServers));
        if (string.IsNullOrWhiteSpace(topic))
            throw new ArgumentException("Topic must not be empty.", nameof(topic));

        _bootstrapServers = bootstrapServers;
        _topic = topic;
    }

    /// <inheritdoc />
    public void ExportItem(in TelemetryItem item)
    {
        // TODO: serialize into JSON / MessagePack and enqueue into the
        // producer's queue.
    }

    /// <inheritdoc />
    public void Flush()
    {
        // TODO: await delivery of the accumulated messages.
    }
}
