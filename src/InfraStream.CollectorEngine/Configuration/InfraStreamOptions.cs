namespace InfraStream.CollectorEngine.Configuration;

/// <summary>
/// Configuration options for the InfraStream collector engine.
/// Bound from the "InfraStream" section of appsettings.json / environment
/// variables.
/// </summary>
/// <remarks>
/// Example <c>appsettings.json</c>:
/// <code>
/// {
///   "InfraStream": {
///     "IngressPort": 5005,
///     "ManagementPort": 5002,
///     "WorkerCount": 4,
///     "QueueCapacity": 64
///   }
/// }
/// </code>
/// Environment override (ASP.NET Core convention, double underscore):
/// <code>
/// InfraStream__WorkerCount=4
/// </code>
/// </remarks>
public sealed class InfraStreamOptions
{
    /// <summary>Configuration section name in appsettings.json.</summary>
    public const string SectionName = "InfraStream";

    /// <summary>
    /// Port for incoming telemetry (raw TCP + ConnectionHandler).
    /// </summary>
    /// <remarks>
    /// Default: <c>5005</c>. Chosen to avoid a clash with the ASP.NET Core
    /// development defaults (<c>5000</c> for HTTP, <c>5001</c> for HTTPS).
    /// </remarks>
    public int IngressPort { get; set; } = 5005;

    /// <summary>
    /// Port for management HTTP endpoints (health, future /metrics).
    /// </summary>
    /// <remarks>
    /// Default: <c>5002</c>.
    /// </remarks>
    public int ManagementPort { get; set; } = 5002;

    /// <summary>
    /// Number of parallel worker threads.
    /// </summary>
    /// <remarks>
    /// <c>0</c> or unset means "use <see cref="Environment.ProcessorCount"/>".
    /// Default: <c>0</c>.
    /// </remarks>
    public int WorkerCount { get; set; }

    /// <summary>
    /// Bounded channel capacity (number of batches in flight).
    /// </summary>
    /// <remarks>
    /// Default: <c>64</c>.
    /// </remarks>
    public int QueueCapacity { get; set; } = 64;
}
