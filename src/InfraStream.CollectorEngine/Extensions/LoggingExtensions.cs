namespace InfraStream.CollectorEngine.Extensions;

/// <summary>
/// Extension for configuring InfraStream logging.
/// </summary>
/// <remarks>
/// <para>
/// In Development — human-readable single-line format.
/// In Production — structured JSON (for K8s, Fluent Bit, Vector, OTel).
/// </para>
/// <para>
/// Filters:
/// <list type="bullet">
///   <item><c>Microsoft.AspNetCore</c> — Warning (suppress INFO spam).</item>
///   <item><c>Microsoft.AspNetCore.Server.Kestrel</c> — Critical
///         (suppress everything except failures).</item>
/// </list>
/// </para>
/// </remarks>
public static class LoggingExtensions
{
    /// <summary>
    /// Configures InfraStream logging: providers and filters.
    /// </summary>
    /// <param name="builder">The owning <see cref="WebApplicationBuilder"/>.</param>
    /// <returns>
    /// The same <paramref name="builder"/> instance for fluent chaining.
    /// </returns>
    public static WebApplicationBuilder AddInfraStreamLogging(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();

        if (builder.Environment.IsDevelopment())
        {
            builder.Logging.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss.fff ";
            });
        }
        else
        {
            builder.Logging.AddJsonConsole(options =>
            {
                options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions
                {
                    Indented = false
                };
            });
        }

        // Suppress INFO spam from ASP.NET Core and Kestrel.
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.AspNetCore.Server.Kestrel", LogLevel.Critical);

        return builder;
    }
}
