using InfraStream.CollectorEngine.Ingress;
using InfraStream.CollectorEngine.Ingress.Decoders;
using Xunit;

namespace CollectorEngine.Tests.Ingress;

public sealed class IdentityContentDecoderTests
{
    private readonly IdentityContentDecoder _decoder = new();

    [Fact]
    public void Encoding_IsIdentity()
    {
        Assert.Equal(ContentEncoding.Identity, _decoder.Encoding);
    }

    [Fact]
    public void Decode_CopiesBytesVerbatim()
    {
        ReadOnlyMemory<byte> input = "hello, telemetry"u8.ToArray();
        Span<byte> destination = stackalloc byte[64];

        int written = _decoder.Decode(input, destination);

        Assert.Equal(input.Length, written);
        Assert.True(input.Span.SequenceEqual(destination.Slice(0, written)));
    }

    [Fact]
    public void Decode_EmptyInput_ReturnsZero()
    {
        Span<byte> destination = stackalloc byte[16];
        int written = _decoder.Decode(ReadOnlyMemory<byte>.Empty, destination);
        Assert.Equal(0, written);
    }

    [Fact]
    public void Decode_ExactlyFits_Works()
    {
        ReadOnlyMemory<byte> input = "abc"u8.ToArray();
        Span<byte> destination = stackalloc byte[3];

        int written = _decoder.Decode(input, destination);

        Assert.Equal(3, written);
        Assert.True(input.Span.SequenceEqual(destination));
    }

    [Fact]
    public void Decode_DestinationTooSmall_Throws()
    {
        Assert.Throws<InvalidDataException>(() => DecodeWithSmallDestination());
    }

    private static void DecodeWithSmallDestination()
    {
        var decoder = new IdentityContentDecoder();
        ReadOnlyMemory<byte> input = "too long"u8.ToArray();
        Span<byte> destination = stackalloc byte[2];
        decoder.Decode(input, destination);
    }

    [Fact]
    public void Decode_BinaryContent_Works()
    {
        byte[] input = { 0x00, 0x01, 0xFF, 0x7F, 0x80 };
        Span<byte> destination = stackalloc byte[16];

        int written = _decoder.Decode(input, destination);

        Assert.Equal(input.Length, written);
        Assert.True(input.AsSpan().SequenceEqual(destination.Slice(0, written)));
    }
}
