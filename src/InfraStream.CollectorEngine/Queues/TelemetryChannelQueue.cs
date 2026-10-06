namespace InfraStream.CollectorEngine.Queues;

/// <summary>
/// Hard-bounded in-memory transit channel between Kestrel sockets and
/// worker threads.
/// </summary>
/// <remarks>
/// <para>
/// Provides backpressure: when workers cannot keep up, Kestrel threads
/// asynchronously wait inside <c>WriteAsync</c>, the kernel shrinks the
/// TCP window, and the gateway does not overflow.
/// </para>
/// <para>
/// Usage model:
/// <list type="bullet">
///   <item>MANY writers (Kestrel threads, one per connection).</item>
///   <item>SEVERAL readers (N <c>TelemetryProcessorWorker</c> instances,
///         one per CPU core).</item>
///   <item>Full channel → the writer waits
///         (<see cref="BoundedChannelFullMode.Wait"/>).</item>
/// </list>
/// </para>
/// </remarks>
public sealed class TelemetryChannelQueue
{
    /// <summary>
    /// Default capacity of the bounded channel.
    /// </summary>
    public const int DefaultCapacity = 64;

    private readonly Channel<TelemetryBatchPayload> _channel;

    /// <summary>
    /// Reader side of the underlying bounded channel.
    /// </summary>
    public ChannelReader<TelemetryBatchPayload> Reader => _channel.Reader;

    /// <summary>
    /// Writer side of the underlying bounded channel.
    /// </summary>
    public ChannelWriter<TelemetryBatchPayload> Writer => _channel.Writer;

    /// <summary>
    /// Number of payloads currently waiting in the channel.
    /// </summary>
    public int Count => _channel.Reader.Count;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="TelemetryChannelQueue"/> class.
    /// </summary>
    /// <param name="capacity">Maximum number of payloads in the channel.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="capacity"/> is not positive.
    /// </exception>
    public TelemetryChannelQueue(int capacity = DefaultCapacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");

        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,

            // Multiple Kestrel threads write concurrently.
            SingleWriter = false,

            // Multiple workers read in parallel (one per CPU core).
            SingleReader = false,

            AllowSynchronousContinuations = false
        };

        _channel = Channel.CreateBounded<TelemetryBatchPayload>(options);
    }
}
