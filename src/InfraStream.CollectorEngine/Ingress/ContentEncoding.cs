namespace InfraStream.CollectorEngine.Ingress;

/// <summary>
/// Content encoding of a telemetry payload, as declared by the client
/// in the <c>Content-Encoding</c> header.
/// </summary>
/// <remarks>
/// <see cref="Identity"/> means the payload is uncompressed.
/// The registry resolves a decoder by <see cref="ContentEncoding"/> and
/// the worker uses it to decompress the payload before parsing.
/// </remarks>
public enum ContentEncoding
{
    /// <summary>Payload is not compressed.</summary>
    Identity = 0,

    /// <summary>Payload is compressed with Brotli (RFC 7932).</summary>
    Brotli = 1,

    /// <summary>Payload is compressed with gzip (RFC 1952).</summary>
    Gzip = 2,

    /// <summary>Payload is compressed with zstd (RFC 8878).</summary>
    Zstd = 3
}
