using InfraStream.CollectorEngine.Configuration;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace InfraStream.CollectorEngine.Ingress.Protocols;

/// <summary>
/// Strategy for one ingress protocol listener. Exactly one strategy is
/// selected per process based on the configuration.
/// </summary>
/// <remarks>
/// <para>
/// Strategies are stateless: the same instance may be reused, and
/// <see cref="ConfigureListener"/> / <see cref="ConfigureServices"/> /
/// <see cref="MapEndpoints"/> are pure functions of the options.
/// </para>
/// <para>
/// Lifecycle:
/// <list type="number">
///   <item><see cref="ConfigureServices"/> — registers DI services
///         (called from <c>AddCollectorEngine</c>).</item>
///   <item><see cref="ConfigureListener"/> — registers a Kestrel listener
///         (called from <c>ConfigureKestrel</c>).</item>
///   <item><see cref="MapEndpoints"/> — maps HTTP endpoints (called from
///         <c>MapCollectorEngineEndpoints</c> after <c>builder.Build()</c>).</item>
/// </list>
/// </para>
/// </remarks>
public interface IIngressProtocol
{
    /// <summary>Stable name for logging and diagnostics.</summary>
    string Name { get; }

    /// <summary>Effective port for this protocol.</summary>
    int Port(InfraStreamOptions options);

    /// <summary>
    /// Registers DI services required by this protocol.
    /// </summary>
    void ConfigureServices(IServiceCollection services, InfraStreamOptions options);

    /// <summary>
    /// Registers the Kestrel listener.
    /// </summary>
    void ConfigureListener(KestrelServerOptions kestrel, InfraStreamOptions options);

    /// <summary>
    /// Maps HTTP endpoints. No-op for <c>ConnectionHandler</c>-mode protocols.
    /// </summary>
    void MapEndpoints(WebApplication app, InfraStreamOptions options);
}
