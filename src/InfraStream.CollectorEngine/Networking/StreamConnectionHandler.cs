using System.Threading.Channels;
using InfraStream.CollectorEngine.Ingress;
using InfraStream.CollectorEngine.Queues;
using Microsoft.Extensions.Options;

namespace InfraStream.CollectorEngine.Networking;

/// <summary>
/// Kestrel <see cref="ConnectionHandler"/> that parses HTTP/1.1 requests
/// from edge nodes and pushes payloads into <see cref="TelemetryChannelQueue"/>.
/// </summary>
/// <remarks>
/// <para>
/// The handler is optimized for the ingest hot path: headers and body are
/// parsed directly from a <see cref="ReadOnlySequence{T}"/> without
/// materializing intermediate <see cref="string"/> instances, and body
/// bytes are copied straight into a pooled <c>TelemetryBatchPayload</c>.
/// </para>
/// <para>
/// Only a single <c>POST</c> is expected per session; <c>NodeId</c> and
/// <c>Environment</c> are captured from the first request's headers and
/// reused for subsequent keep-alive requests on the same connection.
/// </para>
/// <para>
/// The <c>Content-Encoding</c> header is validated against the
/// <see cref="ContentDecoderRegistry"/>: unsupported or disabled encodings
/// are rejected with <c>415 Unsupported Media Type</c> before the payload
/// is enqueued. Malformed headers (including an empty <c>Content-Encoding</c>
/// value) are rejected with <c>400 Bad Request</c>. Decoding itself happens
/// later, in the worker.
/// </para>
/// <para>
/// During graceful shutdown the queue is completed, and any new payload
/// is rejected with <c>503 Service Unavailable</c>. In-flight requests
/// that were accepted before the queue closed are still processed.
/// </para>
/// </remarks>
public sealed class StreamConnectionHandler : ConnectionHandler
{
    private const int MaxHeaderBytes = 16 * 1024;

    private static readonly byte[] EndOfHeaders = "\r\n\r\n"u8.ToArray();
    private static readonly byte[] PostPrefix   = "POST "u8.ToArray();

    private static readonly byte[] ClHeader       = "content-length:"u8.ToArray();
    private static readonly byte[] NodeIdHeader   = "x-node-id:"u8.ToArray();
    private static readonly byte[] EnvHeader      = "x-environment:"u8.ToArray();
    private static readonly byte[] EncodingHeader = "content-encoding:"u8.ToArray();

    private static readonly byte[] ResponseAccepted =
        "HTTP/1.1 202 Accepted\r\nContent-Length: 0\r\nConnection: keep-alive\r\n\r\n"u8.ToArray();
    private static readonly byte[] ResponseBadRequest =
        "HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray();
    private static readonly byte[] ResponsePayloadTooLarge =
        "HTTP/1.1 413 Payload Too Large\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray();
    private static readonly byte[] ResponseUnsupportedMediaType =
        "HTTP/1.1 415 Unsupported Media Type\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray();
    private static readonly byte[] ResponseServiceUnavailable =
        "HTTP/1.1 503 Service Unavailable\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray();

    private readonly TelemetryChannelQueue _queue;
    private readonly ContentDecoderRegistry _decoders;
    private readonly int _maxBodySize;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="StreamConnectionHandler"/> class.
    /// </summary>
    /// <param name="queue">Bounded channel used to hand off payloads to workers.</param>
    /// <param name="decoders">Registry used to validate <c>Content-Encoding</c>.</param>
    /// <param name="ingressOptions">Ingress configuration (body size limit).</param>
    public StreamConnectionHandler(
        TelemetryChannelQueue queue,
        ContentDecoderRegistry decoders,
        IOptions<IngressOptions> ingressOptions)
    {
        _queue = queue;
        _decoders = decoders;
        _maxBodySize = ingressOptions.Value.MaxBodySize;
    }

