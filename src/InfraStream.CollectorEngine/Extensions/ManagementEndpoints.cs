using InfraStream.CollectorEngine.BackgroundServices;
using InfraStream.CollectorEngine.Configuration;
using InfraStream.CollectorEngine.Ingress.Protocols;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace InfraStream.CollectorEngine.Extensions;

/// <summary>
/// Maps InfraStream management endpoints (<c>/health</c>, <c>/ready</c>,
/// <c>/metrics</c>) onto the management port.
/// </summary>
public static class ManagementEndpoints
{
    /// <summary>
    /// Maps management endpoints, restricted to the management port.
    /// </summary>
    /// <param name="app">Application to map onto.</param>
    /// <param name="managementPort">Port for management endpoints.</param>
    public static void MapCollectorEngineEndpoints(
        this WebApplication app,
        int managementPort)
    {
        // Wildcard host, explicit port: matches any hostname on the
        // management port only. The bare ":port" form is not accepted
        // by HostMatcherPolicy.
        string hostFilter = $"*:{managementPort}";

        // /health — liveness.
        app.MapGet("/health", (CollectorEngineConfig config) => Results.Ok(new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            workerCount = config.WorkerCount
        }))
        .RequireHost(hostFilter);

        // /ready — readiness.
        app.MapGet("/ready", (
            WorkerRegistry registry,
            IHostApplicationLifetime lifetime) =>
        {
            if (lifetime.ApplicationStopping.IsCancellationRequested)
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

            if (registry.WorkerCount == 0)
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

            return Results.Ok(new
            {
                status = "ready",
                workers = registry.WorkerCount
            });
        })
        .RequireHost(hostFilter);

        // /metrics — Prometheus exposition format.
        app.MapGet("/metrics", async context =>
        {
            context.Response.ContentType = "text/plain; version=0.0.4; charset=utf-8";
            await global::Prometheus.Metrics.DefaultRegistry
                .CollectAndExportAsTextAsync(context.Response.Body);
        })
        .RequireHost(hostFilter);

        // Ingress endpoints for Kestrel-mode protocols.
        var strategy = app.Services.GetRequiredService<IIngressProtocol>();
        var options = app.Services.GetRequiredService<IOptions<InfraStreamOptions>>().Value;
        strategy.MapEndpoints(app, options);
    }
}
