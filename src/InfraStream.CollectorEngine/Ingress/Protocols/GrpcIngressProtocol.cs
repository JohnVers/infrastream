using InfraStream.CollectorEngine.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace InfraStream.CollectorEngine.Ingress.Protocols;

/// <summary>
/// gRPC ingress via Kestrel (HTTP/2 cleartext, prior-knowledge).
/// </summary>
public sealed class GrpcIngressProtocol : IIngressProtocol
{
    /// <inheritdoc />
    public string Name => "Grpc";

    /// <inheritdoc />
    public int Port(InfraStreamOptions options) => options.Protocols.Grpc.Port;

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, InfraStreamOptions options)
    {
        services.AddGrpc(o =>
        {
            o.MaxReceiveMessageSize = 16 * 1024 * 1024;
        });
        services.AddSingleton<IngestGrpcService>();
    }

    /// <inheritdoc />
    public void ConfigureListener(KestrelServerOptions kestrel, InfraStreamOptions options)
    {
        int port = Port(options);
        // gRPC h2c: Http2-only (ALPN requires TLS for Http1AndHttp2).
        // IPv4 wildcard — Docker network uses IPv4 addressing.
        kestrel.Listen(System.Net.IPAddress.Any, port, lo =>
        {
            lo.Protocols = HttpProtocols.Http2;
        });
    }

    /// <inheritdoc />
    public void MapEndpoints(WebApplication app, InfraStreamOptions options)
    {
        app.MapGrpcService<IngestGrpcService>();
    }
}
