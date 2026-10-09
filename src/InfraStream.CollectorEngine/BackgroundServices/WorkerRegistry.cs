using System.Collections.Generic;

namespace InfraStream.CollectorEngine.BackgroundServices;

/// <summary>
/// Registry of all <see cref="TelemetryProcessorWorker"/> instances created
/// for this process. Used by <see cref="WorkerMetricsReporter"/> and
/// <c>InfraStreamMetrics</c> to read per-worker counters.
/// </summary>
/// <remarks>
/// Workers are created by the DI factory in <c>CollectorEngineExtensions</c>
/// and register themselves here at construction time. The registry is a
/// singleton; access to the list is synchronized because workers may be
/// created lazily during host startup.
/// </remarks>
public sealed class WorkerRegistry
{
    private readonly List<TelemetryProcessorWorker> _workers = new();
    private readonly object _lock = new();

    /// <summary>
    /// Snapshot of the currently registered workers.
    /// </summary>
    public IReadOnlyList<TelemetryProcessorWorker> Workers
    {
        get
        {
            lock (_lock)
            {
                return _workers.ToArray();
            }
        }
    }

    /// <summary>Number of registered workers.</summary>
    public int WorkerCount
    {
        get
        {
            lock (_lock)
            {
                return _workers.Count;
            }
        }
    }

    /// <summary>
    /// Registers a worker. Called by the DI factory when a worker is created.
    /// </summary>
    /// <param name="worker">Worker to register.</param>
    internal void Register(TelemetryProcessorWorker worker)
    {
        ArgumentNullException.ThrowIfNull(worker);
        lock (_lock)
        {
            _workers.Add(worker);
        }
    }
}
