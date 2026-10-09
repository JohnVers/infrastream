using InfraStream.CollectorEngine.Ingress.Decoders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InfraStream.CollectorEngine.Ingress;

/// <summary>
/// Extension methods for registering ingress content decoders.
/// </summary>
public static class IngressServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IngressOptions"/> and all enabled content
    /// decoders, plus the <see cref="ContentDecoderRegistry"/>.
    /// </summary>
    /// <remarks>
    /// Decoders are registered only if their <c>Enabled</c> flag is
    /// <see langword="true"/> in configuration. Enabling or disabling a
    /// decoder requires an application restart.
    /// </remarks>
    /// <param name="services">DI container.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same <paramref name="services"/> instance.</returns>
    public static IServiceCollection AddIngressContentDecoders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(IngressOptions.SectionName);
        services.Configure<IngressOptions>(section);

        var options = section.Get<IngressOptions>() ?? new IngressOptions();
        var decoders = options.ContentDecoders;

        if (decoders.Identity.Enabled)
            services.AddSingleton<IContentDecoder, IdentityContentDecoder>();

        if (decoders.Brotli.Enabled)
            services.AddSingleton<IContentDecoder, BrotliContentDecoder>();

        if (decoders.Gzip.Enabled)
            services.AddSingleton<IContentDecoder, GzipContentDecoder>();

        // Zstd: not implemented yet. Once added, register it here under
        // decoders.Zstd.Enabled.

        services.AddSingleton<ContentDecoderRegistry>();
        return services;
    }
}
