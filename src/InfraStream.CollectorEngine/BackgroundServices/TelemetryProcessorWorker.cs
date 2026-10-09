using InfraStream.Core.Exporters;
using InfraStream.Core.Plugins;
using InfraStream.CollectorEngine.Ingress;
using InfraStream.CollectorEngine.Parsing;
using InfraStream.CollectorEngine.Queues;
using Microsoft.Extensions.Options;

namespace InfraStream.CollectorEngine.BackgroundServices;

/// <summary>
/// Telemetry processing worker.
/// One instance = one processing thread.
/// </summary>
/// <remarks>
/// <para>
/// Responsible for:
/// <list type="number">
///   <item>Decoding the batch payload via <see cref="IContentDecoder"/>
///         (skipped when <see cref="ContentEncoding.Identity"/>).</item>
///   <item>JSON parsing (with selective in-place masking).</item>
///   <item>Dispatch to <c>ITelemetryProcessor[]</c> and
///         <c>ITelemetryExporter[]</c>.</item>
/// </list>
/// </para>
/// <para>
/// Counters are updated with <see cref="Interlocked"/> on the hot path
/// (two atomic operations per batch, negligible at expected rates) and
/// are read by <see cref="WorkerMetricsReporter"/>. No per-iteration
/// metrics are written to the log; only the periodic reporter and the
/// final "stopped" line emit data.
/// </para>
/// </remarks>
public sealed class TelemetryProcessorWorker
{
    private readonly TelemetryChannelQueue _queue;
    private readonly ITelemetryProcessor[] _processors;
    private readonly ITelemetryExporter[] _exporters;
    private readonly ContentDecoderRegistry _decoders;
    private readonly ILogger _logger;
    private readonly int _workerIndex;
    private readonly int _decompressedBufferSize;

    private long _batchesProcessed;
    private long _itemsProcessed;
    private long _errors;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="TelemetryProcessorWorker"/> class.
    /// </summary>
    /// <param name="queue">Shared bounded channel with incoming payloads.</param>
    /// <param name="processors">Processors applied to every parsed item.</param>
    /// <param name="exporters">Exporters that receive accepted items.</param>
    /// <param name="decoders">Registry of content decoders.</param>
    /// <param name="ingressOptions">Ingress configuration (buffer size).</param>
    /// <param name="logger">Logger used for lifecycle and error events.</param>
    /// <param name="workerIndex">
    /// Zero-based index of this worker, used for log correlation.
    /// </param>
    public TelemetryProcessorWorker(
        TelemetryChannelQueue queue,
        ITelemetryProcessor[] processors,
        ITelemetryExporter[] exporters,
        ContentDecoderRegistry decoders,
        IOptions<IngressOptions> ingressOptions,
        ILogger logger,
        int workerIndex)
    {
        _queue = queue;
        _processors = processors;
        _exporters = exporters;
        _decoders = decoders;
        _logger = logger;
        _workerIndex = workerIndex;
        _decompressedBufferSize = ingressOptions.Value.DecompressedBufferSize;
    }

    /// <summary>Zero-based index of this worker.</summary>
    public int WorkerIndex => _workerIndex;

    /// <summary>Total number of batches successfully processed.</summary>
    public long BatchesProcessed => Interlocked.Read(ref _batchesProcessed);

    /// <summary>Total number of telemetry items parsed and dispatched.</summary>
    public long ItemsProcessed => Interlocked.Read(ref _itemsProcessed);

    /// <summary>Total number of batch-level errors.</summary>
    public long Errors => Interlocked.Read(ref _errors);

    /// <summary>
    /// Runs the worker loop until <paramref name="stoppingToken"/> is
    /// signaled.
    /// </summary>
    /// <param name="stoppingToken">Token used to stop the worker.</param>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker {Index} started.", _workerIndex);

        byte[] decompressedBuffer = ArrayPool<byte>.Shared.Rent(_decompressedBufferSize);

        try
        {
            await foreach (var payload in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    if (payload.Length == 0)
                        continue;

                    byte[] parseBuffer;
                    int parseLength;

                    if (payload.ContentEncoding == ContentEncoding.Identity)
                    {
                        // Uncompressed: parse in place.
                        parseBuffer = payload.Array;
                        parseLength = payload.Length;
                    }
                    else
                    {
                        if (!_decoders.TryGet(payload.ContentEncoding, out var decoder))
                        {
                            // Should be unreachable: the handler rejects
                            // unsupported encodings with 415 before enqueue.
                            Interlocked.Increment(ref _errors);
                            _logger.LogWarning(
                                "Worker {Index}: no decoder for encoding {Encoding}. NodeId={NodeId}, Len={Len}",
                                _workerIndex, payload.ContentEncoding, payload.NodeId, payload.Length);
                            continue;
                        }

                        int bytesWritten;
                        try
                        {
                            bytesWritten = decoder.Decode(payload.Memory, decompressedBuffer.AsSpan());
                        }
                        catch (InvalidDataException ex)
                        {
                            Interlocked.Increment(ref _errors);
                            _logger.LogWarning(ex,
                                "Worker {Index}: malformed payload. Encoding={Encoding}, NodeId={NodeId}, Len={Len}",
                                _workerIndex, payload.ContentEncoding, payload.NodeId, payload.Length);
                            continue;
                        }

                        parseBuffer = decompressedBuffer;
                        parseLength = bytesWritten;
                    }

                    var dispatcher = new EnginePipelineDispatcher(
                        _processors, _exporters, payload.NodeId, payload.Environment);

                    TelemetryBatchParser.Parse(
                        parseBuffer,
                        parseLength,
                        ref dispatcher);

                    Interlocked.Add(ref _itemsProcessed, dispatcher.Count);
                    Interlocked.Increment(ref _batchesProcessed);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _errors);
                    _logger.LogError(ex,
                        "Worker {Index}: error processing batch. NodeId={NodeId}, Len={Len}",
                        _workerIndex, payload.NodeId, payload.Length);
                }
                finally
                {
                    payload.Dispose();
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            ArrayPool<byte>.Shared.Return(decompressedBuffer);

            _logger.LogInformation(
                "Worker {Index} stopped. Batches={Batches}, Items={Items}, Errors={Errors}",
                _workerIndex,
                BatchesProcessed,
                ItemsProcessed,
                Errors);
        }
    }
}
