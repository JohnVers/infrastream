using InfraStream.CollectorEngine.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

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
        // int port = Port(options);
        // kestrel.ListenAnyIP(port, lo => lo.Protocols = HttpProtocols.Http2);

        int port = Port(options);
        // Явный IPv4 wildcard: Docker-сеть использует IPv4-адресацию.
        kestrel.Listen(System.Net.IPAddress.Any, port, lo => lo.Protocols = HttpProtocols.Http2);
    }

    /// <inheritdoc />
    public void MapEndpoints(WebApplication app, InfraStreamOptions options)
    {
        int port = Port(options);
        app.MapPost(IngestHandler.Path, IngestHandler.HandleAsync)
            .RequireHost($"*:{port}");
    }
}