    /// <inheritdoc />
    public override async Task OnConnectedAsync(ConnectionContext connection)
    {
        var input = connection.Transport.Input;
        var output = connection.Transport.Output;
        var ct = connection.ConnectionClosed;

        string nodeId = "unknown";
        string environment = "unknown";
        bool sessionParsed = false;

        try
        {
            while (true)
            {
                var result = await input.ReadAsync(ct).ConfigureAwait(false);
                var buffer = result.Buffer;

                try
                {
                    if (buffer.IsEmpty && result.IsCompleted) break;

                    int headersLength;
                    int contentLength;
                    ContentEncoding contentEncoding;
                    EncodingParseResult encodingResult;
                    HeaderStatus status;

                    if (!sessionParsed)
                    {
                        if (!TryParseFullHeaders(buffer, out headersLength, out contentLength,
                                out contentEncoding, out encodingResult, out nodeId, out environment, out status))
                        {
                            if (result.IsCompleted) break;
                            input.AdvanceTo(buffer.Start, buffer.End);
                            continue;
                        }
                    }
                    else
                    {
                        if (!TryParseContentLengthAndEncoding(buffer, out headersLength, out contentLength,
                                out contentEncoding, out encodingResult, out status))
                        {
                            if (result.IsCompleted) break;
                            input.AdvanceTo(buffer.Start, buffer.End);
                            continue;
                        }
                    }

                    if (status == HeaderStatus.Failed)
                    {
                        await WriteAndCloseAsync(output, ResponseBadRequest, ct).ConfigureAwait(false);
                        return;
                    }

                    if (encodingResult == EncodingParseResult.Malformed)
                    {
                        await WriteAndCloseAsync(output, ResponseBadRequest, ct).ConfigureAwait(false);
                        return;
                    }

                    if (encodingResult == EncodingParseResult.Unsupported)
                    {
                        await WriteAndCloseAsync(output, ResponseUnsupportedMediaType, ct).ConfigureAwait(false);
                        return;
                    }

                    // Validate Content-Encoding against the decoder registry.
                    // The token parsed fine, but the decoder may not be
                    // registered (not implemented yet, or disabled in config).
                    if (!_decoders.TryGet(contentEncoding, out _))
                    {
                        await WriteAndCloseAsync(output, ResponseUnsupportedMediaType, ct).ConfigureAwait(false);
                        return;
                    }

                    if (contentLength < 0 || contentLength > _maxBodySize)
                    {
                        await WriteAndCloseAsync(output, ResponsePayloadTooLarge, ct).ConfigureAwait(false);
                        return;
                    }

                    sessionParsed = true;

                    long totalRequestLength = (long)headersLength + contentLength;
                    if (buffer.Length < totalRequestLength)
                    {
                        if (result.IsCompleted) break;
                        input.AdvanceTo(buffer.Start, buffer.End);
                        continue;
                    }

                    // Fast-fail if shutdown has already closed the queue.
                    // This avoids renting a payload that cannot be enqueued.
                    if (_queue.IsCompleted)
                    {
                        await WriteAndCloseAsync(output, ResponseServiceUnavailable, ct).ConfigureAwait(false);
                        return;
                    }

                    // Copy the body into a pooled payload.
                    var payload = TelemetryBatchPool.Rent(contentLength);
                    payload.NodeId = nodeId;
                    payload.Environment = environment;
                    payload.ContentEncoding = contentEncoding;

                    ReadOnlySequence<byte> bodySequence = buffer.Slice(headersLength, contentLength);
                    bodySequence.CopyTo(payload.Array.AsSpan(0, contentLength));

                    // Backpressure: try the fast path first, then fall back to
                    // an async write if the channel is currently full.
                    if (!_queue.Writer.TryWrite(payload))
                    {
                        try
                        {
                            await _queue.Writer.WriteAsync(payload, ct).ConfigureAwait(false);
                        }
                        catch (ChannelClosedException)
                        {
                            // Queue completed between the fast check and the
                            // async write: shutdown in progress.
                            payload.Dispose();
                            await WriteAndCloseAsync(output, ResponseServiceUnavailable, ct).ConfigureAwait(false);
                            return;
                        }
                        catch
                        {
                            payload.Dispose();
                            throw;
                        }
                    }

                    await WriteAsync(output, ResponseAccepted, ct).ConfigureAwait(false);

                    SequencePosition nextPosition = buffer.GetPosition(totalRequestLength);
                    input.AdvanceTo(nextPosition, nextPosition);

                    if (result.IsCompleted) break;
                }
                catch (OperationCanceledException) { return; }
                catch (ConnectionResetException)   { return; }
                catch (System.Net.Sockets.SocketException) { return; }
                catch (Exception) { return; }
            }
        }
        finally
        {
            try { await output.CompleteAsync().ConfigureAwait(false); } catch { }
            try { await input.CompleteAsync().ConfigureAwait(false); } catch { }
        }
    }

    /// <summary>
    /// Outcome of header parsing for a single request.
    /// </summary>
    private enum HeaderStatus
    {
        /// <summary>Headers were parsed successfully.</summary>
        Ok,

        /// <summary>
        /// Headers are malformed or violate protocol limits; the connection
        /// must be closed with a 400 response.
        /// </summary>
        Failed
    }

    /// <summary>
    /// Outcome of <c>Content-Encoding</c> parsing.
    /// </summary>
    private enum EncodingParseResult
    {
        /// <summary>
        /// Token is recognized, or the header is absent (which maps to
        /// <see cref="ContentEncoding.Identity"/>).
        /// </summary>
        Ok,

