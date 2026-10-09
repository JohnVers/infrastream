using InfraStream.CollectorEngine.Ingress;
using InfraStream.CollectorEngine.Ingress.Decoders;
using Xunit;

namespace CollectorEngine.Tests.Ingress;

public sealed class ContentDecoderRegistryTests
{
    [Fact]
    public void TryGet_ByEnum_ResolvesRegisteredDecoder()
    {
        var registry = new ContentDecoderRegistry(new IContentDecoder[] { new IdentityContentDecoder() });

        Assert.True(registry.TryGet(ContentEncoding.Identity, out var decoder));
        Assert.IsType<IdentityContentDecoder>(decoder);
    }

    [Fact]
    public void TryGet_ByToken_ResolvesRegisteredDecoder()
    {
        var registry = new ContentDecoderRegistry(new IContentDecoder[] { new IdentityContentDecoder() });

        Assert.True(registry.TryGet("identity", out var decoder));
        Assert.IsType<IdentityContentDecoder>(decoder);
    }

    [Theory]
    [InlineData("IDENTITY")]
    [InlineData("Identity")]
    [InlineData("identity")]
    public void TryGet_ByToken_IsCaseInsensitive(string token)
    {
        var registry = new ContentDecoderRegistry(new IContentDecoder[] { new IdentityContentDecoder() });
        Assert.True(registry.TryGet(token, out _));
    }

    [Fact]
    public void TryGet_NullOrEmptyToken_FallsBackToIdentity()
    {
        var registry = new ContentDecoderRegistry(new IContentDecoder[] { new IdentityContentDecoder() });

        Assert.True(registry.TryGet((string?)null, out var d1));
        Assert.IsType<IdentityContentDecoder>(d1);

        Assert.True(registry.TryGet(string.Empty, out var d2));
        Assert.IsType<IdentityContentDecoder>(d2);
    }

    [Fact]
    public void TryGet_UnknownEncoding_ReturnsFalse()
    {
        var registry = new ContentDecoderRegistry(new IContentDecoder[] { new IdentityContentDecoder() });

        Assert.False(registry.TryGet(ContentEncoding.Brotli, out _));
        Assert.False(registry.TryGet("br", out _));
    }

    [Fact]
    public void Constructor_DuplicateEncoding_Throws()
    {
        var decoders = new IContentDecoder[]
        {
            new IdentityContentDecoder(),
            new IdentityContentDecoder()
        };

        var ex = Assert.Throws<ArgumentException>(() => new ContentDecoderRegistry(decoders));
        Assert.Contains("identity", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_NullDecoders_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ContentDecoderRegistry(null!));
    }

    [Fact]
    public void SupportedTokens_ContainsRegisteredTokens()
    {
        var registry = new ContentDecoderRegistry(new IContentDecoder[] { new IdentityContentDecoder() });
        Assert.Contains("identity", registry.SupportedTokens);
    }

    [Fact]
    public void IsSupported_MatchesTryGet()
    {
        var registry = new ContentDecoderRegistry(new IContentDecoder[] { new IdentityContentDecoder() });

        Assert.True(registry.IsSupported("identity"));
        Assert.True(registry.IsSupported(null));
        Assert.False(registry.IsSupported("br"));
    }
}
