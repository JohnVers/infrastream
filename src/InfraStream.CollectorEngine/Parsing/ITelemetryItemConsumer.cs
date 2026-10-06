using InfraStream.Core.Models;

namespace InfraStream.CollectorEngine.Parsing;

/// <summary>
/// Consumer interface into which the high-throughput parser pushes
/// parsed <see cref="TelemetryItem"/> values by reference.
/// </summary>
public interface ITelemetryItemConsumer
{
    /// <summary>
    /// Hot-path sink method for a single parsed log record.
    /// The item is passed by reference (<c>in</c>) to avoid copying.
    /// </summary>
    /// <param name="item">The parsed telemetry item.</param>
    void OnItemParsed(in TelemetryItem item);
}
