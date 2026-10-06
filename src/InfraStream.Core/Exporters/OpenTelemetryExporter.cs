using InfraStream.Core.Models;

namespace InfraStream.Core.Exporters;

/// <summary>
/// Exporter for the OpenTelemetry Collector over the OTLP protocol.
/// </summary>
/// <remarks>
/// <para>
/// TODO: implement.
/// <list type="bullet">
///   <item>OTLP/gRPC or OTLP/HTTP.</item>
///   <item>Batching (batch size, timeout).</item>
///   <item>Retry on failures.</item>
///   <item>Backpressure.</item>
/// </list>
/// </para>
/// <para>
/// IMPORTANT: <see cref="TelemetryItem"/> contains
/// <see cref="ReadOnlyMemory{T}"/> values that are slices of the batch
/// buffer. The exporter MUST copy the data if it is retained beyond the
/// lifetime of the batch.
/// </para>
/// </remarks>
public sealed class OpenTelemetryExporter : ITelemetryExporter
{
    private readonly string _endpoint;

    /// <inheritdoc />
    public string Name => "OpenTelemetry";

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="OpenTelemetryExporter"/> class.
    /// </summary>
    /// <param name="endpoint">
    /// OTLP endpoint of the OpenTelemetry Collector
    /// (e.g. <c>http://otel:4317</c>).
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="endpoint"/> is null, empty, or whitespace.
    /// </exception>
    public OpenTelemetryExporter(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Endpoint must not be empty.", nameof(endpoint));

        _endpoint = endpoint;
    }

    /// <inheritdoc />
    public void ExportItem(in TelemetryItem item)
    {
        // TODO: serialize into an OTLP LogRecord and enqueue into the buffer.
    }

    /// <inheritdoc />
    public void Flush()
    {
        // TODO: send the accumulated buffer to the OTel Collector.
    }
}
