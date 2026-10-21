using InfraStream.CollectorEngine.Configuration;
using Microsoft.AspNetCore.Server.Kestrel.Core;

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
            o.MaxReceiveMessageSize = options.Protocols.Grpc.MaxReceiveMessageSize;
        });
        services.AddSingleton<IngestGrpcService>();
    }

    /// <inheritdoc />
    public void ConfigureListener(KestrelServerOptions kestrel, InfraStreamOptions options)
    {
        int port = Port(options);
        kestrel.Limits.Http2.MaxStreamsPerConnection =
            options.Protocols.Grpc.MaxStreamsPerConnection;
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
