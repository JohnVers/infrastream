using InfraStream.Core.Configuration;
using InfraStream.Core.Exporters;
using Microsoft.Extensions.Options;

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
///     .Kafka(new KafkaExporterOptions
///     {
///         BootstrapServers = "kafka:9092",
///         Topic = "telemetry",
///         DeadLetterTopic = "telemetry-dead-letter",
///         DiskSpillPath = "/var/lib/infrastream/spill/",
///     }));
/// </code>
/// <para>
/// Alternatively, bind the Kafka options from configuration:
/// <code>
/// builder.AddExporters(exporters => exporters
///     .Null()
///     .KafkaFromConfiguration());
/// </code>
/// </para>
/// </remarks>
public sealed class ExporterBuilder
{
    private readonly IServiceCollection _services;
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="ExporterBuilder"/> class.
    /// </summary>
    /// <param name="services">Service collection to register exporters into.</param>
    /// <param name="configuration">
    /// Application configuration used by the <c>*FromConfiguration</c>
    /// methods.
    /// </param>
    internal ExporterBuilder(
        IServiceCollection services,
        IConfiguration configuration)
    {
        _services = services;
        _configuration = configuration;
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
    /// <param name="options">
    /// Full Kafka exporter configuration: bootstrap servers, topic,
    /// dead-letter topic, spill path, buffer size, and timeouts.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    public ExporterBuilder Kafka(KafkaExporterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _services.AddSingleton<ITelemetryExporter>(sp =>
            new KafkaExporter(
                options,
                sp.GetRequiredService<ILogger<KafkaExporter>>()));

        return this;
    }

    /// <summary>
    /// Registers the Kafka exporter using configuration bound from the
    /// <c>InfraStream:Exporters:Kafka</c> section of <c>appsettings.json</c>
    /// (or environment variables).
    /// </summary>
    /// <remarks>
    /// Example <c>appsettings.json</c>:
    /// <code>
    /// {
    ///   "InfraStream": {
    ///     "Exporters": {
    ///       "Kafka": {
    ///         "BootstrapServers": "kafka:9092",
    ///         "Topic": "telemetry",
    ///         "DeadLetterTopic": "telemetry-dead-letter",
    ///         "DiskSpillPath": "/var/lib/infrastream/spill/"
    ///       }
    ///     }
    ///   }
    /// }
    /// </code>
    /// Environment override:
    /// <code>
    /// InfraStream__Exporters__Kafka__BootstrapServers=kafka:9092
    /// </code>
    /// </remarks>
    public ExporterBuilder KafkaFromConfiguration()
    {
        _services
            .AddOptions<KafkaExporterOptions>()
            .Bind(_configuration.GetSection(KafkaExporterOptions.SectionName));

        _services.AddSingleton<ITelemetryExporter>(sp =>
        {
            var options = sp
                .GetRequiredService<IOptions<KafkaExporterOptions>>()
                .Value;

            return new KafkaExporter(
                options,
                sp.GetRequiredService<ILogger<KafkaExporter>>());
        });

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
