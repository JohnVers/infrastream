namespace InfraStream.CollectorEngine.Extensions;

/// <summary>
/// Typed builder for fluent configuration of the Collector Engine.
/// Returned from <c>AddCollectorEngine</c> and allows chaining:
/// </summary>
/// <remarks>
/// <code>
/// builder.AddCollectorEngine()
///        .AddNullExporter()
///        .AddOpenTelemetryExporter("http://otel:4317")
///        .AddKafkaExporter("kafka:9092", "telemetry");
/// </code>
/// <para>
/// Holds a reference to <see cref="WebApplicationBuilder"/> so that
/// extensions can register services in DI.
/// </para>
/// </remarks>
public sealed class CollectorEngineBuilder
{
    private readonly WebApplicationBuilder _builder;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="CollectorEngineBuilder"/> class.
    /// </summary>
    /// <param name="builder">The owning <see cref="WebApplicationBuilder"/>.</param>
    internal CollectorEngineBuilder(WebApplicationBuilder builder)
    {
        _builder = builder;
    }

    /// <summary>
    /// Underlying <see cref="WebApplicationBuilder"/>.
    /// Use it if you need to fall back to the standard fluent API.
    /// </summary>
    public WebApplicationBuilder Builder => _builder;

    /// <summary>
    /// Direct access to <see cref="IServiceCollection"/>.
    /// </summary>
    public IServiceCollection Services => _builder.Services;
}
