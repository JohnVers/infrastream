namespace InfraStream.CollectorEngine.Ingress;

/// <summary>
/// Configuration for the ingress pipeline, bound from
/// the <c>InfraStream:Ingress</c> configuration section.
/// </summary>
public sealed class IngressOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "InfraStream:Ingress";

    /// <summary>
    /// Maximum size of the compressed request body, in bytes.
    /// Requests larger than this are rejected before decoding with
    /// <c>413 Payload Too Large</c>.
    /// </summary>
    public int MaxBodySize { get; set; } = 1 * 1024 * 1024;

    /// <summary>
    /// Maximum size of the decompressed payload, in bytes.
    /// Decoders must not write more than this into the destination
    /// buffer; exceeding this limit is treated as a batch error.
    /// </summary>
    public int DecompressedBufferSize { get; set; } = 16 * 1024 * 1024;

    /// <summary>
    /// Per-decoder configuration.
    /// </summary>
    public ContentDecoderOptions ContentDecoders { get; set; } = new();
}

/// <summary>
/// Per-decoder configuration. Each decoder can be enabled or disabled
/// independently; disabled decoders are not registered and their
/// <c>Content-Encoding</c> tokens are rejected with <c>415</c>.
/// </summary>
public sealed class ContentDecoderOptions
{
    /// <summary>Configuration for the identity (passthrough) decoder.</summary>
    public IdentityDecoderOptions Identity { get; set; } = new();

    /// <summary>Configuration for the Brotli decoder.</summary>
    public BrotliDecoderOptions Brotli { get; set; } = new();

    /// <summary>Configuration for the gzip decoder.</summary>
    public GzipDecoderOptions Gzip { get; set; } = new();

    /// <summary>Configuration for the zstd decoder.</summary>
    public ZstdDecoderOptions Zstd { get; set; } = new();
}

/// <summary>Configuration for the identity (passthrough) decoder.</summary>
public sealed class IdentityDecoderOptions
{
    /// <summary>Enables the identity decoder for raw (uncompressed) bodies.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>Configuration for the Brotli decoder.</summary>
public sealed class BrotliDecoderOptions
{
    /// <summary>Enables the Brotli decoder.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Reserved for future encoder support. Decoding ignores this value.
    /// </summary>
    public int Quality { get; set; }
}

/// <summary>Configuration for the gzip decoder.</summary>
public sealed class GzipDecoderOptions
{
    /// <summary>Enables the gzip decoder.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>Configuration for the zstd decoder.</summary>
public sealed class ZstdDecoderOptions
{
    /// <summary>Enables the zstd decoder. Off by default.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Reserved for future encoder support. Decoding ignores this value.
    /// </summary>
    public int Level { get; set; }
}
