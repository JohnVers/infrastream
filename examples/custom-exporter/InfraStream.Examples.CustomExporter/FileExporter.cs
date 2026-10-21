using System.Buffers;
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
/// It demonstrates:
/// <list type="number">
///   <item>The ownership contract: <see cref="ReadOnlyMemory{T}"/> values inside
///         a <see cref="TelemetryItem"/> point into the batch buffer, so they
///         are read synchronously and never retained.</item>
///   <item>Thread safety: <see cref="ExportItem"/> may be called concurrently
///         from several workers, so the internal buffer is guarded by a lock.</item>
///   <item>Deferred I/O: items are accumulated in a pooled byte buffer and
///         written to disk when <see cref="Flush"/> is called (periodically by
///         the worker, and on <see cref="Dispose"/> at shutdown).</item>
/// </list>
/// </para>
/// <para>
/// NOT production-grade: no rotation, no compression, no retry, no
/// backpressure. For production use, add those on top of this pattern.
/// </para>
/// </remarks>
public sealed class FileExporter : ITelemetryExporter, IDisposable
{
    private const string DefaultFilePath = "telemetry.jsonl";

    private readonly object _sync = new();
    private readonly string _filePath;
    private readonly ArrayBufferWriter<byte> _buffer = new(initialCapacity: 64 * 1024);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileExporter"/> class
    /// that writes to <c>telemetry.jsonl</c> in the current directory.
    /// </summary>
    public FileExporter() : this(DefaultFilePath) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileExporter"/> class
    /// that writes to <paramref name="filePath"/>.
    /// </summary>
    /// <param name="filePath">Path of the output JSONL file.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="filePath"/> is <see langword="null"/>.
    /// </exception>
    public FileExporter(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    /// <inheritdoc />
    public string Name => "FileExporter";

    /// <inheritdoc />
    public void ExportItem(in TelemetryItem item)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            using (var writer = new Utf8JsonWriter(_buffer))
            {
                writer.WriteStartObject();

                writer.WriteString("timestamp", item.Timestamp.ToString("O"));
                writer.WriteString("level",     item.Level.Span);
                writer.WriteString("component", item.Component.Span);
                writer.WriteString("message",   item.Message.Span);

                if (!item.Attributes.IsEmpty)
                {
                    writer.WriteStartObject("attributes");
                    foreach (ref readonly var attr in item.Attributes.Span)
                    {
                        writer.WriteString(attr.Key.Span, attr.Value.Span);
                    }
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            _buffer.Write("\n"u8);
        }
    }

    /// <inheritdoc />
    public void Flush()
    {
        lock (_sync)
        {
            FlushCore();
        }
    }

    /// <summary>
    /// Flushes any buffered items and releases resources.
    /// </summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;

            try
            {
                FlushCore();
            }
            finally
            {
                _disposed = true;
            }
        }
    }

    private void FlushCore()
    {
        if (_disposed || _buffer.WrittenCount == 0) return;

        using var stream = new FileStream(
            _filePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read);
        stream.Write(_buffer.WrittenSpan);

        _buffer.ResetWrittenCount();
    }
}
