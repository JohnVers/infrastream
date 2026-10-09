namespace InfraStream.CollectorEngine.Logging;

/// <summary>
/// Source-generated log messages for graceful shutdown.
/// </summary>
internal static partial class ShutdownLog
{
    [LoggerMessage(
        EventId = 300,
        Level = LogLevel.Information,
        Message = "Shutdown started. Completing ingress queue. Pending batches={Pending}")]
    internal static partial void Started(ILogger logger, int pending);
}
