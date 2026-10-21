using InfraStream.CollectorEngine.Queues;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace InfraStream.CollectorEngine.Ingress;

/// <summary>
/// HTTP handler for <see cref="Protocols.Http1KestrelProtocol"/> and
/// <see cref="Protocols.Http2KestrelProtocol"/>.
/// </summary>
/// <remarks>
/// Reads the request body, validates <c>Content-Encoding</c>, copies bytes
/// into a pooled <c>TelemetryBatchPayload</c>, and enqueues it. Returns
/// <c>202 Accepted</c> on success, or one of <c>415</c>, <c>413</c>,
/// <c>400</c>, <c>503</c>.
/// </remarks>
public sealed class IngestHandler
{
    /// <summary>Path for telemetry ingest.</summary>
    public const string Path = "/api/v1/ingest";

    private readonly TelemetryChannelQueue _queue;
    private readonly ContentDecoderRegistry _decoders;
    private readonly int _maxBodySize;

    /// <summary>
    /// Initializes a new instance of the <see cref="IngestHandler"/> class.
    /// </summary>
    public IngestHandler(
        TelemetryChannelQueue queue,
        ContentDecoderRegistry decoders,
        IOptions<IngressOptions> ingressOptions)
    {
        _queue = queue;
        _decoders = decoders;
        _maxBodySize = ingressOptions.Value.MaxBodySize;
    }

    /// <summary>
    /// ASP.NET Core endpoint handler.
    /// </summary>
    public static Task<IResult> HandleAsync(
        HttpContext context,
        IngestHandler handler,
        CancellationToken ct)
        => handler.HandleCoreAsync(context, ct);

    private async Task<IResult> HandleCoreAsync(HttpContext context, CancellationToken ct)
    {
        // Content-Encoding validation.
        ContentEncoding encoding = ContentEncoding.Identity;
        var encodingHeader = context.Request.Headers.ContentEncoding;

        if (encodingHeader.Count > 0)
        {
            string? token = encodingHeader.ToString();
            if (string.IsNullOrWhiteSpace(token) || token.IndexOf(',') >= 0)
                return Results.StatusCode(StatusCodes.Status400BadRequest);

            if (!ContentEncodingExtensions.TryParseToken(token, out encoding))
                return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        if (!_decoders.TryGet(encoding, out _))
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);

        // Body size limit.
        long? contentLength = context.Request.ContentLength;
        if (contentLength is null || contentLength < 0)
            return Results.StatusCode(StatusCodes.Status400BadRequest);

        if (contentLength > _maxBodySize)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        int bodyLength = (int)contentLength.Value;

        // Shutdown check.
        if (_queue.IsCompleted)
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

        // NodeId / Environment from headers (optional).
        string nodeId = context.Request.Headers["X-Node-Id"].ToString();
        if (string.IsNullOrEmpty(nodeId)) nodeId = "unknown";

        string environment = context.Request.Headers["X-Environment"].ToString();
        if (string.IsNullOrEmpty(environment)) environment = "unknown";

        // Copy body into a pooled payload.
        var payload = TelemetryBatchPool.Rent(bodyLength);
        payload.NodeId = nodeId;
        payload.Environment = environment;
        payload.ContentEncoding = encoding;

        try
        {
            int totalRead = 0;
            while (totalRead < bodyLength)
            {
                int read = await context.Request.Body
                    .ReadAsync(payload.Array.AsMemory(totalRead, bodyLength - totalRead), ct)
                    .ConfigureAwait(false);

                if (read == 0) break;
                totalRead += read;
            }

            if (totalRead != bodyLength)
            {
                payload.Dispose();
                return Results.StatusCode(StatusCodes.Status400BadRequest);
            }

            payload.Length = bodyLength;

            // Enqueue.
            if (!_queue.Writer.TryWrite(payload))
            {
                try
                {
                    await _queue.Writer.WriteAsync(payload, ct).ConfigureAwait(false);
                }
                catch (System.Threading.Channels.ChannelClosedException)
                {
                    payload.Dispose();
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                }
                catch
                {
                    payload.Dispose();
                    throw;
                }
            }

            return Results.Accepted();
        }
        catch
        {
            payload.Dispose();
            throw;
        }
    }
}
