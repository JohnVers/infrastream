using InfraStream.CollectorEngine.Ingress;
using Xunit;

namespace CollectorEngine.Tests.Ingress;

public sealed class ContentEncodingExtensionsTests
{
    [Theory]
    [InlineData(ContentEncoding.Identity, "identity")]
    [InlineData(ContentEncoding.Brotli,   "br")]
    [InlineData(ContentEncoding.Gzip,     "gzip")]
    [InlineData(ContentEncoding.Zstd,     "zstd")]
    public void ToToken_ReturnsCanonicalToken(ContentEncoding encoding, string expected)
    {
        Assert.Equal(expected, encoding.ToToken());
    }

    [Fact]
    public void ToToken_Throws_ForUnknownValue()
    {
        var unknown = (ContentEncoding)999;
        Assert.Throws<ArgumentOutOfRangeException>(() => unknown.ToToken());
    }

    [Theory]
    [InlineData("identity", ContentEncoding.Identity)]
    [InlineData("br",       ContentEncoding.Brotli)]
    [InlineData("gzip",     ContentEncoding.Gzip)]
    [InlineData("zstd",     ContentEncoding.Zstd)]
    [InlineData("BR",       ContentEncoding.Brotli)]
    [InlineData("GZip",     ContentEncoding.Gzip)]
    [InlineData("  br  ",   ContentEncoding.Brotli)]
    public void TryParseToken_ParsesKnownTokens_CaseInsensitive(string token, ContentEncoding expected)
    {
        Assert.True(ContentEncodingExtensions.TryParseToken(token, out var encoding));
        Assert.Equal(expected, encoding);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParseToken_EmptyOrNull_ReturnsTrue_WithIdentity(string? token)
    {
        Assert.True(ContentEncodingExtensions.TryParseToken(token, out var encoding));
        Assert.Equal(ContentEncoding.Identity, encoding);
    }

    [Theory]
    [InlineData("deflate")]
    [InlineData("snappy")]
    [InlineData("foobar")]
    [InlineData("brotli")]   // canonical token is "br", not "brotli"
    public void TryParseToken_UnknownToken_ReturnsFalse(string token)
    {
        Assert.False(ContentEncodingExtensions.TryParseToken(token, out var encoding));
        Assert.Equal(ContentEncoding.Identity, encoding);
    }

    [Theory]
    [InlineData("gzip, br")]
    [InlineData("br,gzip")]
    [InlineData("gzip , br")]
    public void TryParseToken_MultipleEncodings_ReturnsFalse(string token)
    {
        Assert.False(ContentEncodingExtensions.TryParseToken(token, out _));
    }
}
