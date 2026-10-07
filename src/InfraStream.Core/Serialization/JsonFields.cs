namespace InfraStream.Core.Serialization;

/// <summary>
/// JSON field names used by the telemetry serializers.
/// </summary>
/// <remarks>
/// <para>
/// These names form the JSON contract with downstream consumers
/// (ClickHouse, Elasticsearch, custom consumers). They MUST remain
/// stable across releases — changing them breaks downstream.
/// </para>
/// <para>
/// Kept as <c>const string</c> rather than <c>nameof(...)</c> so that:
/// <list type="bullet">
///   <item>The JSON contract does not depend on C# property names.</item>
///   <item>No allocation occurs (unlike <c>nameof(...).ToLower()</c>).</item>
///   <item>Typos are caught at compile time.</item>
/// </list>
/// </para>
/// </remarks>
internal static class JsonFields
{
    /// <summary>Timestamp of the log event.</summary>
    public const string Timestamp = "timestamp";

    /// <summary>Log level (e.g. INFO, WARN, ERROR).</summary>
    public const string Level = "level";

    /// <summary>Component or service name that emitted the log.</summary>
    public const string Component = "component";

    /// <summary>Log message (may be PII-masked).</summary>
    public const string Message = "message";

    /// <summary>Custom dynamic log attributes.</summary>
    public const string Attributes = "attributes";
}
