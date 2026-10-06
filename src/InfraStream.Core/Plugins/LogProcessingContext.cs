namespace InfraStream.Core.Plugins;

/// <summary>
/// Processing context for a single log record, passed through plugins.
/// Operates on UTF-8 bytes — no conversion to <see cref="string"/>.
/// </summary>
/// <remarks>
/// Declared as a <see langword="ref struct"/>: the context is stack-only
/// and must not outlive the synchronous call that created it.
/// </remarks>
public ref struct LogProcessingContext
{
    /// <summary>Identifier of the node that emitted the log record, UTF-8.</summary>
    public ReadOnlySpan<byte> NodeId;

    /// <summary>Environment name (e.g. <c>prod</c>, <c>staging</c>), UTF-8.</summary>
    public ReadOnlySpan<byte> Environment;

    /// <summary>
    /// Validity flag for the current log record.
    /// </summary>
    /// <remarks>
    /// If a plugin sets this to <see langword="false"/>, the log record
    /// is immediately filtered out and dropped.
    /// </remarks>
    public bool IsValid;
}
