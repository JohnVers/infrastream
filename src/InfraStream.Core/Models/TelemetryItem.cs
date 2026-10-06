namespace InfraStream.Core.Models;

/// <summary>
/// Hybrid representation of a single telemetry item (log line).
/// Passed through the pipeline strictly by reference (<c>in</c> / <c>ref</c>)
/// to avoid copying.
/// </summary>
/// <remarks>
/// All string-like fields are <see cref="ReadOnlyMemory{T}"/> of UTF-8 bytes.
/// No UTF-16 conversion is performed on the hot path.
/// The exporter decides when to convert (typically when writing to a
/// database or a message broker).
/// </remarks>
public readonly struct TelemetryItem
{
    /// <summary>Timestamp of the original log event.</summary>
    public DateTime Timestamp { get; }

    /// <summary>Log level (e.g. <c>INFO</c>, <c>WARN</c>, <c>ERROR</c>), UTF-8.</summary>
    public ReadOnlyMemory<byte> Level { get; }

    /// <summary>Log message, UTF-8. May already be PII-masked in-place.</summary>
    public ReadOnlyMemory<byte> Message { get; }

    /// <summary>Component / service name that emitted the log, UTF-8.</summary>
    public ReadOnlyMemory<byte> Component { get; }

    /// <summary>Custom dynamic log attributes.</summary>
    public ReadOnlyMemory<LogAttribute> Attributes { get; }

    /// <summary>
    /// Raw slice of the original JSON object of the log line,
    /// intended for pass-through export.
    /// </summary>
    /// <remarks>
    /// If in-place PII masking has been applied to the source buffer,
    /// this slice contains the masked data. Consumers must be aware
    /// that they do not receive the pristine original JSON here.
    /// </remarks>
    public ReadOnlyMemory<byte> RawExtensionJson { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TelemetryItem"/> struct.
    /// </summary>
    /// <param name="timestamp">Timestamp of the original log event.</param>
    /// <param name="level">Log level, UTF-8.</param>
    /// <param name="message">Log message, UTF-8.</param>
    /// <param name="component">Component / service name, UTF-8.</param>
    /// <param name="attributes">Custom dynamic log attributes.</param>
    /// <param name="rawExtensionJson">Raw slice of the original JSON object.</param>
    public TelemetryItem(
        DateTime timestamp,
        ReadOnlyMemory<byte> level,
        ReadOnlyMemory<byte> message,
        ReadOnlyMemory<byte> component,
        ReadOnlyMemory<LogAttribute> attributes,
        ReadOnlyMemory<byte> rawExtensionJson)
    {
        Timestamp = timestamp;
        Level = level;
        Message = message;
        Component = component;
        Attributes = attributes;
        RawExtensionJson = rawExtensionJson;
    }
}
