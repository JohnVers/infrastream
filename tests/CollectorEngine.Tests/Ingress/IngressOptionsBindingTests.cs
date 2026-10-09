using InfraStream.CollectorEngine.Ingress;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CollectorEngine.Tests.Ingress;

public sealed class IngressOptionsBindingTests
{
    [Fact]
    public void Defaults_AreReasonable()
    {
        var options = new IngressOptions();

        Assert.Equal(1 * 1024 * 1024, options.MaxBodySize);
        Assert.Equal(16 * 1024 * 1024, options.DecompressedBufferSize);
        Assert.NotNull(options.ContentDecoders);
        Assert.True(options.ContentDecoders.Identity.Enabled);
        Assert.True(options.ContentDecoders.Brotli.Enabled);
        Assert.True(options.ContentDecoders.Gzip.Enabled);
        Assert.False(options.ContentDecoders.Zstd.Enabled);
    }

    [Fact]
    public void Binds_FromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InfraStream:Ingress:MaxBodySize"] = "2048",
                ["InfraStream:Ingress:DecompressedBufferSize"] = "4096",
                ["InfraStream:Ingress:ContentDecoders:Identity:Enabled"] = "true",
                ["InfraStream:Ingress:ContentDecoders:Brotli:Enabled"] = "false",
                ["InfraStream:Ingress:ContentDecoders:Gzip:Enabled"] = "true",
                ["InfraStream:Ingress:ContentDecoders:Zstd:Enabled"] = "false",
                ["InfraStream:Ingress:ContentDecoders:Brotli:Quality"] = "5",
                ["InfraStream:Ingress:ContentDecoders:Zstd:Level"] = "3"
            })
            .Build();

        var options = config.GetSection(IngressOptions.SectionName).Get<IngressOptions>();

        Assert.NotNull(options);
        Assert.Equal(2048, options!.MaxBodySize);
        Assert.Equal(4096, options.DecompressedBufferSize);
        Assert.True(options.ContentDecoders.Identity.Enabled);
        Assert.False(options.ContentDecoders.Brotli.Enabled);
        Assert.True(options.ContentDecoders.Gzip.Enabled);
        Assert.False(options.ContentDecoders.Zstd.Enabled);
        Assert.Equal(5, options.ContentDecoders.Brotli.Quality);
        Assert.Equal(3, options.ContentDecoders.Zstd.Level);
    }

    [Fact]
    public void SectionName_IsExpected()
    {
        Assert.Equal("InfraStream:Ingress", IngressOptions.SectionName);
    }

    [Fact]
    public void MissingSection_ProducesDefaults()
    {
        var config = new ConfigurationBuilder().Build();
        var options = config.GetSection(IngressOptions.SectionName).Get<IngressOptions>();
        // When the section is missing, the binder returns null;
        // AddIngressContentDecoders handles that with `?? new IngressOptions()`.
        Assert.Null(options);
    }
}
