using InfraStream.Core.Exporters;
using InfraStream.Core.Plugins;
using InfraStream.CollectorEngine.Parsing;
using InfraStream.CollectorEngine.Queues;

namespace InfraStream.CollectorEngine.BackgroundServices;

/// <summary>
/// Telemetry processing worker.
/// One instance = one processing thread.
/// </summary>
/// <remarks>
/// <para>
/// Responsible for:
/// <list type="number">
///   <item>Brotli decompression of the batch (if <c>IsCompressed</c>).</item>
///   <item>JSON parsing (with selective in-place masking).</item>
///   <item>Dispatch to <c>ITelemetryProcessor[]</c> and
///         <c>ITelemetryExporter[]</c>.</item>
/// </list>
/// </para>
/// <para>
/// The hot path is clean: no <c>Stopwatch</c>, no <c>Process</c>,
/// no per-iteration metrics. Final metrics (batches, items, errors)
/// are logged on stop. Uptime and rate come from host metrics
/// (Prometheus / OpenTelemetry).
/// </para>
/// </remarks>
public sealed class TelemetryProcessorWorker
{
    private const int DecompressedBufferSize = 16 * 1024 * 1024;

    private readonly TelemetryChannelQueue _queue;
    private readonly ITelemetryProcessor[] _processors;
    private readonly ITelemetryExporter[] _exporters;
    private readonly ILogger _logger;
    private readonly int _workerIndex;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="TelemetryProcessorWorker"/> class.
    /// </summary>
    /// <param name="queue">Shared bounded channel with incoming payloads.</param>
    /// <param name="processors">Processors applied to every parsed item.</param>
    /// <param name="exporters">Exporters that receive accepted items.</param>
    /// <param name="logger">Logger used for lifecycle and error events.</param>
    /// <param name="workerIndex">
    /// Zero-based index of this worker, used for log correlation.
    /// </param>
    public TelemetryProcessorWorker(
        TelemetryChannelQueue queue,
        ITelemetryProcessor[] processors,
        ITelemetryExporter[] exporters,
        ILogger logger,
        int workerIndex)
    {
        _queue = queue;
        _processors = processors;
        _exporters = exporters;
        _logger = logger;
        _workerIndex = workerIndex;
    }

    /// <summary>
    /// Runs the worker loop until <paramref name="stoppingToken"/> is
    /// signaled.
    /// </summary>
    /// <param name="stoppingToken">Token used to stop the worker.</param>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker {Index} started.", _workerIndex);

        byte[] decompressedBuffer = ArrayPool<byte>.Shared.Rent(DecompressedBufferSize);

        long totalItemsProcessed = 0;
        long totalBatchesProcessed = 0;
        long totalErrors = 0;

        try
        {
            await foreach (var payload in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    if (payload.Length == 0)
                        continue;

                    ReadOnlySpan<byte> source = payload.Span;
                    Span<byte> target = decompressedBuffer.AsSpan();

                    byte[] parseBuffer;
                    int parseLength;

                    if (payload.IsCompressed)
                    {
                        if (!BrotliDecoder.TryDecompress(source, target, out int bytesWritten))
                        {
                            // Fallback: try to parse it as raw JSON.
                            var fallbackDispatcher = new EnginePipelineDispatcher(
                                _processors, _exporters, payload.NodeId, payload.Environment);

                            TelemetryBatchParser.Parse(
                                payload.Array,
                                payload.Length,
                                ref fallbackDispatcher);

                            totalItemsProcessed += fallbackDispatcher.Count;
                            totalBatchesProcessed++;
                            continue;
                        }

                        parseBuffer = decompressedBuffer;
                        parseLength = bytesWritten;
                    }
                    else
                    {
                        parseBuffer = payload.Array;
                        parseLength = payload.Length;
                    }

                    var dispatcher = new EnginePipelineDispatcher(
                        _processors, _exporters, payload.NodeId, payload.Environment);

                    TelemetryBatchParser.Parse(
                        parseBuffer,
                        parseLength,
                        ref dispatcher);

                    totalItemsProcessed += dispatcher.Count;
                    totalBatchesProcessed++;
                }
                catch (Exception ex)
                {
                    totalErrors++;
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
                totalBatchesProcessed,
                totalItemsProcessed,
                totalErrors);
        }
    }
}
