using InfraStream.Core.Exporters;

namespace InfraStream.CollectorEngine.Extensions;

/// <summary>
/// Fluent builder for registering telemetry exporters.
/// </summary>
/// <remarks>
/// Example:
/// <code>
/// builder.AddExporters(exporters => exporters
///     .Null()
///     .OpenTelemetry("http://otel-collector:4317")
///     .Kafka("kafka:9092", "telemetry"));
/// </code>
/// </remarks>
public sealed class ExporterBuilder
{
    private readonly IServiceCollection _services;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="ExporterBuilder"/> class.
    /// </summary>
    /// <param name="services">Service collection to register exporters into.</param>
    internal ExporterBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Registers <see cref="NullExporter"/> — a no-op sink.
    /// Used for benchmarks and dry-run testing.
    /// </summary>
    public ExporterBuilder Null()
    {
        _services.AddSingleton<ITelemetryExporter, NullExporter>();
        return this;
    }

    /// <summary>
    /// Registers the OpenTelemetry exporter (OTLP).
    /// </summary>
    /// <param name="endpoint">
    /// OTLP endpoint (e.g. <c>http://otel-collector:4317</c>).
    /// </param>
    public ExporterBuilder OpenTelemetry(string endpoint)
    {
        _services.AddSingleton<ITelemetryExporter>(sp =>
            new OpenTelemetryExporter(endpoint));
        return this;
    }

    /// <summary>
    /// Registers the Kafka exporter.
    /// </summary>
    /// <param name="bootstrapServers">
    /// Kafka bootstrap servers (e.g. <c>kafka:9092</c>).
    /// </param>
    /// <param name="topic">Target topic for telemetry.</param>
    public ExporterBuilder Kafka(string bootstrapServers, string topic)
    {
        _services.AddSingleton<ITelemetryExporter>(sp =>
            new KafkaExporter(bootstrapServers, topic));
        return this;
    }

    /// <summary>
    /// Registers a custom exporter via the supplied factory.
    /// </summary>
    /// <typeparam name="TExporter">Custom exporter type.</typeparam>
    /// <param name="factory">
    /// Factory invoked with the current <see cref="IServiceProvider"/>
    /// to create the exporter instance.
    /// </param>
    public ExporterBuilder Custom<TExporter>(Func<IServiceProvider, TExporter> factory)
        where TExporter : class, ITelemetryExporter
    {
        _services.AddSingleton<ITelemetryExporter>(sp => factory(sp));
        return this;
    }
}
