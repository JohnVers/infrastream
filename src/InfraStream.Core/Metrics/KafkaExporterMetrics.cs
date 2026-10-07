using InfraStream.Core.Exporters;

namespace InfraStream.Core.Metrics;

/// <summary>
/// Thread-safe counters and gauges for <see cref="KafkaExporter"/>.
/// All operations are lock-free and allocation-free.
/// </summary>
/// <remarks>
/// <para>
/// Counters are monotonic — they only increase. Use <see cref="Reset"/>
/// to reset them to zero (typically for tests only).
/// </para>
/// <para>
/// Gauges reflect the current state (buffer size, spill file count, etc.).
/// </para>
/// </remarks>
public sealed class KafkaExporterMetrics
{
    // ---- Counters (monotonic) ----
    private long _queuedTotal;
    private long _sentTotal;
    private long _spilledTotal;
    private long _readFromDiskTotal;
    private long _dlqTotal;
    private long _errorsTotal;
    private long _bufferOverflowsTotal;
    private long _diskFullTotal;

    // ---- Gauges (current values) ----
    private long _bufferSize;
    private long _diskSpillFiles;
    private long _diskSpillBytes;

    /// <summary>Total number of items placed into the in-memory buffer.</summary>
    public long QueuedTotal => Interlocked.Read(ref _queuedTotal);

    /// <summary>Total number of messages successfully produced to Kafka.</summary>
    public long SentTotal => Interlocked.Read(ref _sentTotal);

    /// <summary>Total number of items spilled to disk.</summary>
    public long SpilledTotal => Interlocked.Read(ref _spilledTotal);

    /// <summary>Total number of items read back from disk spill files.</summary>
    public long ReadFromDiskTotal => Interlocked.Read(ref _readFromDiskTotal);

    /// <summary>Total number of messages sent to the dead-letter topic.</summary>
    public long DlqTotal => Interlocked.Read(ref _dlqTotal);

    /// <summary>Total number of Kafka errors observed.</summary>
    public long ErrorsTotal => Interlocked.Read(ref _errorsTotal);

    /// <summary>
    /// Total number of times the in-memory buffer was full when a new
    /// item arrived.
    /// </summary>
    public long BufferOverflowsTotal => Interlocked.Read(ref _bufferOverflowsTotal);

    /// <summary>
    /// Total number of times the disk spill area was full and the
    /// worker had to block.
    /// </summary>
    public long DiskFullTotal => Interlocked.Read(ref _diskFullTotal);

    /// <summary>Current number of items in the in-memory buffer.</summary>
    public long BufferSize => Interlocked.Read(ref _bufferSize);

    /// <summary>Current number of spill files on disk.</summary>
    public long DiskSpillFiles => Interlocked.Read(ref _diskSpillFiles);

    /// <summary>Current total size of spill files on disk, in bytes.</summary>
    public long DiskSpillBytes => Interlocked.Read(ref _diskSpillBytes);

    // ---- Increment methods (internal use only) ----

    internal void RecordQueued() => Interlocked.Increment(ref _queuedTotal);
    internal void RecordSent() => Interlocked.Increment(ref _sentTotal);
    internal void RecordSpilled() => Interlocked.Increment(ref _spilledTotal);
    internal void RecordReadFromDisk() => Interlocked.Increment(ref _readFromDiskTotal);
    internal void RecordDlq() => Interlocked.Increment(ref _dlqTotal);
    internal void RecordError() => Interlocked.Increment(ref _errorsTotal);
    internal void RecordBufferOverflow() => Interlocked.Increment(ref _bufferOverflowsTotal);
    internal void RecordDiskFull() => Interlocked.Increment(ref _diskFullTotal);

    // ---- Gauge setters ----

    internal void SetBufferSize(long value) => Interlocked.Exchange(ref _bufferSize, value);
    internal void SetDiskSpillFiles(long value) => Interlocked.Exchange(ref _diskSpillFiles, value);
    internal void SetDiskSpillBytes(long value) => Interlocked.Exchange(ref _diskSpillBytes, value);

    /// <summary>
    /// Resets all counters and gauges to zero.
    /// Intended for tests only.
    /// </summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _queuedTotal, 0);
        Interlocked.Exchange(ref _sentTotal, 0);
        Interlocked.Exchange(ref _spilledTotal, 0);
        Interlocked.Exchange(ref _readFromDiskTotal, 0);
        Interlocked.Exchange(ref _dlqTotal, 0);
        Interlocked.Exchange(ref _errorsTotal, 0);
        Interlocked.Exchange(ref _bufferOverflowsTotal, 0);
        Interlocked.Exchange(ref _diskFullTotal, 0);
        Interlocked.Exchange(ref _bufferSize, 0);
        Interlocked.Exchange(ref _diskSpillFiles, 0);
        Interlocked.Exchange(ref _diskSpillBytes, 0);
    }
}
