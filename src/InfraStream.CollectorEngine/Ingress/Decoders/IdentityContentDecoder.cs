namespace InfraStream.CollectorEngine.Ingress.Decoders;

/// <summary>
/// Passthrough decoder for uncompressed payloads.
/// </summary>
public sealed class IdentityContentDecoder : IContentDecoder
{
    /// <inheritdoc />
    public ContentEncoding Encoding => ContentEncoding.Identity;

    /// <inheritdoc />
    public int Decode(ReadOnlyMemory<byte> input, Span<byte> destination)
    {
        if (input.Length > destination.Length)
        {
            throw new InvalidDataException(
                $"Destination buffer is too small: input={input.Length}, destination={destination.Length}.");
        }

        input.Span.CopyTo(destination);
        return input.Length;
    }
}
