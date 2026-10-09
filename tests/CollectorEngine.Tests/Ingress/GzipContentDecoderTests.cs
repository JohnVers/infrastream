using System.IO.Compression;
using InfraStream.CollectorEngine.Ingress;
using InfraStream.CollectorEngine.Ingress.Decoders;
using Xunit;

namespace CollectorEngine.Tests.Ingress;

public sealed class GzipContentDecoderTests
{
    private readonly GzipContentDecoder _decoder = new();

    [Fact]
    public void Encoding_IsGzip()
    {
        Assert.Equal(ContentEncoding.Gzip, _decoder.Encoding);
    }

    [Fact]
    public void Decode_RoundTripsPayloadCompressedInTest()
    {
        ReadOnlyMemory<byte> plain = "the quick brown fox jumps over the lazy dog"u8.ToArray();
        byte[] compressed = CompressWithGzip(plain.Span);

        Span<byte> destination = stackalloc byte[256];
        int written = _decoder.Decode(compressed, destination);

        Assert.Equal(plain.Length, written);
        Assert.True(plain.Span.SequenceEqual(destination.Slice(0, written)));
    }

    [Fact]
    public void Decode_ExactlyFits_Works()
    {
        ReadOnlyMemory<byte> plain = "abc"u8.ToArray();
        byte[] compressed = CompressWithGzip(plain.Span);

        Span<byte> destination = stackalloc byte[3];
        int written = _decoder.Decode(compressed, destination);

        Assert.Equal(3, written);
        Assert.True(plain.Span.SequenceEqual(destination));
    }

    [Fact]
    public void Decode_EmptyInput_Throws()
    {
        Assert.Throws<InvalidDataException>(() => DecodeEmptyInput(_decoder));
    }

    private static void DecodeEmptyInput(GzipContentDecoder decoder)
    {
        Span<byte> destination = stackalloc byte[16];
        decoder.Decode(ReadOnlyMemory<byte>.Empty, destination);
    }

    [Fact]
    public void Decode_MalformedInput_Throws()
    {
        Assert.Throws<InvalidDataException>(() => DecodeMalformedInput(_decoder));
    }

    private static void DecodeMalformedInput(GzipContentDecoder decoder)
    {
        ReadOnlyMemory<byte> garbage = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x01, 0x02, 0x03 };
        Span<byte> destination = stackalloc byte[256];
        decoder.Decode(garbage, destination);
    }

    [Fact]
    public void Decode_DestinationTooSmall_Throws()
    {
        byte[] plain = new byte[200];
        Array.Fill(plain, (byte)'A');
        byte[] compressed = CompressWithGzip(plain);

        Assert.True(plain.Length > 16);

        Assert.Throws<InvalidDataException>(() => DecodeWithSmallDestination(_decoder, compressed));
    }

    private static void DecodeWithSmallDestination(GzipContentDecoder decoder, byte[] compressed)
    {
        Span<byte> smallDestination = stackalloc byte[16];
        decoder.Decode(compressed, smallDestination);
    }

    [Fact]
    public void Decode_BinaryContent_RoundTrips()
    {
        byte[] plain = { 0x00, 0x01, 0xFF, 0x7F, 0x80, 0x42, 0x43, 0x44 };
        byte[] compressed = CompressWithGzip(plain);

        Span<byte> destination = stackalloc byte[32];
        int written = _decoder.Decode(compressed, destination);

        Assert.Equal(plain.Length, written);
        Assert.True(plain.AsSpan().SequenceEqual(destination.Slice(0, written)));
    }

    [Fact]
    public void Decode_MultiBlockStream_RoundTrips()
    {
        // Larger payload to force GZipStream to consume multiple internal blocks.
        byte[] plain = new byte[8192];
        for (int i = 0; i < plain.Length; i++) plain[i] = (byte)(i & 0xFF);

        byte[] compressed = CompressWithGzip(plain);

        byte[] destination = new byte[plain.Length];
        int written = _decoder.Decode(compressed, destination);

        Assert.Equal(plain.Length, written);
        Assert.True(plain.AsSpan().SequenceEqual(destination.AsSpan(0, written)));
    }

    private static byte[] CompressWithGzip(ReadOnlySpan<byte> plain)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(plain);
        }
        return output.ToArray();
    }
}
