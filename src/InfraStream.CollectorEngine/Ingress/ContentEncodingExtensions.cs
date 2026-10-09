namespace InfraStream.CollectorEngine.Ingress;

/// <summary>
/// Conversions between <see cref="ContentEncoding"/> and the HTTP
/// <c>Content-Encoding</c> token.
/// </summary>
public static class ContentEncodingExtensions
{
    /// <summary>
    /// Returns the canonical HTTP token for <paramref name="encoding"/>
    /// (<c>identity</c>, <c>br</c>, <c>gzip</c>, <c>zstd</c>).
    /// </summary>
    public static string ToToken(this ContentEncoding encoding) => encoding switch
    {
        ContentEncoding.Identity => "identity",
        ContentEncoding.Brotli   => "br",
        ContentEncoding.Gzip     => "gzip",
        ContentEncoding.Zstd     => "zstd",
        _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Unknown content encoding.")
    };

    /// <summary>
    /// Tries to parse an HTTP <c>Content-Encoding</c> token into a
    /// <see cref="ContentEncoding"/>. Case-insensitive.
    /// </summary>
    /// <param name="token">The header value (may be null, empty, or whitespace).</param>
    /// <param name="encoding">Parsed encoding, or <see cref="ContentEncoding.Identity"/> for empty input.</param>
    /// <returns>
    /// <see langword="true"/> if the token is recognized (including empty,
    /// which maps to <see cref="ContentEncoding.Identity"/>) or the token is
    /// the canonical <c>identity</c>. <see langword="false"/> for unknown tokens.
    /// </returns>
    public static bool TryParseToken(string? token, out ContentEncoding encoding)
    {
        encoding = ContentEncoding.Identity;

        if (string.IsNullOrWhiteSpace(token))
            return true;

        // HTTP allows surrounding whitespace; normalize once.
        ReadOnlySpan<char> value = token.AsSpan().Trim();

        // Reject multiple encodings (e.g. "gzip, br"): not supported.
        if (value.IndexOf(',') >= 0)
            return false;

        if (value.Equals("identity", StringComparison.OrdinalIgnoreCase))
        {
            encoding = ContentEncoding.Identity;
            return true;
        }

        if (value.Equals("br", StringComparison.OrdinalIgnoreCase))
        {
            encoding = ContentEncoding.Brotli;
            return true;
        }

        if (value.Equals("gzip", StringComparison.OrdinalIgnoreCase))
        {
            encoding = ContentEncoding.Gzip;
            return true;
        }

        if (value.Equals("zstd", StringComparison.OrdinalIgnoreCase))
        {
            encoding = ContentEncoding.Zstd;
            return true;
        }

        return false;
    }
}
