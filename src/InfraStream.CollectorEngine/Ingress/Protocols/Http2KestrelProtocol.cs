using InfraStream.CollectorEngine.Configuration;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace InfraStream.CollectorEngine.Ingress.Protocols;

/// <summary>
/// HTTP/2 via Kestrel (h2c or TLS, depending on the endpoint configuration).
/// </summary>
public sealed class Http2KestrelProtocol : IIngressProtocol
{

    /// <inheritdoc />
    public string Name => "Http2";

    /// <inheritdoc />
    public int Port(InfraStreamOptions options) => options.Protocols.Http2.Port;

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, InfraStreamOptions options)
    {
        services.AddSingleton<IngestHandler>();
    }

    /// <inheritdoc />
    public void ConfigureListener(KestrelServerOptions kestrel, InfraStreamOptions options)
    {
        int port = Port(options);
        kestrel.Limits.Http2.MaxStreamsPerConnection =
            options.Protocols.Http2.MaxStreamsPerConnection;
        kestrel.Listen(System.Net.IPAddress.Any, port, lo =>
        {
            lo.Protocols = HttpProtocols.Http2;
        });
    }

    /// <inheritdoc />
    public void MapEndpoints(WebApplication app, InfraStreamOptions options)
    {
        int port = Port(options);
        app.MapPost(IngestHandler.Path, IngestHandler.HandleAsync)
            .RequireHost($"*:{port}");
    }
}
