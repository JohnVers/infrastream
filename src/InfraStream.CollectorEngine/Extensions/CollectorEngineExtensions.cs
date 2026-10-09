using InfraStream.CollectorEngine.BackgroundServices;
using InfraStream.CollectorEngine.Configuration;
using InfraStream.CollectorEngine.Hosting;
using InfraStream.CollectorEngine.Ingress;
using InfraStream.CollectorEngine.Networking;
using InfraStream.CollectorEngine.Queues;
using InfraStream.Core.Exporters;
using InfraStream.Core.Plugins;
using Microsoft.Extensions.Options;

namespace InfraStream.CollectorEngine.Extensions;

/// <summary>
/// Resolved configuration for the collector engine, exposed via DI.
/// Used by health / startup logs to display actual runtime settings.
/// </summary>
public sealed record CollectorEngineConfig(
    int IngressPort,
    int ManagementPort,
    int WorkerCount,
    int QueueCapacity);

public static class CollectorEngineExtensions
{
    /// <summary>
    /// Registers the collector engine: queue, N workers, and Kestrel listeners.
    ///
    /// Configuration is read from the "InfraStream" section of appsettings.json,
    /// with environment variable overrides (e.g. InfraStream__WorkerCount=4).
    ///
    /// Explicit arguments (if provided) override both config and env.
    ///
    /// Returns a CollectorEngineBuilder for fluent configuration:
    ///   builder.AddCollectorEngine()
    ///          .AddNullExporter()
    ///          .AddOpenTelemetryExporter("http://otel:4317");
    /// </summary>
    public static CollectorEngineBuilder AddCollectorEngine(
        this WebApplicationBuilder builder,
        int? ingressPort = null,
        int? managementPort = null,
        int? queueCapacity = null,
        int? workerCount = null)
    {
        // 1. Bind options from config ("InfraStream" section).
        builder.Services
            .AddOptions<InfraStreamOptions>()
            .Bind(builder.Configuration.GetSection(InfraStreamOptions.SectionName));

        // 2. Ingress options + content decoders (section "InfraStream:Ingress").
        builder.Services.AddIngressContentDecoders(builder.Configuration);

        // 3. Read options immediately to configure Kestrel (needs values at startup).
        var options = builder.Configuration
            .GetSection(InfraStreamOptions.SectionName)
            .Get<InfraStreamOptions>() ?? new InfraStreamOptions();

        // 4. Resolve values: explicit argument > config/env > default.
        int resolvedIngressPort = ingressPort ?? options.IngressPort;
        int resolvedManagementPort = managementPort ?? options.ManagementPort;
        int resolvedQueueCapacity = queueCapacity ?? options.QueueCapacity;

        int resolvedWorkerCount = workerCount
            ?? (options.WorkerCount > 0 ? options.WorkerCount : 0);
        if (resolvedWorkerCount <= 0)
            resolvedWorkerCount = Math.Max(1, Environment.ProcessorCount);

        // 5. Queue (Singleton).
        var queue = new TelemetryChannelQueue(resolvedQueueCapacity);
        builder.Services.AddSingleton(queue);

        // 6. Worker registry (Singleton) — collects worker instances for the
        //    metrics reporter.
        var workerRegistry = new WorkerRegistry();
        builder.Services.AddSingleton(workerRegistry);

        // 7. N workers. Each IHostedService creates its own TelemetryProcessorWorker
        //    and registers it in the WorkerRegistry.
        for (int i = 0; i < resolvedWorkerCount; i++)
        {
            int index = i;

            builder.Services.AddSingleton<IHostedService>(sp =>
            {
                var processors = sp.GetServices<ITelemetryProcessor>().ToArray();
                var exporters = sp.GetServices<ITelemetryExporter>().ToArray();
                var decoders = sp.GetRequiredService<ContentDecoderRegistry>();
                var ingressOptions = sp.GetRequiredService<IOptions<IngressOptions>>();
                var logger = sp.GetRequiredService<ILoggerFactory>()
                    .CreateLogger($"TelemetryProcessorWorker[{index}]");

                var worker = new TelemetryProcessorWorker(
                    sp.GetRequiredService<TelemetryChannelQueue>(),
                    processors,
                    exporters,
                    decoders,
                    ingressOptions,
                    logger,
                    index);

                sp.GetRequiredService<WorkerRegistry>().Register(worker);

                return new TelemetryWorkerHost(worker);
            });
        }

        // 8. Periodic metrics reporter (Singleton hosted service).
        builder.Services.AddSingleton<IHostedService>(sp =>
        {
            var registry = sp.GetRequiredService<WorkerRegistry>();
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger<WorkerMetricsReporter>();
            return new WorkerMetricsReporter(registry, logger);
        });

        // 9. Kestrel: management HTTP + ingress raw TCP.
        builder.WebHost.ConfigureKestrel(kestrelOptions =>
        {
            // Management HTTP (health, future /metrics).
            kestrelOptions.ListenAnyIP(resolvedManagementPort);

            // Ingress: raw TCP + ConnectionHandler.
            kestrelOptions.ListenAnyIP(resolvedIngressPort, listenOptions =>
            {
                listenOptions.UseConnectionHandler<StreamConnectionHandler>();
            });
        });

        // 10. Publish resolved config via DI (for health / startup logs).
        builder.Services.AddSingleton(new CollectorEngineConfig(
            IngressPort: resolvedIngressPort,
            ManagementPort: resolvedManagementPort,
            WorkerCount: resolvedWorkerCount,
            QueueCapacity: resolvedQueueCapacity));

        return new CollectorEngineBuilder(builder);
    }
}
