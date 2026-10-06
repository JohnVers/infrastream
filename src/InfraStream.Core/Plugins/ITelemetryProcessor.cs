namespace InfraStream.Core.Plugins;

/// <summary>
/// Contract for a high-performance log processing / filtering plugin.
/// </summary>
public interface ITelemetryProcessor
{
    /// <summary>
    /// Processor name used for configuration and logging.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Allocation-free in-place processing or filtering of a log record.
    /// </summary>
    /// <param name="context">
    /// Mutable processing context passed by reference.
    /// </param>
    void Process(ref LogProcessingContext context);
}
