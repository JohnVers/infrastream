using System.Diagnostics;
using InfraStream.CollectorEngine.BackgroundServices;
using InfraStream.CollectorEngine.Queues;
using Prometheus;

namespace InfraStream.CollectorEngine.Metrics;

/// <summary>
/// Prometheus metrics for InfraStream, exposed via <c>/metrics</c>.
/// </summary>
/// <remarks>
/// <para>
/// Counters are updated lazily on each scrape via
/// <see cref="Prometheus.Metrics.DefaultRegistry"/> callbacks, reading the
/// current values from <see cref="WorkerRegistry"/>. This keeps the hot
/// path free of Prometheus calls.
/// </para>
/// <para>
/// Process-wide gauges (RSS, managed heap) and the queue depth gauge are
/// also refreshed on scrape. CPU is published as a cumulative counter
/// (<c>infrastream_process_cpu_seconds_total</c>); use Prometheus
/// <c>rate()</c> on the query side.
/// </para>
/// <para>
/// Registered as an <see cref="IHostedService"/> so that it is instantiated
/// during host startup, which creates the metrics and registers the
/// scrape callback on the default registry.
/// </para>
/// </remarks>
public sealed class InfraStreamMetrics : IHostedService
{
    private static readonly string[] WorkerLabel = { "worker" };

    private readonly WorkerRegistry _registry;
    private readonly TelemetryChannelQueue _queue;
    private readonly Process _process;

    private readonly Counter _batchesProcessed;
    private readonly Counter _itemsProcessed;
    private readonly Counter _errors;

    private readonly Gauge _queueDepth;
    private readonly Gauge _processRssBytes;
    private readonly Gauge _processManagedBytes;
    private readonly Counter _processCpuSeconds;

    /// <summary>
    /// Initializes metrics, registers them with the default registry, and
    /// wires the scrape-time callback.
    /// </summary>
    /// <param name="registry">Worker registry (source of per-worker counters).</param>
    /// <param name="queue">Bounded channel (source of the queue-depth gauge).</param>
    public InfraStreamMetrics(WorkerRegistry registry, TelemetryChannelQueue queue)
    {
        _registry = registry;
        _queue = queue;
        _process = Process.GetCurrentProcess();

        _batchesProcessed = Prometheus.Metrics.CreateCounter(
            "infrastream_batches_processed_total",
            "Total batches processed by a worker.",
            new CounterConfiguration { LabelNames = WorkerLabel });

        _itemsProcessed = Prometheus.Metrics.CreateCounter(
            "infrastream_items_processed_total",
            "Total telemetry items parsed and dispatched by a worker.",
            new CounterConfiguration { LabelNames = WorkerLabel });

        _errors = Prometheus.Metrics.CreateCounter(
            "infrastream_errors_total",
            "Total batch-level errors per worker.",
            new CounterConfiguration { LabelNames = WorkerLabel });

        _queueDepth = Prometheus.Metrics.CreateGauge(
            "infrastream_queue_depth",
            "Current number of batches waiting in the ingress queue.");

        _processRssBytes = Prometheus.Metrics.CreateGauge(
            "infrastream_process_rss_bytes",
            "Resident set size of the process, in bytes.");

        _processManagedBytes = Prometheus.Metrics.CreateGauge(
            "infrastream_process_managed_bytes",
            "Managed heap size, in bytes.");

        _processCpuSeconds = Prometheus.Metrics.CreateCounter(
            "infrastream_process_cpu_seconds_total",
            "Total CPU time consumed by the process, in seconds.");

        Prometheus.Metrics.DefaultRegistry.AddBeforeCollectCallback(OnBeforeCollect);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _process.Dispose();
        return Task.CompletedTask;
    }

    private void OnBeforeCollect()
    {
        // Per-worker counters.
        foreach (var worker in _registry.Workers)
        {
            string label = worker.WorkerIndex.ToString();
            _batchesProcessed.WithLabels(label).IncTo(worker.BatchesProcessed);
            _itemsProcessed.WithLabels(label).IncTo(worker.ItemsProcessed);
            _errors.WithLabels(label).IncTo(worker.Errors);
        }

        // Process-wide gauges.
        _queueDepth.Set(_queue.Reader.Count);
        _processRssBytes.Set(Environment.WorkingSet);
        _processManagedBytes.Set(GC.GetTotalMemory(forceFullCollection: false));
        _processCpuSeconds.IncTo(_process.TotalProcessorTime.TotalSeconds);
    }
}