        /// <summary>
        /// Token is not recognized, or the header carries multiple values
        /// (e.g. <c>"gzip, br"</c>). Response: 415.
        /// </summary>
        Unsupported,

        /// <summary>
        /// Header value is empty or otherwise malformed. Response: 400.
        /// </summary>
        Malformed
    }

    private static bool TryParseFullHeaders(
        ReadOnlySequence<byte> buffer,
        out int headersLength,
        out int contentLength,
        out ContentEncoding contentEncoding,
        out EncodingParseResult encodingResult,
        out string nodeId,
        out string environment,
        out HeaderStatus status)
    {
        headersLength = 0;
        contentLength = -1;
        contentEncoding = ContentEncoding.Identity;
        encodingResult = EncodingParseResult.Ok;
        nodeId = "unknown";
        environment = "unknown";
        status = HeaderStatus.Ok;

        if (buffer.Length > MaxHeaderBytes && !ContainsEndOfHeaders(buffer))
        {
            status = HeaderStatus.Failed;
            return true;
        }

        var reader = new SequenceReader<byte>(buffer);

        if (!reader.TryReadTo(out ReadOnlySequence<byte> _, EndOfHeaders, advancePastDelimiter: true))
            return false;

        headersLength = (int)reader.Consumed;
        ReadOnlySequence<byte> headersSeq = buffer.Slice(0, headersLength);

        if (!SequenceStartsWith(headersSeq, PostPrefix))
        {
            status = HeaderStatus.Failed;
            return true;
        }

        if (!ParseAllHeaderLines(headersSeq, ref contentLength, ref contentEncoding,
                ref encodingResult, ref nodeId, ref environment))
        {
            status = HeaderStatus.Failed;
            return true;
        }

        if (contentLength < 0) { status = HeaderStatus.Failed; return true; }
        return true;
    }

    private static bool TryParseContentLengthAndEncoding(
        ReadOnlySequence<byte> buffer,
        out int headersLength,
        out int contentLength,
        out ContentEncoding contentEncoding,
        out EncodingParseResult encodingResult,
        out HeaderStatus status)
    {
        headersLength = 0;
        contentLength = -1;
        contentEncoding = ContentEncoding.Identity;
        encodingResult = EncodingParseResult.Ok;
        status = HeaderStatus.Ok;

        if (buffer.Length > MaxHeaderBytes && !ContainsEndOfHeaders(buffer))
        {
            status = HeaderStatus.Failed;
            return true;
        }

        var reader = new SequenceReader<byte>(buffer);

        if (!reader.TryReadTo(out ReadOnlySequence<byte> _, EndOfHeaders, advancePastDelimiter: true))
            return false;

        headersLength = (int)reader.Consumed;
        ReadOnlySequence<byte> headersSeq = buffer.Slice(0, headersLength);

        if (!SequenceStartsWith(headersSeq, PostPrefix))
        {
            status = HeaderStatus.Failed;
            return true;
        }

        var lineReader = new SequenceReader<byte>(headersSeq);
        if (!lineReader.TryReadTo(out ReadOnlySequence<byte> _, "\r\n"u8, advancePastDelimiter: true))
        {
            status = HeaderStatus.Failed;
            return true;
        }

        Span<byte> scratch = stackalloc byte[256];

        while (lineReader.TryReadTo(out ReadOnlySequence<byte> line, "\r\n"u8, advancePastDelimiter: true))
        {
            if (line.IsEmpty) break;

            if (TryMatchHeader(line, ClHeader, scratch, out int clLen))
            {
                if (contentLength >= 0) { status = HeaderStatus.Failed; return true; }
                if (!TryParseInt(scratch.Slice(0, clLen), out contentLength))
                {
                    status = HeaderStatus.Failed;
                    return true;
                }
                continue;
            }

            if (TryMatchHeader(line, EncodingHeader, scratch, out int ceLen))
            {
                encodingResult = TryParseContentEncoding(scratch.Slice(0, ceLen), out contentEncoding);
                if (encodingResult == EncodingParseResult.Malformed)
                {
                    status = HeaderStatus.Failed;
                    return true;
                }
                continue;
            }
        }

        if (contentLength < 0) { status = HeaderStatus.Failed; return true; }
        return true;
    }

    private static bool ContainsEndOfHeaders(ReadOnlySequence<byte> buffer)
    {
        var r = new SequenceReader<byte>(buffer);
        return r.TryReadTo(out ReadOnlySequence<byte> _, EndOfHeaders, advancePastDelimiter: true);
    }

