using InfraStream.Core.Models;

namespace InfraStream.Core.Exporters;

/// <summary>
/// High-throughput reference exporter for network benchmarks
/// (the /dev/null pattern).
/// </summary>
/// <remarks>
/// Simulates instantaneous, non-blocking log absorption, eliminating
/// any impact from the disk subsystem.
/// </remarks>
public sealed class NullExporter : ITelemetryExporter
{
    private long _totalItemsProcessed;

    /// <inheritdoc />
    public string Name => "NullExporter";

    /// <summary>
    /// Cumulative counter of log lines that were successfully parsed
    /// and delivered.
    /// </summary>
    public long TotalItemsProcessed => Interlocked.Read(ref _totalItemsProcessed);

    /// <summary>
    /// Allocation-free sink that completes in zero nanoseconds.
    /// </summary>
    /// <param name="item">The telemetry item being exported.</param>
    public void ExportItem(in TelemetryItem item)
    {
        // Just atomically bump the counter for the final gateway
        // performance report.
        Interlocked.Increment(ref _totalItemsProcessed);
    }

    /// <summary>
    /// Instantaneous no-op flush of accumulation buffers.
    /// </summary>
    public void Flush()
    {
        // In /dev/null mode the buffers are always pristine.
    }
}
