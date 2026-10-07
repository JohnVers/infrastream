using System.Buffers;

namespace InfraStream.Core.Serialization;

/// <summary>
/// A pool-backed implementation of <see cref="IBufferWriter{T}"/> that
/// writes directly into a <see cref="byte"/> array rented from
/// <see cref="ArrayPool{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="ArrayBufferWriter{T}"/>, this writer does not
/// allocate its own backing array — it rents from the pool and returns
/// the buffer on <see cref="Return"/>. This makes the writer suitable
/// for the hot path of the exporter pipeline.
/// </para>
/// <para>
/// The writer grows the underlying buffer on demand using a doubling
/// strategy. When growing, the previous buffer is returned to the pool
/// and a new one is rented.
/// </para>
/// <para>
/// The caller MUST call <see cref="Return"/> exactly once when done.
/// After calling <see cref="Return"/>, the writer must not be used.
/// </para>
/// </remarks>
internal sealed class PooledBufferWriter : IBufferWriter<byte>
{
    /// <summary>Default initial buffer size.</summary>
    public const int DefaultInitialSize = 512;

    private readonly ArrayPool<byte> _pool;

    private byte[] _buffer;
    private int _written;
    private bool _returned;

    /// <summary>
    /// Initializes a new writer with a buffer rented from the shared pool.
    /// </summary>
    public PooledBufferWriter() : this(DefaultInitialSize)
    {
    }

    /// <summary>
    /// Initializes a new writer with a buffer rented from the shared pool.
    /// </summary>
    /// <param name="initialSize">Initial buffer size in bytes.</param>
    public PooledBufferWriter(int initialSize)
    {
        if (initialSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(initialSize));

        _pool = ArrayPool<byte>.Shared;
        _buffer = _pool.Rent(initialSize);
        _written = 0;
        _returned = false;
    }

    /// <summary>Number of bytes written so far.</summary>
    public int WrittenCount => _written;

    /// <summary>The written portion of the buffer.</summary>
    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

    /// <summary>The written portion of the buffer.</summary>
    public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);

    /// <inheritdoc />
    public void Advance(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (_written + count > _buffer.Length)
            throw new InvalidOperationException("Cannot advance past the end of the buffer.");

        _written += count;
    }

    /// <inheritdoc />
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsMemory(_written);
    }

    /// <inheritdoc />
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsSpan(_written);
    }

    /// <summary>
    /// Returns the underlying buffer to the pool.
    /// Must be called exactly once. After this, the writer must not be used.
    /// </summary>
    public void Return()
    {
        if (_returned)
            return;

        _returned = true;
        _pool.Return(_buffer, clearArray: false);
        _buffer = Array.Empty<byte>();
        _written = 0;
    }

    private void EnsureCapacity(int sizeHint)
    {
        if (sizeHint < 0)
            throw new ArgumentOutOfRangeException(nameof(sizeHint));

        // Per IBufferWriter<T> contract: sizeHint = 0 means "any amount".
        if (sizeHint == 0)
            sizeHint = 1;

        int free = _buffer.Length - _written;
        if (free >= sizeHint)
            return;

        Grow(sizeHint);
    }

    private void Grow(int sizeHint)
    {
        int required = _written + sizeHint;

        // Double until it fits.
        int newSize = _buffer.Length * 2;
        while (newSize < required)
            newSize *= 2;

        byte[] newBuffer = _pool.Rent(newSize);
        _buffer.AsSpan(0, _written).CopyTo(newBuffer);

        _pool.Return(_buffer, clearArray: false);
        _buffer = newBuffer;
    }
}
