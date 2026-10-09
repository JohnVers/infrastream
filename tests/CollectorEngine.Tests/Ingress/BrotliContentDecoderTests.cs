using System.IO.Compression;
using System.Text;
using InfraStream.CollectorEngine.Ingress;
using InfraStream.CollectorEngine.Ingress.Decoders;
using Xunit;

namespace CollectorEngine.Tests.Ingress;

public sealed class BrotliContentDecoderTests
{
    private readonly BrotliContentDecoder _decoder = new();

    /// <summary>
    /// Canonical Brotli-compressed payload for the string
    /// "hello, brotli telemetry". Generated once and pinned here so the
    /// test verifies interop with an external encoder, not only symmetry
    /// with the BCL encoder used in the other tests.
    /// </summary>
    private const string CanonicalCompressedBase64 = "CwuAaGVsbG8sIGJyb3RsaSB0ZWxlbWV0cnkD";
    private const string CanonicalPlainText = "hello, brotli telemetry";


    [Fact]
    public void Encoding_IsBrotli()
    {
        Assert.Equal(ContentEncoding.Brotli, _decoder.Encoding);
    }

    [Fact]
    public void Decode_CanonicalFixture_ProducesExpectedText()
    {
        byte[] compressed = Convert.FromBase64String(CanonicalCompressedBase64);
        Span<byte> destination = stackalloc byte[256];

        int written = _decoder.Decode(compressed, destination);

        string decoded = Encoding.UTF8.GetString(destination.Slice(0, written));
        Assert.Equal(CanonicalPlainText, decoded);
    }

    [Fact]
    public void Decode_RoundTripsPayloadCompressedInTest()
    {
        ReadOnlyMemory<byte> plain = "the quick brown fox jumps over the lazy dog"u8.ToArray();
        byte[] compressed = CompressWithBrotli(plain.Span);

        Span<byte> destination = stackalloc byte[256];
        int written = _decoder.Decode(compressed, destination);

        Assert.Equal(plain.Length, written);
        Assert.True(plain.Span.SequenceEqual(destination.Slice(0, written)));
    }

    [Fact]
    public void Decode_ExactlyFits_Works()
    {
        ReadOnlyMemory<byte> plain = "abc"u8.ToArray();
        byte[] compressed = CompressWithBrotli(plain.Span);

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

    private static void DecodeEmptyInput(BrotliContentDecoder decoder)
    {
        Span<byte> destination = stackalloc byte[16];
        decoder.Decode(ReadOnlyMemory<byte>.Empty, destination);
    }

    [Fact]
    public void Decode_MalformedInput_Throws()
    {
        Assert.Throws<InvalidDataException>(() => DecodeMalformedInput(_decoder));
    }

    private static void DecodeMalformedInput(BrotliContentDecoder decoder)
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
        byte[] compressed = CompressWithBrotli(plain);

        // Sanity: the compressed payload must actually decompress to >16 bytes.
        Assert.True(plain.Length > 16);

        Assert.Throws<InvalidDataException>(() => DecodeWithSmallDestination(_decoder, compressed));
    }

    private static void DecodeWithSmallDestination(BrotliContentDecoder decoder, byte[] compressed)
    {
        Span<byte> smallDestination = stackalloc byte[16];
        decoder.Decode(compressed, smallDestination);
    }

    [Fact]
    public void Decode_BinaryContent_RoundTrips()
    {
        byte[] plain = { 0x00, 0x01, 0xFF, 0x7F, 0x80, 0x42, 0x43, 0x44 };
        byte[] compressed = CompressWithBrotli(plain);

        Span<byte> destination = stackalloc byte[32];
        int written = _decoder.Decode(compressed, destination);

        Assert.Equal(plain.Length, written);
        Assert.True(plain.AsSpan().SequenceEqual(destination.Slice(0, written)));
    }

    private static byte[] CompressWithBrotli(ReadOnlySpan<byte> plain)
    {
        int maxCompressed = BrotliEncoder.GetMaxCompressedLength(plain.Length);
        byte[] scratch = new byte[maxCompressed];

        if (!BrotliEncoder.TryCompress(plain, scratch, out int compressedLength))
            throw new InvalidOperationException("Brotli compression failed in test setup.");

        return scratch.AsSpan(0, compressedLength).ToArray();
    }
}
