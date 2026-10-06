using InfraStream.Core.Models;

namespace InfraStream.Core.Exporters;

/// <summary>
/// Contract for sink adapters targeting databases and message brokers
/// (ClickHouse, Kafka, Null, etc.).
/// </summary>
/// <remarks>
/// <para>
/// IMPORTANT: <see cref="ExportItem"/> is called SYNCHRONOUSLY from the
/// parser hot path.
/// </para>
/// <para>
/// All string-like fields of <see cref="TelemetryItem"/> are
/// <see cref="ReadOnlyMemory{T}"/> of UTF-8 bytes that point INTO THE
/// BATCH BUFFER or into the static cache.
/// </para>
/// <para>
/// The exporter MUST comply with one of the following:
/// <list type="number">
///   <item>
///     Consume the data SYNCHRONOUSLY inside <see cref="ExportItem"/>
///     (do not retain references).
///   </item>
///   <item>
///     Make a COPY if the data is retained beyond the call:
///     <code>var copy = item.Message.ToArray();</code>
///   </item>
/// </list>
/// </para>
/// <para>
/// Ownership of <c>item.Attributes</c> belongs to the caller (the parser)
/// and is RETURNED TO THE POOL immediately after <see cref="ExportItem"/>
/// returns.
/// </para>
/// <para>
/// If an implementation only reads the data (counters, aggregates),
/// no copying is required.
/// </para>
/// </remarks>
public interface ITelemetryExporter
{
    /// <summary>
    /// Unique exporter name used for monitoring and configuration.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Hands a log record over to the exporter's accumulation buffer.
    /// Called synchronously from the hot path.
    /// </summary>
    /// <param name="item">The telemetry item to export.</param>
    /// <remarks>
    /// Must not throw — an unhandled exception here would bring down
    /// the entire batch processing.
    /// </remarks>
    void ExportItem(in TelemetryItem item);

    /// <summary>
    /// Force-flushes the internal buffers to the target system.
    /// Called periodically (about every 500 ms) by
    /// <c>TelemetryProcessorWorker</c>.
    /// </summary>
    void Flush();
}
