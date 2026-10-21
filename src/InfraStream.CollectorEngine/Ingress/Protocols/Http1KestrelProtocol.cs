using InfraStream.CollectorEngine.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace InfraStream.CollectorEngine.Ingress.Protocols;

/// <summary>
/// HTTP/1.1 via the standard ASP.NET Core pipeline
/// (<c>HttpContext</c>, middleware).
/// </summary>
/// <remarks>
/// Uses <see cref="IngestHandler"/> to read the body, validate
/// <c>Content-Encoding</c>, and enqueue the payload. Slower than
/// <see cref="Http1ConnectionHandlerProtocol"/> but compatible with
/// ASP.NET Core middleware, filters, and DI.
/// </remarks>
public sealed class Http1KestrelProtocol : IIngressProtocol
{
    /// <inheritdoc />
    public string Name => "Http1.Kestrel";

    /// <inheritdoc />
    public int Port(InfraStreamOptions options)
        => IngressOptionsValidator.ResolveHttp1Port(options);

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, InfraStreamOptions options)
    {
        services.AddSingleton<IngestHandler>();
    }

    /// <inheritdoc />
    public void ConfigureListener(KestrelServerOptions kestrel, InfraStreamOptions options)
    {
        int port = Port(options);
        kestrel.ListenAnyIP(port, lo => lo.Protocols = HttpProtocols.Http1);
    }

    /// <inheritdoc />
    public void MapEndpoints(WebApplication app, InfraStreamOptions options)
    {
        int port = Port(options);
        app.MapPost(IngestHandler.Path, IngestHandler.HandleAsync)
            .RequireHost($"*:{port}");
    }
}
