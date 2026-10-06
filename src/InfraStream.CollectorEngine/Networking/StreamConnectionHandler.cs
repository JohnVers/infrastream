using InfraStream.CollectorEngine.Queues;

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
/// </remarks>
public sealed class StreamConnectionHandler : ConnectionHandler
{
    private const int MaxHeaderBytes = 16 * 1024;
    private const int MaxPayloadBytes = 16 * 1024 * 1024;

    private static readonly byte[] EndOfHeaders = "\r\n\r\n"u8.ToArray();
    private static readonly byte[] PostPrefix   = "POST "u8.ToArray();

    private static readonly byte[] ClHeader      = "content-length:"u8.ToArray();
    private static readonly byte[] NodeIdHeader  = "x-node-id:"u8.ToArray();
    private static readonly byte[] EnvHeader     = "x-environment:"u8.ToArray();
    private static readonly byte[] EncodingHeader = "content-encoding:"u8.ToArray();

    private static readonly byte[] ResponseAccepted =
        "HTTP/1.1 202 Accepted\r\nContent-Length: 0\r\nConnection: keep-alive\r\n\r\n"u8.ToArray();
    private static readonly byte[] ResponseBadRequest =
        "HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray();
    private static readonly byte[] ResponsePayloadTooLarge =
        "HTTP/1.1 413 Payload Too Large\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray();

    private readonly TelemetryChannelQueue _queue;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="StreamConnectionHandler"/> class.
    /// </summary>
    /// <param name="queue">Bounded channel used to hand off payloads to workers.</param>
    public StreamConnectionHandler(TelemetryChannelQueue queue) => _queue = queue;

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
                    bool isCompressed;
                    HeaderStatus status;

                    if (!sessionParsed)
                    {
                        if (!TryParseFullHeaders(buffer, out headersLength, out contentLength,
                                out isCompressed, out nodeId, out environment, out status))
                        {
                            if (result.IsCompleted) break;
                            input.AdvanceTo(buffer.Start, buffer.End);
                            continue;
                        }
                    }
                    else
                    {
                        if (!TryParseContentLengthAndEncoding(buffer, out headersLength, out contentLength,
                                out isCompressed, out status))
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

                    if (contentLength < 0 || contentLength > MaxPayloadBytes)
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

                    // Copy the body into a pooled payload.
                    var payload = TelemetryBatchPool.Rent(contentLength);
                    payload.NodeId = nodeId;
                    payload.Environment = environment;
                    payload.IsCompressed = isCompressed;

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
    /// Parses the full set of headers for the first request of a session.
    /// </summary>
    /// <param name="buffer">Input buffer containing the request headers.</param>
    /// <param name="headersLength">
    /// Total length of the header block, including the terminating CRLFCRLF.
    /// </param>
    /// <param name="contentLength">Value of the <c>Content-Length</c> header.</param>
    /// <param name="isCompressed">
    /// <see langword="true"/> if <c>Content-Encoding</c> indicates compression.
    /// </param>
    /// <param name="nodeId">Value of the <c>X-Node-Id</c> header, or <c>unknown</c>.</param>
    /// <param name="environment">
    /// Value of the <c>X-Environment</c> header, or <c>unknown</c>.
    /// </param>
    /// <param name="status">Parsing outcome.</param>
    /// <returns>
    /// <see langword="true"/> if the header block has been fully parsed
    /// (successfully or with a failure status); <see langword="false"/> if
    /// more data is required.
    /// </returns>
    private static bool TryParseFullHeaders(
        ReadOnlySequence<byte> buffer,
        out int headersLength,
        out int contentLength,
        out bool isCompressed,
        out string nodeId,
        out string environment,
        out HeaderStatus status)
    {
        headersLength = 0;
        contentLength = -1;
        isCompressed = false;
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

        if (!ParseAllHeaderLines(headersSeq, ref contentLength, ref isCompressed, ref nodeId, ref environment))
        {
            status = HeaderStatus.Failed;
            return true;
        }

        if (contentLength < 0) { status = HeaderStatus.Failed; return true; }
        return true;
    }

    /// <summary>
    /// Parses only <c>Content-Length</c> and <c>Content-Encoding</c> for
    /// keep-alive requests. <c>NodeId</c> / <c>Environment</c> are already
    /// captured for the session.
    /// </summary>
    /// <param name="buffer">Input buffer containing the request headers.</param>
    /// <param name="headersLength">
    /// Total length of the header block, including the terminating CRLFCRLF.
    /// </param>
    /// <param name="contentLength">Value of the <c>Content-Length</c> header.</param>
    /// <param name="isCompressed">
    /// <see langword="true"/> if <c>Content-Encoding</c> indicates compression.
    /// </param>
    /// <param name="status">Parsing outcome.</param>
    /// <returns>
    /// <see langword="true"/> if the header block has been fully parsed
    /// (successfully or with a failure status); <see langword="false"/> if
    /// more data is required.
    /// </returns>
    private static bool TryParseContentLengthAndEncoding(
        ReadOnlySequence<byte> buffer,
        out int headersLength,
        out int contentLength,
        out bool isCompressed,
        out HeaderStatus status)
    {
        headersLength = 0;
        contentLength = -1;
        isCompressed = false;
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
                isCompressed = IsCompressedEncoding(scratch.Slice(0, ceLen));
                continue;
            }
        }

