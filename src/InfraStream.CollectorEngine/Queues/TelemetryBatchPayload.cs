namespace InfraStream.CollectorEngine.Queues;

/// <summary>
/// Rentable binary container for a telemetry batch.
/// Lifecycle: <c>Rent()</c> → fill → queue → worker → <c>Dispose()</c>.
/// <see cref="Dispose"/> is guarded against repeated calls: the second
/// and all subsequent calls are no-ops.
/// </summary>
public sealed class TelemetryBatchPayload : IDisposable
{
    // 0 = active (in use), 1 = returned to the pool.
    private int _returned;

    /// <summary>
    /// Raw array rented from <c>ArrayPool</c>. The array length is
    /// greater than or equal to <see cref="Length"/>.
    /// </summary>
    /// <remarks>
    /// Never read beyond <see cref="Length"/>.
    /// </remarks>
    public byte[] Array { get; internal set; } = null!;

    /// <summary>
    /// Actual length of the payload bytes in <see cref="Array"/>.
    /// </summary>
    public int Length { get; internal set; }

    /// <summary>
    /// Whether the data is compressed (Brotli/gzip) or passed through
    /// as-is. If <see langword="false"/>, the worker skips decompression.
    /// </summary>
    public bool IsCompressed { get; set; }

    /// <summary>
    /// Session metadata attached at the socket layer.
    /// </summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// Session metadata attached at the socket layer.
    /// </summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>Slice <c>[0, Length)</c> over <see cref="Array"/>.</summary>
    public Memory<byte> Memory => Array.AsMemory(0, Length);

    /// <summary>Slice <c>[0, Length)</c> over <see cref="Array"/>.</summary>
    public ReadOnlySpan<byte> Span => Array.AsSpan(0, Length);

    /// <summary>
    /// Writable slice <c>[0, Length)</c> — used for in-place masking.
    /// </summary>
    public Span<byte> WritableSpan => Array.AsSpan(0, Length);

    /// <summary>
    /// Called by <c>TelemetryBatchPool.Rent()</c> before the instance is
    /// handed out. Resets the return flag so that the next
    /// <see cref="Dispose"/> will take effect again.
    /// </summary>
    internal void ResetForRent()
    {
        Volatile.Write(ref _returned, 0);
    }

    /// <summary>
    /// Returns the instance to <c>TelemetryBatchPool</c>.
    /// Repeated calls are ignored.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _returned, 1) != 0)
            return;

        NodeId = string.Empty;
        Environment = string.Empty;
        IsCompressed = false;
        Length = 0;

        TelemetryBatchPool.Return(this);
    }
}
