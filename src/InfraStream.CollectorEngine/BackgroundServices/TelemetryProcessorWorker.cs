using System.Text;
using InfraStream.Core.Exporters;
using InfraStream.Core.Models;
using InfraStream.Core.Plugins;
using InfraStream.CollectorEngine.Ingress;
using InfraStream.CollectorEngine.Logging;
using InfraStream.CollectorEngine.Parsing;
using InfraStream.CollectorEngine.Queues;
using InfraStream.Contracts;
using Microsoft.Extensions.Options;

namespace InfraStream.CollectorEngine.BackgroundServices;

/// <summary>
/// Telemetry processing worker.
/// One instance = one processing thread.
/// </summary>
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

    public int WorkerIndex => _workerIndex;
    public long BatchesProcessed => Interlocked.Read(ref _batchesProcessed);
    public long ItemsProcessed => Interlocked.Read(ref _itemsProcessed);
    public long Errors => Interlocked.Read(ref _errors);

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        WorkerLog.Started(_logger, _workerIndex);

        byte[] decompressedBuffer = ArrayPool<byte>.Shared.Rent(_decompressedBufferSize);

        try
        {
            while (!_queue.IsCompleted || _queue.Count > 0)
            {
                if (!_queue.Reader.TryRead(out var payload))
                {
                    try
                    {
                        if (!await _queue.Reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
                            break;
                    }
                    catch (OperationCanceledException)
                    {
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

            if (payload.ContentFormat == ContentFormat.Protobuf)
            {
                ProcessProtobufPayload(payload);
                return;
            }

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

    /// <summary>
    /// Protobuf ingress path: parses <see cref="TelemetryBatch"/> natively
    /// (no JSON step) and feeds each record to the shared dispatcher.
    /// </summary>
    /// <remarks>
    /// PII masking: <see cref="TelemetryMasker.MaskInPlace"/> is applied to
    /// the UTF-8 bytes of <c>message</c>. Note that the masker targets JSON
    /// keys (<c>"password"</c>, <c>"secret"</c>, ...) and 16-digit card
    /// sequences; plain-text patterns like <c>password=...</c> are NOT
    /// masked on the gRPC path. This is a known limitation to be addressed
    /// by passing <c>message</c> as a JSON fragment in the protobuf contract.
    /// </remarks>
    private void ProcessProtobufPayload(TelemetryBatchPayload payload)
    {
        try
        {
            var batch = TelemetryBatch.Parser.ParseFrom(payload.Span);

            var dispatcher = new EnginePipelineDispatcher(
                _processors, _exporters, payload.NodeId, payload.Environment);

            foreach (var record in batch.Payload)
            {
                byte[] levelBytes = Encoding.UTF8.GetBytes(record.Level);
                byte[] messageBytes = Encoding.UTF8.GetBytes(record.Message);
                byte[] componentBytes = Encoding.UTF8.GetBytes(record.Component);

                // Mask sensitive data in-place on the UTF-8 message bytes.
                TelemetryMasker.MaskInPlace(messageBytes);

                DateTime timestamp = DateTime.TryParse(
                    record.Timestamp,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var ts)
                    ? ts
                    : default;

                var item = new TelemetryItem(
                    timestamp,
                    levelBytes,
                    messageBytes,
                    componentBytes,
                    ReadOnlyMemory<LogAttribute>.Empty,
                    ReadOnlyMemory<byte>.Empty);

                dispatcher.OnItemParsed(in item);
            }

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
