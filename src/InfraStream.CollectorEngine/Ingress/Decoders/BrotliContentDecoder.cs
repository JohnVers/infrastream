namespace InfraStream.CollectorEngine.Ingress.Decoders;

/// <summary>
/// Brotli decoder (<c>Content-Encoding: br</c>, RFC 7932).
/// </summary>
public sealed class BrotliContentDecoder : IContentDecoder
{
    /// <inheritdoc />
    public ContentEncoding Encoding => ContentEncoding.Brotli;

    /// <inheritdoc />
    public int Decode(ReadOnlyMemory<byte> input, Span<byte> destination)
    {
        if (input.IsEmpty)
        {
            throw new InvalidDataException("Brotli input is empty.");
        }

        bool success = BrotliDecoder.TryDecompress(input.Span, destination, out int bytesWritten);

        if (!success)
        {
            throw new InvalidDataException(
                $"Brotli decompression failed: input={input.Length}, destination={destination.Length}.");
        }

        return bytesWritten;
    }
}
