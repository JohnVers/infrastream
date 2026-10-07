using System.Text.Json;
using InfraStream.Core.Models;

namespace InfraStream.Core.Serialization;

/// <summary>
/// Zero-allocation serializer for <see cref="TelemetryItem"/> into Kafka
/// message payloads (UTF-8 JSON).
/// </summary>
/// <remarks>
/// <para>
/// The serializer writes directly into a buffer rented from
/// <see cref="System.Buffers.ArrayPool{T}"/> via
/// <see cref="PooledBufferWriter"/>. The caller is responsible for
/// returning the writer to the pool after the message has been produced.
/// </para>
/// <para>
/// The JSON shape is:
/// <code>
/// {
///   "timestamp": "2026-10-08T...",
///   "level": "INFO",
///   "component": "auth-service",
///   "message": "User logged in",
///   "attributes": { "trace_id": "abc", "span_id": "def" }
/// }
/// </code>
/// The <c>attributes</c> object is omitted if the item has no attributes.
/// </para>
/// </remarks>
internal static class KafkaExporterSerializer
{
    /// <summary>
    /// Serializes a <see cref="TelemetryItem"/> into a pooled UTF-8 JSON buffer.
    /// </summary>
    /// <param name="item">The telemetry item to serialize.</param>
    /// <returns>
    /// A <see cref="PooledBufferWriter"/> containing the serialized JSON. The
    /// caller MUST call <see cref="PooledBufferWriter.Return"/> when done.
    /// </returns>
    public static PooledBufferWriter Serialize(in TelemetryItem item)
    {
        var writer = new PooledBufferWriter();

        try
        {
            using (var json = new Utf8JsonWriter(writer))
            {
                WriteItem(json, in item);
            }

            return writer;
        }
        catch
        {
            writer.Return();
            throw;
        }
    }

    private static void WriteItem(Utf8JsonWriter writer, in TelemetryItem item)
    {
        writer.WriteStartObject();

        writer.WriteString(JsonFields.Timestamp, item.Timestamp);
        writer.WriteString(JsonFields.Level, item.Level.Span);
        writer.WriteString(JsonFields.Component, item.Component.Span);
        writer.WriteString(JsonFields.Message, item.Message.Span);

        if (!item.Attributes.IsEmpty)
        {
            writer.WriteStartObject(JsonFields.Attributes);
            foreach (ref readonly var attr in item.Attributes.Span)
            {
                writer.WriteString(attr.Key.Span, attr.Value.Span);
            }
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }
}
