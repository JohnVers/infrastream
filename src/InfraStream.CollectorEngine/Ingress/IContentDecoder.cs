namespace InfraStream.CollectorEngine.Ingress;

/// <summary>
/// Decodes a telemetry payload from a specific <see cref="ContentEncoding"/>
/// into a caller-provided destination buffer.
/// </summary>
/// <remarks>
/// Implementations must be stateless and thread-safe: a single instance
/// may be called concurrently by multiple workers.
/// Decoding is synchronous and allocation-free on the hot path.
/// </remarks>
public interface IContentDecoder
{
    /// <summary>
    /// Content encoding handled by this decoder.
    /// </summary>
    ContentEncoding Encoding { get; }

    /// <summary>
    /// Decodes <paramref name="input"/> into <paramref name="destination"/>.
    /// </summary>
    /// <param name="input">
    /// Compressed input bytes. In the current pipeline this is always
    /// backed by a pooled array (the telemetry batch payload).
    /// </param>
    /// <param name="destination">
    /// Destination buffer. Must be large enough to hold the decompressed
    /// payload. Implementations must not write beyond this span.
    /// </param>
    /// <returns>Number of bytes written to <paramref name="destination"/>.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when <paramref name="input"/> is malformed or when the
    /// decompressed output does not fit into <paramref name="destination"/>.
    /// </exception>
    int Decode(ReadOnlyMemory<byte> input, Span<byte> destination);
}
