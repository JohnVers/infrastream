using InfraStream.CollectorEngine.Configuration;
using InfraStream.CollectorEngine.Networking;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace InfraStream.CollectorEngine.Ingress.Protocols;

/// <summary>
/// HTTP/1.1 over raw TCP, parsed by <see cref="StreamConnectionHandler"/>.
/// </summary>
/// <remarks>
/// This is the zero-alloc hot path: the handler parses HTTP/1.1 directly
/// from a <see cref="System.Buffers.ReadOnlySequence{T}"/>. No
/// <c>HttpContext</c>, no middleware.
/// </remarks>
public sealed class Http1ConnectionHandlerProtocol : IIngressProtocol
{
    /// <inheritdoc />
    public string Name => "Http1.ConnectionHandler";

    /// <inheritdoc />
    public int Port(InfraStreamOptions options)
        => IngressOptionsValidator.ResolveHttp1Port(options);

    /// <inheritdoc />
    /// <remarks>No DI services: <see cref="StreamConnectionHandler"/> is
    /// constructed by Kestrel.</remarks>
    public void ConfigureServices(IServiceCollection services, InfraStreamOptions options)
    {
        // No-op.
    }

    /// <inheritdoc />
    public void ConfigureListener(KestrelServerOptions kestrel, InfraStreamOptions options)
    {
        int port = Port(options);
        kestrel.ListenAnyIP(port, lo => lo.UseConnectionHandler<StreamConnectionHandler>());
    }

    /// <inheritdoc />
    /// <remarks>No HTTP endpoints: the handler owns the connection.</remarks>
    public void MapEndpoints(WebApplication app, InfraStreamOptions options)
    {
        // No-op.
    }
}
