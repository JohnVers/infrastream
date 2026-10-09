using InfraStream.CollectorEngine.BackgroundServices;

namespace InfraStream.CollectorEngine.Hosting;

/// <summary>
/// Wrapper around <see cref="TelemetryProcessorWorker"/>.
/// One <see cref="TelemetryWorkerHost"/> = one worker in the
/// <c>IHostedService</c> set.
/// </summary>
/// <remarks>
/// <para>
/// On startup, runs <see cref="TelemetryProcessorWorker.RunAsync"/> on a
/// background task. On shutdown, waits for that task to complete — which
/// happens only after the channel is completed and drained, or after the
/// host shutdown timeout elapses.
/// </para>
/// </remarks>
public sealed class TelemetryWorkerHost : IHostedService
{
    private readonly TelemetryProcessorWorker _worker;
    private Task? _runTask;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="TelemetryWorkerHost"/> class.
    /// </summary>
    /// <param name="worker">The worker delegated to by this host.</param>
    public TelemetryWorkerHost(TelemetryProcessorWorker worker)
    {
        _worker = worker;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _runTask = Task.Run(() => _worker.RunAsync(CancellationToken.None), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_runTask is null)
            return;

        try
        {
            await _runTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Host shutdown timeout elapsed before the worker drained the
            // queue. Remaining payloads are dropped; the worker logs the
            // final counters on exit.
        }
    }
}
