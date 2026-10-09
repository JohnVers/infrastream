
namespace InfraStream.CollectorEngine.Logging;

/// <summary>
/// Source-generated log messages for <c>WorkerMetricsReporter</c>.
/// </summary>
/// <remarks>
/// All messages use <see cref="LoggerMessageAttribute"/> delegates, which
/// check <c>IsEnabled</c> before any argument is boxed or formatted. When
/// the level is disabled (e.g. via <c>Logging__LogLevel__InfraStream=None</c>),
/// nothing is allocated and nothing is written.
/// </remarks>
internal static partial class MetricsLog
{
    [LoggerMessage(
        EventId = 100,
        Level = LogLevel.Information,
        Message = "metrics: reporter started. ProcessorCount={ProcessorCount}, DotnetProcessorCountEnv={EnvValue}")]
    internal static partial void ReporterStarted(ILogger logger, int processorCount, string envValue);

    [LoggerMessage(
        EventId = 101,
        Level = LogLevel.Information,
        Message = "metrics worker={Index} rss={Rss}MB managed={Managed}MB cpu={Cpu}% batches/s={BatchesPerSec} items/s={ItemsPerSec} errors={Errors}")]
    internal static partial void WorkerMetrics(
        ILogger logger,
        int index,
        long rss,
        long managed,
        double cpu,
        double batchesPerSec,
        double itemsPerSec,
        long errors);

    [LoggerMessage(
        EventId = 102,
        Level = LogLevel.Information,
        Message = "metrics total workers={Workers} rss={Rss}MB managed={Managed}MB cpu={Cpu}% batches/s={BatchesPerSec} items/s={ItemsPerSec} errors={Errors}")]
    internal static partial void TotalMetrics(
        ILogger logger,
        int workers,
        long rss,
        long managed,
        double cpu,
        double batchesPerSec,
        double itemsPerSec,
        long errors);
}
