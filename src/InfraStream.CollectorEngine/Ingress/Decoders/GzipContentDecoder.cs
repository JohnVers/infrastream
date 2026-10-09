namespace InfraStream.CollectorEngine.Ingress.Decoders;

/// <summary>
/// Gzip decoder (<c>Content-Encoding: gzip</c>, RFC 1952).
/// </summary>
public sealed class GzipContentDecoder : IContentDecoder
{
    /// <inheritdoc />
    public ContentEncoding Encoding => ContentEncoding.Gzip;

    /// <inheritdoc />
    public int Decode(ReadOnlyMemory<byte> input, Span<byte> destination)
    {
        if (input.IsEmpty)
        {
            throw new InvalidDataException("Gzip input is empty.");
        }

        using var source = new ReadOnlyMemoryStream(input);
        using var gzip = new GZipStream(source, CompressionMode.Decompress, leaveOpen: true);

        int totalWritten = 0;

        while (totalWritten < destination.Length)
        {
            int read = gzip.Read(destination.Slice(totalWritten));
            if (read == 0)
                break;

            totalWritten += read;
        }

        // Probe for one more byte: if the stream still has data,
        // the destination was too small.
        if (totalWritten == destination.Length)
        {
            Span<byte> probe = stackalloc byte[1];
            if (gzip.Read(probe) > 0)
            {
                throw new InvalidDataException(
                    $"Gzip decompressed payload is larger than the destination buffer ({destination.Length} bytes).");
            }
        }

        return totalWritten;
    }

    /// <summary>
    /// Minimal read-only <see cref="Stream"/> over a
    /// <see cref="ReadOnlyMemory{Byte}"/> buffer.
    /// </summary>
    private sealed class ReadOnlyMemoryStream : Stream
    {
        private readonly ReadOnlyMemory<byte> _buffer;
        private int _position;

        public ReadOnlyMemoryStream(ReadOnlyMemory<byte> buffer) => _buffer = buffer;

        public override bool CanRead  => true;
        public override bool CanSeek  => false;
        public override bool CanWrite => false;

        public override long Length => _buffer.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
            => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int remaining = _buffer.Length - _position;
            if (remaining <= 0) return 0;

            int toCopy = Math.Min(remaining, buffer.Length);
            _buffer.Span.Slice(_position, toCopy).CopyTo(buffer);
            _position += toCopy;
            return toCopy;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
