namespace InfraStream.Core.Models;

/// <summary>
/// Allocation-free struct representing a custom log attribute (key-value pair).
/// </summary>
/// <remarks>
/// <para>
/// Stores UTF-8 bytes — no conversion to <see cref="string"/>.
/// The exporter decides when to convert to a string (or to write the
/// bytes directly).
/// </para>
/// <para>
/// IMPORTANT: <see cref="Key"/> and <see cref="Value"/> point into the
/// batch buffer (or into the interned static cache). As long as the
/// batch is alive, the data remains accessible. The exporter must
/// either consume them synchronously or copy them.
/// </para>
/// </remarks>
public readonly struct LogAttribute
{
    /// <summary>Attribute key, UTF-8.</summary>
    public ReadOnlyMemory<byte> Key { get; }

    /// <summary>Attribute value, UTF-8.</summary>
    public ReadOnlyMemory<byte> Value { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LogAttribute"/> struct.
    /// </summary>
    /// <param name="key">Attribute key, UTF-8.</param>
    /// <param name="value">Attribute value, UTF-8.</param>
    public LogAttribute(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value)
    {
        Key = key;
        Value = value;
    }
}