        if (contentLength < 0) { status = HeaderStatus.Failed; return true; }
        return true;
    }

    /// <summary>
    /// Returns <see langword="true"/> if the buffer contains the
    /// end-of-headers delimiter (<c>\r\n\r\n</c>).
    /// </summary>
    private static bool ContainsEndOfHeaders(ReadOnlySequence<byte> buffer)
    {
        var r = new SequenceReader<byte>(buffer);
        return r.TryReadTo(out ReadOnlySequence<byte> _, EndOfHeaders, advancePastDelimiter: true);
    }

    /// <summary>
    /// Iterates over individual header lines and fills the supplied
    /// references with parsed values.
    /// </summary>
    private static bool ParseAllHeaderLines(
        ReadOnlySequence<byte> headers,
        ref int contentLength,
        ref bool isCompressed,
        ref string nodeId,
        ref string environment)
    {
        var reader = new SequenceReader<byte>(headers);

        // Skip the request line ("POST /... HTTP/1.1").
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
                isCompressed = IsCompressedEncoding(scratch.Slice(0, ceLen));
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

    /// <summary>
    /// Recognizes <c>Content-Encoding: br</c>, <c>gzip</c>, or <c>deflate</c>
    /// as compression.
    /// </summary>
    private static bool IsCompressedEncoding(ReadOnlySpan<byte> value)
    {
        // "br", "gzip", "deflate" — all of them imply compression.
        // Checking the first byte is enough: 'b', 'g', 'd' (case-insensitive).
        if (value.IsEmpty) return false;
        byte b = value[0];
        if (b >= (byte)'A' && b <= (byte)'Z') b = (byte)(b + 32);
        return b == (byte)'b' || b == (byte)'g' || b == (byte)'d';
    }

    /// <summary>
    /// Case-insensitively matches <paramref name="headerName"/> at the
    /// beginning of <paramref name="line"/>, skips the following spaces,
    /// and copies the trimmed value into <paramref name="scratch"/>.
    /// </summary>
    /// <param name="line">A single header line.</param>
    /// <param name="headerName">Header name to match (lowercase).</param>
    /// <param name="scratch">Scratch buffer that receives the header value.</param>
    /// <param name="valueLength">Length of the value stored in <paramref name="scratch"/>.</param>
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

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="sequence"/> starts
    /// with <paramref name="prefix"/>. Supports prefixes up to 16 bytes.
    /// </summary>
    private static bool SequenceStartsWith(ReadOnlySequence<byte> sequence, ReadOnlySpan<byte> prefix)
    {
        if (sequence.Length < prefix.Length) return false;
        Span<byte> scratch = stackalloc byte[16];
        if (prefix.Length > scratch.Length) return false;
        sequence.Slice(0, prefix.Length).CopyTo(scratch);
        return scratch.Slice(0, prefix.Length).SequenceEqual(prefix);
    }

    /// <summary>
    /// Writes a fixed response to the output pipe and flushes it.
    /// </summary>
    private static async ValueTask WriteAsync(PipeWriter output, ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        await output.WriteAsync(bytes, ct).ConfigureAwait(false);
        await output.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Best-effort write of a fixed response followed by a flush; swallows
    /// any I/O errors because it runs on the connection-shutdown path.
    /// </summary>
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
