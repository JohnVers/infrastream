using InfraStream.Core.Exporters;

namespace InfraStream.CollectorEngine.Extensions;

/// <summary>
/// Fluent registration of telemetry exporters.
/// </summary>
/// <remarks>
/// Example:
/// <code>
/// builder.AddCollectorEngine()
///        .AddNullExporter()
///        .AddOpenTelemetryExporter("http://otel:4317")
///        .AddKafkaExporter("kafka:9092", "telemetry");
/// </code>
/// <para>
/// All methods return <see cref="CollectorEngineBuilder"/> so that the
/// chain can continue.
/// </para>
/// </remarks>
public static class ExporterExtensions
{
    /// <summary>
    /// Registers <see cref="NullExporter"/> — a no-op sink.
    /// Used for benchmarks and dry-run testing.
    /// </summary>
    /// <param name="collector">Collector Engine builder.</param>
    public static CollectorEngineBuilder AddNullExporter(this CollectorEngineBuilder collector)
    {
        collector.Services.AddSingleton<ITelemetryExporter, NullExporter>();
        return collector;
    }

    /// <summary>
    /// Registers <see cref="ConsoleExporter"/> — prints every telemetry item
    /// to the console.
    /// </summary>
    /// <remarks>
    /// Intended for local development and troubleshooting.
    /// Not recommended for production: see the remarks on
    /// <see cref="ConsoleExporter"/> for details.
    /// </remarks>
    /// <param name="collector">Collector Engine builder.</param>
    public static CollectorEngineBuilder AddConsoleExporter(this CollectorEngineBuilder collector)
    {
        collector.Services.AddSingleton<ITelemetryExporter, ConsoleExporter>();
        return collector;
    }

    /// <summary>
    /// Registers the OpenTelemetry exporter (OTLP).
    /// </summary>
    /// <param name="collector">Collector Engine builder.</param>
    /// <param name="endpoint">
    /// OTLP endpoint (e.g. <c>http://otel-collector:4317</c>).
    /// </param>
    public static CollectorEngineBuilder AddOpenTelemetryExporter(
        this CollectorEngineBuilder collector,
        string endpoint)
    {
        collector.Services.AddSingleton<ITelemetryExporter>(_ =>
            new OpenTelemetryExporter(endpoint));
        return collector;
    }

    /// <summary>
    /// Registers the Kafka exporter.
    /// </summary>
    /// <param name="collector">Collector Engine builder.</param>
    /// <param name="bootstrapServers">
    /// Kafka bootstrap servers (e.g. <c>kafka:9092</c>).
    /// </param>
    /// <param name="topic">Target topic for telemetry.</param>
    public static CollectorEngineBuilder AddKafkaExporter(
        this CollectorEngineBuilder collector,
        string bootstrapServers,
        string topic)
    {
        collector.Services.AddSingleton<ITelemetryExporter>(_ =>
            new KafkaExporter(bootstrapServers, topic));
        return collector;
    }

    /// <summary>
    /// Registers a custom exporter via a factory.
    /// </summary>
    /// <typeparam name="TExporter">Custom exporter type.</typeparam>
    /// <param name="collector">Collector Engine builder.</param>
    /// <param name="factory">
    /// Factory invoked with the current <see cref="IServiceProvider"/>
    /// to create the exporter instance.
    /// </param>
    public static CollectorEngineBuilder AddCustomExporter<TExporter>(
        this CollectorEngineBuilder collector,
        Func<IServiceProvider, TExporter> factory)
        where TExporter : class, ITelemetryExporter
    {
        collector.Services.AddSingleton<ITelemetryExporter>(sp => factory(sp));
        return collector;
    }

    /// <summary>
    /// Registers a custom exporter that has a parameterless constructor.
    /// </summary>
    /// <typeparam name="TExporter">Custom exporter type.</typeparam>
    /// <param name="collector">Collector Engine builder.</param>
    public static CollectorEngineBuilder AddCustomExporter<TExporter>(
        this CollectorEngineBuilder collector)
        where TExporter : class, ITelemetryExporter, new()
    {
        collector.Services.AddSingleton<ITelemetryExporter, TExporter>();
        return collector;
    }
}
