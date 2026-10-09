namespace InfraStream.CollectorEngine.Ingress;

/// <summary>
/// Resolves an <see cref="IContentDecoder"/> by
/// <see cref="ContentEncoding"/> or by its HTTP token.
/// </summary>
/// <remarks>
/// Instances are created from the set of registered
/// <see cref="IContentDecoder"/> services. Decoders that are disabled
/// in configuration are not registered and therefore not resolvable.
/// Lookups are case-insensitive for tokens.
/// </remarks>
public sealed class ContentDecoderRegistry
{
    private readonly Dictionary<string, IContentDecoder> _byToken;

    /// <summary>
    /// Initializes a new registry from the supplied decoders.
    /// </summary>
    /// <param name="decoders">Decoders to register.</param>
    /// <exception cref="ArgumentException">
    /// Thrown if two decoders handle the same <see cref="ContentEncoding"/>.
    /// </exception>
    public ContentDecoderRegistry(IEnumerable<IContentDecoder> decoders)
    {
        ArgumentNullException.ThrowIfNull(decoders);

        _byToken = new Dictionary<string, IContentDecoder>(StringComparer.OrdinalIgnoreCase);

        foreach (var decoder in decoders)
        {
            string token = decoder.Encoding.ToToken();
            if (!_byToken.TryAdd(token, decoder))
                throw new ArgumentException(
                    $"Duplicate content decoder for encoding '{token}'.",
                    nameof(decoders));
        }
    }

    /// <summary>
    /// Returns the registered encodings (as HTTP tokens).
    /// </summary>
    public IReadOnlyCollection<string> SupportedTokens => _byToken.Keys;

    /// <summary>
    /// Tries to resolve a decoder by its HTTP token.
    /// </summary>
    /// <param name="token">HTTP <c>Content-Encoding</c> token.</param>
    /// <param name="decoder">Resolved decoder, if found.</param>
    /// <returns><see langword="true"/> if a decoder was found.</returns>
    public bool TryGet(string? token, out IContentDecoder decoder)
    {
        if (string.IsNullOrEmpty(token))
        {
            token = ContentEncoding.Identity.ToToken();
        }

        return _byToken.TryGetValue(token, out decoder!);
    }

    /// <summary>
    /// Tries to resolve a decoder by <see cref="ContentEncoding"/>.
    /// </summary>
    /// <param name="encoding">Content encoding.</param>
    /// <param name="decoder">Resolved decoder, if found.</param>
    /// <returns><see langword="true"/> if a decoder was found.</returns>
    public bool TryGet(ContentEncoding encoding, out IContentDecoder decoder)
        => _byToken.TryGetValue(encoding.ToToken(), out decoder!);

    /// <summary>
    /// Returns <see langword="true"/> if a decoder is registered for
    /// <paramref name="token"/>.
    /// </summary>
    public bool IsSupported(string? token) => TryGet(token, out _);
}
