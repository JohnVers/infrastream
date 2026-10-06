namespace InfraStream.CollectorEngine.Queues;

/// <summary>
/// Lock-free pool of <see cref="TelemetryBatchPayload"/> instances with
/// fixed capacity. Eliminates per-request allocations of objects and arrays.
/// </summary>
/// <remarks>
/// <para>
/// Overflow behavior:
/// <list type="bullet">
///   <item>The object is NOT placed into the pool (the pool is full).</item>
///   <item>The array is returned to <c>ArrayPool.Shared</c>.</item>
///   <item>The object itself is left for the GC.</item>
/// </list>
/// </para>
/// <para>
/// Memory limits:
/// the upper bound of the "hot" pool is
/// <c>PoolCapacity × DefaultBufferSize</c>.
/// With <c>PoolCapacity = 64</c> and <c>DefaultBufferSize = 1 MB</c>, that
/// is 64 MB for compressed batches. For uncompressed batches the array
/// size equals the batch size (~3 MB), so the pool may grow further.
/// </para>
/// </remarks>
public static class TelemetryBatchPool
{
    // Maximum number of objects retained in the pool at any given time.
    private const int PoolCapacity = 64;

    // Base size of the rented array for a batch.
    // For uncompressed batches the array will be larger —
    // Math.Max(minimumLength, DefaultBufferSize).
    private const int DefaultBufferSize = 1 * 1024 * 1024;

    private static readonly TelemetryBatchPayload?[] Pool = new TelemetryBatchPayload[PoolCapacity];

    /// <summary>
    /// Rents a container from the pool. If no suitable container is
    /// available, creates a new one.
    /// The returned object is guaranteed to be active (its return flag is
    /// reset), and its <c>Array</c> has a length &gt;= <paramref name="minimumLength"/>.
    /// </summary>
    /// <param name="minimumLength">
    /// Minimum required length of the underlying <c>Array</c>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="minimumLength"/> is negative.
    /// </exception>
    public static TelemetryBatchPayload Rent(int minimumLength)
    {
        if (minimumLength < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumLength));

        // Look for a free slot with a sufficiently large array.
        for (int i = 0; i < Pool.Length; i++)
        {
            var payload = Volatile.Read(ref Pool[i]);
            if (payload is null) continue;
            if (payload.Array.Length < minimumLength) continue;

            // Atomically claim the object from the slot.
            if (Interlocked.CompareExchange(ref Pool[i], null, payload) == payload)
            {
                payload.Length = minimumLength;
                payload.ResetForRent();
                return payload;
            }
        }

        // Pool is empty or has no slot large enough — create a new one.
        int sizeToAllocate = Math.Max(minimumLength, DefaultBufferSize);
        return new TelemetryBatchPayload
        {
            Array = ArrayPool<byte>.Shared.Rent(sizeToAllocate),
            Length = minimumLength
        };
    }

    /// <summary>
    /// Returns a container to the pool.
    /// If the pool is full, returns the raw array to
    /// <c>ArrayPool.Shared</c> and leaves the object for the GC.
    /// </summary>
    /// <remarks>
    /// Called ONLY from <see cref="TelemetryBatchPayload.Dispose"/>
    /// (guarded by <c>Interlocked</c>).
    /// </remarks>
    /// <param name="payload">The container being returned.</param>
    internal static void Return(TelemetryBatchPayload payload)
    {
        // Look for a free slot.
        for (int i = 0; i < Pool.Length; i++)
        {
            if (Volatile.Read(ref Pool[i]) is null)
            {
                if (Interlocked.CompareExchange(ref Pool[i], payload, null) is null)
                    return;
            }
        }

        // Pool is full: return the array to ArrayPool and leave the object for the GC.
        var array = payload.Array;
        if (array is not null)
        {
            payload.Array = null!;
            ArrayPool<byte>.Shared.Return(array);
        }
    }
}
