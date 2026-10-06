using System.Text;
using InfraStream.Core.Models;

namespace InfraStream.Core.Exporters;

/// <summary>
/// Minimal exporter that prints every telemetry item to the console.
/// </summary>
/// <remarks>
/// <para>
/// Intended for local development, troubleshooting, and as a reference
/// implementation of <see cref="ITelemetryExporter"/>.
/// </para>
/// <para>
/// WARNING: do not use this exporter in production. Writing to the console
/// is synchronous and serialized; at high RPS it becomes a bottleneck and
/// will degrade the gateway throughput.
/// </para>
/// </remarks>
public sealed class ConsoleExporter : ITelemetryExporter
{
    /// <inheritdoc />
    public string Name => "ConsoleExporter";

    /// <inheritdoc />
    public void ExportItem(in TelemetryItem item)
    {
        // IMPORTANT: the ReadOnlyMemory<byte> values inside `item` point INTO
        // the batch buffer and are only valid for the duration of this call.
        // They are copied into strings immediately, so nothing is retained
        // beyond the call.
        string timestamp = item.Timestamp.ToString("O");
        string level     = Decode(item.Level);
        string message   = Decode(item.Message);
        string component = Decode(item.Component);

        // Console.Out is thread-safe, so no additional locking is needed even
        // when several workers call ExportItem in parallel.
        Console.Out.WriteLine($"[ConsoleExporter] {timestamp} {level} {component}");
        Console.Out.WriteLine($"  message: {message}");

        if (!item.Attributes.IsEmpty)
        {
            Console.Out.WriteLine("  attributes:");
            foreach (ref readonly var attr in item.Attributes.Span)
            {
                string key = Decode(attr.Key);
                string val = Decode(attr.Value);
                Console.Out.WriteLine($"    {key} = {val}");
            }
        }
    }

    /// <inheritdoc />
    public void Flush()
    {
        // Nothing to flush: Console is unbuffered.
    }

    private static string Decode(ReadOnlyMemory<byte> bytes)
        => bytes.IsEmpty ? string.Empty : Encoding.UTF8.GetString(bytes.Span);
}
