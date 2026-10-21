using InfraStream.CollectorEngine.BackgroundServices;
using InfraStream.CollectorEngine.Configuration;
using InfraStream.CollectorEngine.Hosting;
using InfraStream.CollectorEngine.Ingress;
using InfraStream.CollectorEngine.Ingress.Protocols;
using InfraStream.CollectorEngine.Logging;
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
    /// Configuration sources, in increasing priority:
    ///   appsettings.json -> appsettings.{Environment}.json ->
    ///   infrastream.json -> environment variables (InfraStream__*).
    ///
    /// Explicit arguments (if provided) override both config and env.
    ///
    /// The ingress protocol configuration is validated before any DI
    /// registrations or Kestrel listeners are set up. On failure,
    /// <see cref="ConfigurationValidationException"/> is thrown; the
    /// application entry point is expected to catch it and exit with a
    /// clear message.
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
        // 0. InfraStream config: separate file, overrides appsettings.json,
        //    but is overridden by environment variables.
        builder.Configuration
            .AddJsonFile("infrastream.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();

        // 1. Bind options from config ("InfraStream" section).
        builder.Services
            .AddOptions<InfraStreamOptions>()
            .Bind(builder.Configuration.GetSection(InfraStreamOptions.SectionName));

        // 2. Read options immediately (needed for validation and Kestrel setup).
        var options = builder.Configuration
            .GetSection(InfraStreamOptions.SectionName)
            .Get<InfraStreamOptions>() ?? new InfraStreamOptions();

        // 3. Validate protocol configuration BEFORE any DI registrations or
        //    Kestrel listeners. On failure, throw; the entry point catches
        //    ConfigurationValidationException and exits with a clear message.
        var validationErrors = IngressOptionsValidator.Validate(options);
        if (validationErrors.Count > 0)
        {
            throw new ConfigurationValidationException(
                InfraStreamOptions.SectionName,
                validationErrors);
        }

        // 4. Select the single ingress strategy.
        var strategy = IngressProtocolSelector.Select(options);

        // 5. Configure DI services for the strategy.
        strategy.ConfigureServices(builder.Services, options);

        // 6. Ingress options + content decoders.
        builder.Services.AddIngressContentDecoders(builder.Configuration);

        // 7. Resolve values.
        int resolvedIngressPort = ingressPort ?? strategy.Port(options);
        int resolvedManagementPort = managementPort ?? options.ManagementPort;
        int resolvedQueueCapacity = queueCapacity ?? options.QueueCapacity;

        int resolvedWorkerCount = workerCount
                                  ?? (options.WorkerCount > 0 ? options.WorkerCount : 0);
        if (resolvedWorkerCount <= 0)
            resolvedWorkerCount = Math.Max(1, Environment.ProcessorCount);

        // 8. Queue (Singleton).
        var queue = new TelemetryChannelQueue(resolvedQueueCapacity);
        builder.Services.AddSingleton(queue);

        // 9. Worker registry (Singleton) — collects worker instances for the
        //    metrics reporter.
        var workerRegistry = new WorkerRegistry();
        builder.Services.AddSingleton(workerRegistry);

        // 10. N workers. Each IHostedService creates its own TelemetryProcessorWorker
        //     and registers it in the WorkerRegistry.
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
                    .CreateLogger($"InfraStream.CollectorEngine.TelemetryProcessorWorker[{index}]");

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

        // 11. Periodic metrics reporter (Singleton hosted service).
        builder.Services.AddSingleton<IHostedService>(sp =>
        {
            var registry = sp.GetRequiredService<WorkerRegistry>();
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger<WorkerMetricsReporter>();
            return new WorkerMetricsReporter(registry, logger);
        });

        // 12. Graceful shutdown: stop accepting new payloads as soon as the host
        //     begins stopping. Workers drain the remaining payloads and exit.
        builder.Services.AddSingleton<IHostedService>(sp =>
        {
            var queue = sp.GetRequiredService<TelemetryChannelQueue>();
            var lifetime = sp.GetRequiredService<IHostApplicationLifetime>();
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger("InfraStream.CollectorEngine.GracefulShutdown");

            lifetime.ApplicationStopping.Register(() =>
            {
                ShutdownLog.Started(logger, queue.Count);
                queue.Complete();
            });

            return new NoopHostedService();
        });

        // 13. Kestrel: management HTTP + one ingress listener for the strategy.
        builder.WebHost.ConfigureKestrel(kestrelOptions =>
        {
            kestrelOptions.ListenAnyIP(resolvedManagementPort);
            strategy.ConfigureListener(kestrelOptions, options);
        });

        // 14. Publish the strategy for MapCollectorEngineEndpoints.
        builder.Services.AddSingleton(strategy);

        // 15. Publish resolved config via DI (for health / startup logs).
        builder.Services.AddSingleton(new CollectorEngineConfig(
            IngressPort: resolvedIngressPort,
            ManagementPort: resolvedManagementPort,
            WorkerCount: resolvedWorkerCount,
            QueueCapacity: resolvedQueueCapacity));

        return new CollectorEngineBuilder(builder);
    }

    private sealed class NoopHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
