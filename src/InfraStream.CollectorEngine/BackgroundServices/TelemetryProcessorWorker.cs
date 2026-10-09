using InfraStream.Core.Exporters;
using InfraStream.Core.Plugins;
using InfraStream.CollectorEngine.Ingress;
using InfraStream.CollectorEngine.Logging;
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
/// The worker drains the channel until it is completed and empty, or until
/// the shutdown timeout elapses. The hot path is clean: no <c>Stopwatch</c>,
/// no per-iteration metrics. All log messages use source-generated
/// <see cref="LoggerMessageAttribute"/> delegates, so disabling them
/// (e.g. via <c>Logging__LogLevel__InfraStream=None</c>) eliminates both
/// the console output and the argument-boxing allocations.
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
    /// Runs the worker loop until the channel is completed and drained,
    /// or until <paramref name="stoppingToken"/> is signaled.
    /// </summary>
    /// <remarks>
    /// The loop does not exit on <paramref name="stoppingToken"/> alone:
    /// remaining payloads in the channel are processed first. The token is
    /// only observed while waiting for new payloads; if it fires while the
    /// queue is non-empty, the loop continues to drain.
    /// </remarks>
    /// <param name="stoppingToken">Token used to force-stop the worker.</param>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        WorkerLog.Started(_logger, _workerIndex);

        byte[] decompressedBuffer = ArrayPool<byte>.Shared.Rent(_decompressedBufferSize);

        try
        {
            while (!_queue.IsCompleted || _queue.Count > 0)
            {
                // Wait for data, completion, or forced stop.
                if (!_queue.Reader.TryRead(out var payload))
                {
                    try
                    {
                        // WaitToReadAsync returns false when the channel is
                        // completed and empty. Cancellation forces exit only
                        // when the queue is already empty (checked above).
                        if (!await _queue.Reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
                            break;
                    }
                    catch (OperationCanceledException)
                    {
                        // Force-stop requested; exit even if data remains.
                        break;
                    }

                    continue;
                }

                ProcessPayload(payload, decompressedBuffer);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(decompressedBuffer);
            WorkerLog.Stopped(_logger, _workerIndex, BatchesProcessed, ItemsProcessed, Errors);
        }
    }

    private void ProcessPayload(TelemetryBatchPayload payload, byte[] decompressedBuffer)
    {
        try
        {
            if (payload.Length == 0)
                return;

            byte[] parseBuffer;
            int parseLength;

            if (payload.ContentEncoding == ContentEncoding.Identity)
            {
                parseBuffer = payload.Array;
                parseLength = payload.Length;
            }
            else
            {
                if (!_decoders.TryGet(payload.ContentEncoding, out var decoder))
                {
                    Interlocked.Increment(ref _errors);
                    WorkerLog.NoDecoder(_logger, _workerIndex, payload.ContentEncoding, payload.NodeId, payload.Length);
                    return;
                }

                int bytesWritten;
                try
                {
                    bytesWritten = decoder.Decode(payload.Memory, decompressedBuffer.AsSpan());
                }
                catch (InvalidDataException ex)
                {
                    Interlocked.Increment(ref _errors);
                    WorkerLog.MalformedPayload(_logger, ex, _workerIndex, payload.ContentEncoding, payload.NodeId,
                        payload.Length);
                    return;
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
            WorkerLog.BatchProcessingError(_logger, ex, _workerIndex, payload.NodeId, payload.Length);
        }
        finally
        {
            payload.Dispose();
        }
    }
}
