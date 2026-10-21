namespace InfraStream.CollectorEngine.Configuration;

/// <summary>
/// Configuration options for the InfraStream collector engine.
/// Bound from the "InfraStream" section of infrastream.json /
/// environment variables.
/// </summary>
/// <remarks>
/// <para>
/// Example <c>infrastream.json</c>:
/// </para>
/// <code>
/// {
///   "InfraStream": {
///     "IngressPort": 5005,
///     "ManagementPort": 5002,
///     "WorkerCount": 4,
///     "QueueCapacity": 64,
///     "Protocols": {
///       "Http1": { "Enabled": true,  "Port": 5005, "Mode": "ConnectionHandler" },
///       "Http2": { "Enabled": false, "Port": 5006 },
///       "Grpc":  { "Enabled": false, "Port": 5007 }
///     }
///   }
/// }
/// </code>
/// <para>
/// Environment override (ASP.NET Core convention, double underscore):
/// </para>
/// <code>
/// InfraStream__WorkerCount=4
/// InfraStream__Protocols__Http2__Enabled=true
/// </code>
/// </remarks>
public sealed class InfraStreamOptions
{
    /// <summary>Configuration section name in infrastream.json.</summary>
    public const string SectionName = "InfraStream";

    /// <summary>
    /// Legacy ingress port. Kept for backward compatibility; used as a
    /// fallback when <see cref="Protocols"/>.<see cref="IngressProtocolsOptions.Http1"/>.<see cref="Http1ProtocolOptions.Port"/>
    /// is not set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Default: <c>5005</c>. Chosen to avoid a clash with the ASP.NET Core
    /// development defaults (<c>5000</c> for HTTP, <c>5001</c> for HTTPS).
    /// </para>
    /// <para>
    /// If both <see cref="IngressPort"/> and
    /// <see cref="Http1ProtocolOptions.Port"/> are set and differ, startup
    /// validation fails.
    /// </para>
    /// </remarks>
    public int IngressPort { get; set; } = 5005;

    /// <summary>
    /// Port for management HTTP endpoints (health, /metrics).
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

    /// <summary>
    /// Ingress protocol configuration. At least one protocol must be
    /// enabled.
    /// </summary>
    public IngressProtocolsOptions Protocols { get; set; } = new();
}

/// <summary>
/// Per-protocol ingress configuration.
/// </summary>
public sealed class IngressProtocolsOptions
{
    /// <summary>HTTP/1.1 ingress.</summary>
    public Http1ProtocolOptions Http1 { get; set; } = new();

    /// <summary>HTTP/2 ingress.</summary>
    public Http2ProtocolOptions Http2 { get; set; } = new();

    /// <summary>gRPC ingress (requires HTTP/2).</summary>
    public GrpcProtocolOptions Grpc { get; set; } = new();
}

/// <summary>
/// HTTP/1.1 ingress configuration.
/// </summary>
public sealed class Http1ProtocolOptions
{
    /// <summary>Enables HTTP/1.1 ingress. Default: <see langword="true"/>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// HTTP/1.1 port. <c>0</c> means "use <see cref="InfraStreamOptions.IngressPort"/>".
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// HTTP/1.1 handling mode.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><c>"ConnectionHandler"</c> - custom zero-alloc HTTP/1.1
    ///         parser on raw TCP (fastest path).</item>
    ///   <item><c>"Kestrel"</c> - standard ASP.NET Core HTTP/1.1 pipeline
    ///         (HttpContext, middleware).</item>
    /// </list>
    /// Default: <c>"ConnectionHandler"</c>.
    /// </remarks>
    public string Mode { get; set; } = "ConnectionHandler";
}

/// <summary>
/// HTTP/2 ingress configuration.
/// </summary>
public sealed class Http2ProtocolOptions
{
    /// <summary>Enables HTTP/2 ingress. Default: <see langword="false"/>.</summary>
    public bool Enabled { get; set; }

    /// <summary>HTTP/2 port. Default: <c>5006</c>.</summary>
    public int Port { get; set; } = 5006;

    /// <summary>
    /// Maximum number of concurrent HTTP/2 streams per connection.
    /// </summary>
    /// <remarks>
    /// Default: <c>100</c> (Kestrel default). Raise for high-parallelism
    /// clients, e.g. load tests with more than 100 concurrent copies.
    /// Very high values can degrade performance due to TCP
    /// head-of-line blocking on a single connection.
    /// </remarks>
    public int MaxStreamsPerConnection { get; set; } = 100;
}

/// <summary>
/// gRPC ingress configuration. Requires HTTP/2.
/// </summary>
public sealed class GrpcProtocolOptions
{
    /// <summary>Enables gRPC ingress. Default: <see langword="false"/>.</summary>
    public bool Enabled { get; set; }

    /// <summary>gRPC port. Default: <c>5007</c>.</summary>
    public int Port { get; set; } = 5007;

    /// <summary>
    /// Maximum number of concurrent HTTP/2 streams per connection.
    /// </summary>
    /// <remarks>
    /// Default: <c>100</c> (Kestrel default). Raise for high-parallelism
    /// clients. Very high values can degrade performance due to TCP
    /// head-of-line blocking on a single connection.
    /// </remarks>
    public int MaxStreamsPerConnection { get; set; } = 100;

    /// <summary>
    /// Maximum size, in bytes, of an incoming gRPC message.
    /// </summary>
    /// <remarks>
    /// Default: <c>4 MB</c> (gRPC default). Raise for large batches
    /// (e.g. 10k-record protobuf payloads).
    /// </remarks>
    public int MaxReceiveMessageSize { get; set; } = 4 * 1024 * 1024;
}
