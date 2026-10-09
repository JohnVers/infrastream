using InfraStream.CollectorEngine.Metrics;
using Microsoft.Extensions.DependencyInjection;

namespace InfraStream.CollectorEngine.Extensions;

/// <summary>
/// Fluent registration for InfraStream management endpoints and metrics.
/// </summary>
public static class InfraStreamMetricsExtensions
{
    /// <summary>
    /// Enables Prometheus metrics (<c>/metrics</c>) and the
    /// <c>/ready</c> endpoint on the management port.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registers <see cref="InfraStreamMetrics"/> as a hosted service so
    /// that it is instantiated during host startup. The constructor
    /// creates the Prometheus counters/gauges and registers the
    /// scrape-time callback on the default registry.
    /// </para>
    /// <para>
    /// Call <c>app.MapCollectorEngineEndpoints(managementPort)</c> after
    /// <c>builder.Build()</c> to attach the endpoints.
    /// </para>
    /// </remarks>
    public static CollectorEngineBuilder WithManagementEndpoints(
        this CollectorEngineBuilder collector)
    {
        collector.Services.AddHostedService<InfraStreamMetrics>();
        return collector;
    }
}
