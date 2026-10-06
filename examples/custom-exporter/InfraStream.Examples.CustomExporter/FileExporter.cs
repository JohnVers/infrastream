using System.Buffers;
using System.Text;
using System.Text.Json;
using InfraStream.Core.Exporters;
using InfraStream.Core.Models;

namespace InfraStream.Examples.CustomExporter;

/// <summary>
/// Minimal custom exporter that writes every telemetry item as one JSON line
/// (JSONL) to a file.
/// </summary>
/// <remarks>
/// <para>
/// Intended as a reference for writing your own <see cref="ITelemetryExporter"/>.
/// It demonstrates three things:
/// <list type="number">
///   <item>The ownership contract: <see cref="ReadOnlyMemory{T}"/> values inside
///         a <see cref="TelemetryItem"/> point into the batch buffer, so they
///         are copied into strings before being retained.</item>
///   <item>Thread safety: <see cref="ExportItem"/> may be called concurrently
///         from several workers, so the internal buffer is guarded by a lock.</item>
///   <item>Deferred I/O: items are accumulated in memory and written to disk
///         only when <see cref="Flush"/> is called.</item>
/// </list>
/// </para>
/// <para>
/// NOT production-grade: no rotation, no compression, no retry, no
/// backpressure. For production use, replace the <see cref="StringBuilder"/>
/// with an <c>ArrayBufferWriter&lt;byte&gt;</c> and write bytes directly to
/// the file.
/// </para>
/// </remarks>
public sealed class FileExporter : ITelemetryExporter
{
    private const string FilePath = "telemetry.jsonl";

    private readonly object _sync = new();
    private readonly StringBuilder _buffer = new();

    /// <inheritdoc />
    public string Name => "FileExporter";

    /// <inheritdoc />
    public void ExportItem(in TelemetryItem item)
    {
        // Serialize the item into a single JSON object.
        string line = SerializeItem(item);

        // Append the line to the in-memory buffer. The lock is required because
        // several workers may call ExportItem in parallel.
        lock (_sync)
        {
            _buffer.Append(line);
            _buffer.Append('\n');
        }
    }

    /// <inheritdoc />
    public void Flush()
    {
        string payload;
        lock (_sync)
        {
            if (_buffer.Length == 0) return;
            payload = _buffer.ToString();
            _buffer.Clear();
        }

        // Append the accumulated JSONL to the file. AppendAllText creates the
        // file if it does not exist and appends otherwise.
        File.AppendAllText(FilePath, payload, Encoding.UTF8);
    }

    private static string SerializeItem(in TelemetryItem item)
    {
        // Utf8JsonWriter handles JSON escaping correctly. We write into a
        // temporary buffer, then decode to string for the StringBuilder.
        var bufferWriter = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(bufferWriter))
        {
            writer.WriteStartObject();

            writer.WriteString("timestamp", item.Timestamp.ToString("O"));
            writer.WriteString("level",     Decode(item.Level));
            writer.WriteString("component", Decode(item.Component));
            writer.WriteString("message",   Decode(item.Message));

            if (!item.Attributes.IsEmpty)
            {
                writer.WriteStartObject("attributes");
                foreach (ref readonly var attr in item.Attributes.Span)
                {
                    writer.WriteString(Decode(attr.Key), Decode(attr.Value));
                }
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(bufferWriter.WrittenSpan);
    }

    // Copy the UTF-8 bytes into a string. Called synchronously inside
    // ExportItem / Flush, so nothing dangles after the call returns.
    private static string Decode(ReadOnlyMemory<byte> bytes)
        => bytes.IsEmpty ? string.Empty : Encoding.UTF8.GetString(bytes.Span);
}
