using Confluent.Kafka;
using InfraStream.Core.Configuration;
using InfraStream.Core.Metrics;
using InfraStream.Core.Models;
using InfraStream.Core.Serialization;
using Microsoft.Extensions.Logging;

namespace InfraStream.Core.Exporters;

/// <summary>
/// Kafka exporter with disk spill, backpressure, dead-letter queue,
/// and metrics.
/// </summary>
/// <remarks>
/// <para>
/// Hot path (<see cref="ExportItem"/>) is allocation-free: the item is
/// serialized into a pooled buffer and either enqueued in the in-memory
/// buffer or spilled to disk when the buffer is full. When both the buffer
/// and the disk are full, the worker blocks up to
/// <see cref="KafkaExporterOptions.BlockTimeoutMs"/> to apply backpressure
/// end-to-end.
/// </para>
/// <para>
/// A background flush loop drains disk spill files first (oldest first),
/// then the in-memory buffer, producing messages to Kafka. Failed
/// deliveries are routed to the dead-letter topic.
/// </para>
/// </remarks>
public sealed class KafkaExporter : ITelemetryExporter, IDisposable
{
    private readonly KafkaExporterOptions _options;
    private readonly IProducer<byte[], byte[]> _producer;
    private readonly KafkaExporterMetrics _metrics;
    private readonly SpillManager _spillManager;
    private readonly ILogger<KafkaExporter> _logger;
    private readonly List<PooledBufferWriter> _buffer;
    private readonly object _sync = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _flushLoop;

    private bool _disposed;

    /// <inheritdoc />
    public string Name => "Kafka";

    /// <summary>Metrics collected by this exporter.</summary>
    public KafkaExporterMetrics Metrics => _metrics;

