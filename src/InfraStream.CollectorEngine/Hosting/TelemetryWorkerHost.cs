using InfraStream.CollectorEngine.BackgroundServices;

namespace InfraStream.CollectorEngine.Hosting;

/// <summary>
/// Wrapper around <see cref="TelemetryProcessorWorker"/>.
/// One <see cref="TelemetryWorkerHost"/> = one worker in the
/// <c>IHostedService</c> set.
/// </summary>
/// <remarks>
/// Allows registering N workers in DI.
/// </remarks>
public sealed class TelemetryWorkerHost : BackgroundService
{
    private readonly TelemetryProcessorWorker _worker;

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
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => _worker.RunAsync(stoppingToken);
}