    private static bool ParseAllHeaderLines(
        ReadOnlySequence<byte> headers,
        ref int contentLength,
        ref ContentEncoding contentEncoding,
        ref EncodingParseResult encodingResult,
        ref string nodeId,
        ref string environment)
    {
        var reader = new SequenceReader<byte>(headers);

        if (!reader.TryReadTo(out ReadOnlySequence<byte> _, "\r\n"u8, advancePastDelimiter: true))
            return false;

        Span<byte> scratch = stackalloc byte[256];

        while (reader.TryReadTo(out ReadOnlySequence<byte> line, "\r\n"u8, advancePastDelimiter: true))
        {
            if (line.IsEmpty) break;

            if (TryMatchHeader(line, ClHeader, scratch, out int clLen))
            {
                if (contentLength >= 0) return false;
                if (!TryParseInt(scratch.Slice(0, clLen), out contentLength)) return false;
                continue;
            }

            if (TryMatchHeader(line, EncodingHeader, scratch, out int ceLen))
            {
                encodingResult = TryParseContentEncoding(scratch.Slice(0, ceLen), out contentEncoding);
                if (encodingResult == EncodingParseResult.Malformed)
                    return false;
                continue;
            }

            if (TryMatchHeader(line, NodeIdHeader, scratch, out int nodeLen))
            {
                nodeId = Encoding.UTF8.GetString(scratch.Slice(0, nodeLen));
                continue;
            }

            if (TryMatchHeader(line, EnvHeader, scratch, out int envLen))
            {
                environment = Encoding.UTF8.GetString(scratch.Slice(0, envLen));
                continue;
            }
        }

        return true;
    }

    private static EncodingParseResult TryParseContentEncoding(
        ReadOnlySpan<byte> value,
        out ContentEncoding encoding)
    {
        encoding = ContentEncoding.Identity;

        if (value.IsEmpty)
            return EncodingParseResult.Malformed;

        string token = Encoding.ASCII.GetString(value).Trim();

        if (token.Length == 0)
            return EncodingParseResult.Malformed;

        if (token.IndexOf(',') >= 0)
            return EncodingParseResult.Unsupported;

        return ContentEncodingExtensions.TryParseToken(token, out encoding)
            ? EncodingParseResult.Ok
            : EncodingParseResult.Unsupported;
    }

    private static bool TryMatchHeader(
        ReadOnlySequence<byte> line,
        ReadOnlySpan<byte> headerName,
        Span<byte> scratch,
        out int valueLength)
    {
        valueLength = 0;
        if (line.Length < headerName.Length) return false;

        int len = (int)Math.Min(line.Length, scratch.Length);
        line.Slice(0, len).CopyTo(scratch);

        ReadOnlySpan<byte> lineBytes = scratch.Slice(0, len);

        for (int i = 0; i < headerName.Length; i++)
        {
            byte a = ToLower(lineBytes[i]);
            byte b = headerName[i];
            if (a != b) return false;
        }

        int pos = headerName.Length;
        while (pos < lineBytes.Length && lineBytes[pos] == (byte)' ') pos++;

        if ((int)line.Length > scratch.Length) return false;

        int valueLen = len - pos;
        if (pos > 0 && valueLen > 0)
            scratch.Slice(pos, valueLen).CopyTo(scratch);

        valueLength = valueLen;
        return true;
    }

    private static byte ToLower(byte b) => (b >= (byte)'A' && b <= (byte)'Z') ? (byte)(b + 32) : b;

    private static bool TryParseInt(ReadOnlySpan<byte> span, out int value)
    {
        value = 0;
        if (span.IsEmpty) return false;

        long acc = 0;
        for (int i = 0; i < span.Length; i++)
        {
            byte b = span[i];
            if (b < (byte)'0' || b > (byte)'9') return false;
            acc = acc * 10 + (b - (byte)'0');
            if (acc > int.MaxValue) return false;
        }
        value = (int)acc;
        return true;
    }

    private static bool SequenceStartsWith(ReadOnlySequence<byte> sequence, ReadOnlySpan<byte> prefix)
    {
        if (sequence.Length < prefix.Length) return false;
        Span<byte> scratch = stackalloc byte[16];
        if (prefix.Length > scratch.Length) return false;
        sequence.Slice(0, prefix.Length).CopyTo(scratch);
        return scratch.Slice(0, prefix.Length).SequenceEqual(prefix);
    }

    private static async ValueTask WriteAsync(PipeWriter output, ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        await output.WriteAsync(bytes, ct).ConfigureAwait(false);
        await output.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async ValueTask WriteAndCloseAsync(PipeWriter output, ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        try
        {
            await output.WriteAsync(bytes, ct).ConfigureAwait(false);
            await output.FlushAsync(ct).ConfigureAwait(false);
        }
        catch { }
    }
}
