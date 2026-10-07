using InfraStream.Core.Configuration;
using InfraStream.Core.Exporters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InfraStream.CollectorEngine.Extensions;

/// <summary>
/// Fluent registration of telemetry exporters.
/// </summary>
/// <remarks>
/// Two styles are supported:
/// <list type="bullet">
///   <item>
///     <b>Extension methods</b> on <see cref="CollectorEngineBuilder"/>:
///     <code>
///     builder.AddCollectorEngine()
///            .AddNullExporter()
///            .AddKafkaExporter(new KafkaExporterOptions { ... });
///     </code>
///   </item>
///   <item>
///     <b>Fluent builder</b> via <see cref="AddExporters"/>:
///     <code>
///     builder.AddCollectorEngine()
///            .AddExporters(exporters => exporters
///                .Null()
///                .KafkaFromConfiguration());
///     </code>
///   </item>
/// </list>
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
    /// <param name="options">
    /// Full Kafka exporter configuration: bootstrap servers, topic,
    /// dead-letter topic, spill path, buffer size, and timeouts.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    public static CollectorEngineBuilder AddKafkaExporter(
        this CollectorEngineBuilder collector,
        KafkaExporterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        collector.Services.AddSingleton<ITelemetryExporter>(sp =>
            new KafkaExporter(
                options,
                sp.GetRequiredService<ILogger<KafkaExporter>>()));

        return collector;
    }

    /// <summary>
    /// Registers the Kafka exporter using configuration bound from the
    /// <c>InfraStream:Exporters:Kafka</c> section of <c>appsettings.json</c>.
    /// </summary>
    /// <param name="collector">Collector Engine builder.</param>
    public static CollectorEngineBuilder AddKafkaExporterFromConfiguration(
        this CollectorEngineBuilder collector)
    {
        collector.Services
            .AddOptions<KafkaExporterOptions>()
            .Bind(collector.Builder.Configuration
                .GetSection(KafkaExporterOptions.SectionName));

        collector.Services.AddSingleton<ITelemetryExporter>(sp =>
        {
            var options = sp
                .GetRequiredService<IOptions<KafkaExporterOptions>>()
                .Value;

            return new KafkaExporter(
                options,
                sp.GetRequiredService<ILogger<KafkaExporter>>());
        });

        return collector;
    }

    /// <summary>
    /// Registers one or more exporters using a fluent builder.
    /// </summary>
    /// <remarks>
    /// Example:
    /// <code>
    /// builder.AddCollectorEngine()
    ///        .AddExporters(exporters => exporters
    ///            .Null()
    ///            .KafkaFromConfiguration());
    /// </code>
    /// </remarks>
    /// <param name="collector">Collector Engine builder.</param>
    /// <param name="configure">
    /// Action that configures the <see cref="ExporterBuilder"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    public static CollectorEngineBuilder AddExporters(
        this CollectorEngineBuilder collector,
        Action<ExporterBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new ExporterBuilder(
            collector.Services,
            collector.Builder.Configuration);

        configure(builder);
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
