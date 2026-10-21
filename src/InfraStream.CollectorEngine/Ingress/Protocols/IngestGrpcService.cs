using Google.Protobuf;
using Grpc.Core;
using InfraStream.CollectorEngine.Ingress;
using InfraStream.CollectorEngine.Queues;
using InfraStream.Contracts;
using Microsoft.Extensions.Logging;

namespace InfraStream.CollectorEngine.Ingress.Protocols;

/// <summary>
/// gRPC service that accepts telemetry batches and enqueues them
/// into the shared processing pipeline as native protobuf bytes.
/// </summary>
public sealed class IngestGrpcService : Ingest.IngestBase
{
    private readonly TelemetryChannelQueue _queue;
    private readonly ILogger<IngestGrpcService> _logger;

    public IngestGrpcService(
        TelemetryChannelQueue queue,
        ILogger<IngestGrpcService> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    /// <inheritdoc />
    public override async Task<IngestReply> SendBatch(
        TelemetryBatch request,
        ServerCallContext context)
    {
        // Google.Protobuf 3.30.0: IMessage.WriteTo(Span<byte>) is not available.
        // Use ToByteArray() + CopyTo; optimize later via CodedOutputStream
        // or upgrade to a newer Google.Protobuf with span-based WriteTo.
        var bytes = request.ToByteArray();
        var payload = TelemetryBatchPool.Rent(bytes.Length);

        try
        {
            bytes.CopyTo(payload.Array, 0);
            payload.Length = bytes.Length;
            payload.ContentFormat = ContentFormat.Protobuf;
            payload.ContentEncoding = ContentEncoding.Identity;
            payload.NodeId = request.NodeId;
            payload.Environment = request.Environment;

            await _queue.Writer.WriteAsync(payload, context.CancellationToken);
        }
        catch
        {
            payload.Dispose();
            throw;
        }

        return new IngestReply { Accepted = request.Payload.Count };
    }
}