    /// <summary>
    /// Initializes a new <see cref="KafkaExporter"/> and creates a real
    /// Kafka producer from <paramref name="options"/>.
    /// </summary>
    /// <param name="options">Exporter configuration.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public KafkaExporter(
        KafkaExporterOptions options,
        ILogger<KafkaExporter> logger)
        : this(options, BuildProducer(options), logger)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="KafkaExporter"/> with an injected
    /// producer. Intended for tests.
    /// </summary>
    internal KafkaExporter(
        KafkaExporterOptions options,
        IProducer<byte[], byte[]> producer,
        ILogger<KafkaExporter> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(producer);
        ArgumentNullException.ThrowIfNull(logger);

        ValidateOptions(options);

        _options = options;
        _producer = producer;
        _logger = logger;
        _metrics = new KafkaExporterMetrics();
        _spillManager = new SpillManager(
            options.DiskSpillPath,
            options.MaxSpillFileSizeBytes);
        _buffer = new List<PooledBufferWriter>(options.InMemoryBufferSize);

        _logger.LogInformation(
            "KafkaExporter started: bootstrap={BootstrapServers}, topic={Topic}, dlq={DeadLetterTopic}, spillPath={SpillPath}",
            options.BootstrapServers,
            options.Topic,
            options.DeadLetterTopic,
            options.DiskSpillPath);

        _flushLoop = Task.Run(FlushLoopAsync);
    }

    /// <inheritdoc />
    public void ExportItem(in TelemetryItem item)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 1. Serialize into a pooled buffer (zero-alloc).
        PooledBufferWriter serialized = KafkaExporterSerializer.Serialize(in item);

        // 2. Try to enqueue; spill to disk or block if full.
        lock (_sync)
        {
            while (_buffer.Count >= _options.InMemoryBufferSize)
            {
                _metrics.RecordBufferOverflow();

                if (_spillManager.HasFreeSpace)
                {
                    try
                    {
                        _spillManager.Append(serialized.WrittenSpan);
                        _metrics.RecordSpilled();
                        serialized.Return();
                        return;
                    }
                    catch (IOException ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Disk spill failed; falling back to blocking wait");
                    }
                }

                // Disk full or write failed — block until buffer drains.
                _metrics.RecordDiskFull();

                if (!Monitor.Wait(_sync, _options.BlockTimeoutMs))
                {
                    // Timeout — drop to avoid hanging the pipeline.
                    _metrics.RecordError();
                    _logger.LogWarning(
                        "Buffer and disk full: blocking wait timed out after {Timeout} ms; dropping item",
                        _options.BlockTimeoutMs);
                    serialized.Return();
                    return;
                }
            }

            _buffer.Add(serialized);
            _metrics.RecordQueued();
            _metrics.SetBufferSize(_buffer.Count);
            Monitor.Pulse(_sync);
        }
    }

    /// <inheritdoc />
    public void Flush()
    {
        _producer.Flush(
            TimeSpan.FromMilliseconds(_options.KafkaFlushTimeoutMs));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _logger.LogInformation("KafkaExporter stopping");

        _cts.Cancel();

        try { _flushLoop.Wait(TimeSpan.FromSeconds(5)); }
        catch (AggregateException ex)
        {
            _logger.LogDebug(ex, "Flush loop did not finish cleanly");
        }

        try { _producer.Flush(TimeSpan.FromSeconds(5)); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Final producer flush failed");
        }

        _producer.Dispose();
        _spillManager.Dispose();

        _cts.Dispose();

        _logger.LogInformation(
            "KafkaExporter stopped: sent={Sent}, spilled={Spilled}, dlq={Dlq}, errors={Errors}",
            _metrics.SentTotal,
            _metrics.SpilledTotal,
            _metrics.DlqTotal,
            _metrics.ErrorsTotal);
    }

    // ---- private ----

    private static void ValidateOptions(KafkaExporterOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BootstrapServers))
            throw new ArgumentException(
                "Bootstrap servers must not be empty.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.Topic))
            throw new ArgumentException(
                "Topic must not be empty.", nameof(options));
    }

    private static IProducer<byte[], byte[]> BuildProducer(KafkaExporterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);

        var config = new ProducerConfig
        {
            BootstrapServers = options.BootstrapServers,
            ClientId = "infrastream-gateway",
            Acks = Acks.Leader,
            LingerMs = 5,
            EnableIdempotence = false,
        };

        return new ProducerBuilder<byte[], byte[]>(config).Build();
    }

    private async Task FlushLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                DrainDisk();
                DrainMemory();
            }
            catch (Exception ex)
            {
                _metrics.RecordError();
                _logger.LogError(ex, "Flush loop iteration failed");
                // Keep the loop alive — do not let a single failure kill it.
            }

            try
            {
                await Task.Delay(_options.FlushIntervalMs, _cts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void DrainDisk()
    {
        while (_spillManager.HasData && !_cts.IsCancellationRequested)
        {
            string? oldest = _spillManager.PeekOldestFile();
            if (oldest is null) break;

            int count = 0;
            foreach (var message in SpillManager.ReadFile(oldest))
            {
                Produce(message);
                _metrics.RecordReadFromDisk();
                count++;
            }

            _spillManager.DeleteFile(oldest);
            _metrics.SetDiskSpillFiles(_spillManager.FileCount);
            _metrics.SetDiskSpillBytes(_spillManager.TotalBytes);

            if (count > 0)
            {
                _logger.LogDebug(
                    "Drained {Count} messages from spill file {File}",
                    count,
                    oldest);
            }
        }
    }

    private void DrainMemory()
    {
        PooledBufferWriter[] toSend;

        lock (_sync)
        {
            if (_buffer.Count == 0)
                return;

            toSend = _buffer.ToArray();
            _buffer.Clear();
            _metrics.SetBufferSize(0);
            Monitor.PulseAll(_sync);
        }

        foreach (var writer in toSend)
        {
            try
            {
                Produce(writer.WrittenMemory);
            }
            finally
            {
                writer.Return();
            }
        }
    }

    private void Produce(ReadOnlyMemory<byte> payload)
    {
        try
        {
            var message = new Message<byte[], byte[]>
            {
                Key = null!,
                Value = payload.ToArray(),
            };

            _producer.Produce(_options.Topic, message, DeliveryHandler);
            _metrics.RecordSent();
        }
        catch (ProduceException<byte[], byte[]> ex)
        {
            _metrics.RecordError();
            _logger.LogError(
                ex,
                "Produce to {Topic} failed: {Reason}",
                _options.Topic,
                ex.Error.Reason);
            SendToDeadLetter(payload, ex.Error.Reason);
        }
    }

    private void SendToDeadLetter(ReadOnlyMemory<byte> payload, string reason)
    {
        try
        {
            var message = new Message<byte[], byte[]>
            {
                Key = null!,
                Value = payload.ToArray(),
                Headers = new Headers
                {
                    { "x-error", System.Text.Encoding.UTF8.GetBytes(reason) },
                },
            };

            _producer.Produce(_options.DeadLetterTopic, message, DeliveryHandler);
            _metrics.RecordDlq();
        }
        catch (Exception ex)
        {
            // DLQ failed too — nothing else we can do.
            _metrics.RecordError();
            _logger.LogError(
                ex,
                "Dead-letter produce to {DeadLetterTopic} failed; message lost",
                _options.DeadLetterTopic);
        }
    }

    private void DeliveryHandler(DeliveryReport<byte[], byte[]> report)
    {
        if (report.Error.IsError)
        {
            _metrics.RecordError();
            _logger.LogWarning(
                "Delivery failed: {Reason}",
                report.Error.Reason);
        }
    }
}
