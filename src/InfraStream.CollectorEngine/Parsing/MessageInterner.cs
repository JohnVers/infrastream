namespace InfraStream.CollectorEngine.Parsing;

/// <summary>
/// UTF-8 string interner for the parser hot path.
/// Returns <see cref="ReadOnlyMemory{T}"/> for both well-known tokens and
/// unique values.
/// </summary>
/// <remarks>
/// <para>
/// FOR WELL-KNOWN TOKENS: returns the static cache (zero allocations,
/// lives forever).
/// </para>
/// <para>
/// FOR UNIQUE VALUES: returns a slice of the source buffer (zero
/// allocations, lives until the buffer is returned to the pool).
/// </para>
/// <para>
/// IMPORTANT: unique slices point INTO the batch buffer.
/// The exporter must either consume them synchronously or copy them if it
/// retains the data longer than the batch buffer lives.
/// </para>
/// </remarks>
public static class MessageInterner
{
    // ================================================================
    //  Precomputed static tokens (UTF-8)
    //  Public — used directly by the parser.
    // ================================================================

    // Log levels
    public static readonly ReadOnlyMemory<byte> InternedInfo  = "INFO"u8.ToArray();
    public static readonly ReadOnlyMemory<byte> InternedWarn  = "WARN"u8.ToArray();
    public static readonly ReadOnlyMemory<byte> InternedError = "ERROR"u8.ToArray();
    public static readonly ReadOnlyMemory<byte> InternedDebug = "DEBUG"u8.ToArray();
    public static readonly ReadOnlyMemory<byte> InternedTrace = "TRACE"u8.ToArray();
    public static readonly ReadOnlyMemory<byte> InternedFatal = "FATAL"u8.ToArray();

    // Components
    public static readonly ReadOnlyMemory<byte> InternedNginx   = "nginx"u8.ToArray();
    public static readonly ReadOnlyMemory<byte> InternedAuth    = "auth-service"u8.ToArray();
    public static readonly ReadOnlyMemory<byte> InternedPayment = "payment-processor"u8.ToArray();

    /// <summary>
    /// Returns a <see cref="ReadOnlyMemory{T}"/> for a UTF-8 slice:
    /// <list type="bullet">
    ///   <item>If the token is well-known — returns the static cache.</item>
    ///   <item>Otherwise — returns a SLICE of the source buffer
    ///         (no allocations).</item>
    /// </list>
    /// </summary>
    /// <param name="utf8Bytes">Slice of the source UTF-8 buffer.</param>
    /// <param name="buffer">
    /// Source buffer (needed to materialize the slice as memory).
    /// </param>
    /// <param name="offset">Offset of the slice within the buffer.</param>
    /// <param name="length">Length of the slice.</param>
    public static ReadOnlyMemory<byte> AsMemory(
        ReadOnlySpan<byte> utf8Bytes,
        byte[] buffer,
        int offset,
        int length)
    {
        if (utf8Bytes.IsEmpty)
            return ReadOnlyMemory<byte>.Empty;

        // 1. Well-known log levels
        if (utf8Bytes.SequenceEqual("INFO"u8))  return InternedInfo;
        if (utf8Bytes.SequenceEqual("WARN"u8))  return InternedWarn;
        if (utf8Bytes.SequenceEqual("ERROR"u8)) return InternedError;
        if (utf8Bytes.SequenceEqual("DEBUG"u8)) return InternedDebug;
        if (utf8Bytes.SequenceEqual("TRACE"u8)) return InternedTrace;
        if (utf8Bytes.SequenceEqual("FATAL"u8)) return InternedFatal;

        // 2. Well-known components
        if (utf8Bytes.SequenceEqual("nginx"u8))             return InternedNginx;
        if (utf8Bytes.SequenceEqual("auth-service"u8))      return InternedAuth;
        if (utf8Bytes.SequenceEqual("payment-processor"u8)) return InternedPayment;

        // 3. Unique token — slice of the source buffer (zero allocations).
        return new ReadOnlyMemory<byte>(buffer, offset, length);
    }
}
