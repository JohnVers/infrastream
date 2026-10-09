using System.Diagnostics;
using InfraStream.CollectorEngine.Logging;

namespace InfraStream.CollectorEngine.BackgroundServices;

/// <summary>
/// Periodically logs per-worker and aggregate metrics: RSS, managed heap,
/// process CPU usage, processed batches/items per second, and error count.
/// </summary>
/// <remarks>
/// <para>
/// Every interval, one line per worker plus one aggregate line:
/// </para>
/// <para>
/// <c>cpu</c> follows the <c>docker stats</c> convention: 100% means one
/// fully used core. On an N-core container the process can reach N * 100%.
/// </para>
/// <code>
/// metrics worker=0 rss=221MB managed=256MB cpu=100% batches/s=210 items/s=2100000 errors=0
/// metrics total workers=4 rss=221MB managed=256MB cpu=100% batches/s=796 items/s=7960000 errors=0
/// </code>
/// <para>
/// <c>rss</c>, <c>managed</c>, and <c>cpu</c> are process-wide values and
/// are identical in the per-worker and total lines. <c>cpu</c> is expressed
/// as a percentage of <see cref="Environment.ProcessorCount"/>.
/// </para>
/// <para>
/// All log messages use source-generated <see cref="LoggerMessageAttribute"/>
/// delegates, so disabling them (e.g. via <c>Logging__LogLevel__InfraStream=None</c>)
/// eliminates both the console output and the argument-boxing allocations.
/// </para>
/// </remarks>
public sealed class WorkerMetricsReporter : BackgroundService
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(5);

    private readonly WorkerRegistry _registry;
    private readonly ILogger<WorkerMetricsReporter> _logger;
    private readonly TimeSpan _interval;
    private readonly Process _process;

    private (TimeSpan Cpu, long Ticks)? _prevCpuSample;
    private (long Batches, long Items, long Ticks)? _prevTotalSample;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerMetricsReporter"/> class.
    /// </summary>
    public WorkerMetricsReporter(
        WorkerRegistry registry,
        ILogger<WorkerMetricsReporter> logger,
        TimeSpan? interval = null)
    {
        _registry = registry;
        _logger = logger;
        _interval = interval ?? DefaultInterval;
        _process = Process.GetCurrentProcess();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        MetricsLog.ReporterStarted(
            _logger,
            Environment.ProcessorCount,
            Environment.GetEnvironmentVariable("DOTNET_PROCESSOR_COUNT") ?? "(unset)");

        var previousWorkers = new Dictionary<int, WorkerSample>();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(_interval, stoppingToken).ConfigureAwait(false);

                long rssMb = Environment.WorkingSet / (1024 * 1024);
                long managedMb = GC.GetTotalMemory(forceFullCollection: false) / (1024 * 1024);
                long nowTicks = stopwatch.ElapsedTicks;

                double cpuPercent = ComputeCpuPercent(nowTicks);

                long totalBatches = 0;
                long totalItems = 0;
                long totalErrors = 0;
                int workerCount = 0;

                foreach (var worker in _registry.Workers)
                {
                    int index = worker.WorkerIndex;
                    long batches = worker.BatchesProcessed;
                    long items = worker.ItemsProcessed;
                    long errors = worker.Errors;

                    double batchesPerSec = 0;
                    double itemsPerSec = 0;

                    if (previousWorkers.TryGetValue(index, out var prev))
                    {
                        double deltaSec = (nowTicks - prev.TimestampTicks) / (double)Stopwatch.Frequency;
                        if (deltaSec > 0)
                        {
                            batchesPerSec = (batches - prev.Batches) / deltaSec;
                            itemsPerSec = (items - prev.Items) / deltaSec;
                        }
                    }

                    previousWorkers[index] = new WorkerSample(batches, items, nowTicks);

                    MetricsLog.WorkerMetrics(
                        _logger,
                        index,
                        rssMb,
                        managedMb,
                        cpuPercent,
                        batchesPerSec,
                        itemsPerSec,
                        errors);

                    totalBatches += batches;
                    totalItems += items;
                    totalErrors += errors;
                    workerCount++;
                }

                double totalBatchesPerSec = 0;
                double totalItemsPerSec = 0;

                if (_prevTotalSample is { } prevTotal)
                {
                    double deltaSec = (nowTicks - prevTotal.Ticks) / (double)Stopwatch.Frequency;
                    if (deltaSec > 0)
                    {
                        totalBatchesPerSec = (totalBatches - prevTotal.Batches) / deltaSec;
                        totalItemsPerSec = (totalItems - prevTotal.Items) / deltaSec;
                    }
                }

                _prevTotalSample = (totalBatches, totalItems, nowTicks);

                MetricsLog.TotalMetrics(
                    _logger,
                    workerCount,
                    rssMb,
                    managedMb,
                    cpuPercent,
                    totalBatchesPerSec,
                    totalItemsPerSec,
                    totalErrors);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        finally
        {
            _process.Dispose();
        }
    }

    /// <summary>
    /// Returns process CPU usage as a percentage of all available CPUs,
    /// computed from the delta between this and the previous sample.
    /// Returns 0 on the first sample (no baseline yet).
    /// </summary>
    private double ComputeCpuPercent(long nowTicks)
    {
        TimeSpan cpuNow;
        try
        {
            cpuNow = _process.TotalProcessorTime;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }

        double cpuPercent = 0;

        if (_prevCpuSample is { } prev)
        {
            double wallDeltaSec = (nowTicks - prev.Ticks) / (double)Stopwatch.Frequency;
            double cpuDeltaSec = (cpuNow - prev.Cpu).TotalSeconds;

            if (wallDeltaSec > 0 && cpuDeltaSec >= 0)
            {
                // docker stats convention: 100% == one fully used core.
                // On N cores, total CPU can reach N * 100%.
                cpuPercent = 100.0 * cpuDeltaSec / wallDeltaSec;
            }
        }

        _prevCpuSample = (cpuNow, nowTicks);
        return cpuPercent;
    }


    private readonly record struct WorkerSample(long Batches, long Items, long TimestampTicks);
}
