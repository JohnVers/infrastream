using InfraStream.CollectorEngine.Ingress;

namespace InfraStream.CollectorEngine.Logging;

/// <summary>
/// Source-generated log messages for <c>TelemetryProcessorWorker</c>.
/// </summary>
internal static partial class WorkerLog
{
    [LoggerMessage(
        EventId = 200,
        Level = LogLevel.Information,
        Message = "Worker {Index} started.")]
    internal static partial void Started(ILogger logger, int index);

    [LoggerMessage(
        EventId = 201,
        Level = LogLevel.Information,
        Message = "Worker {Index} stopped. Batches={Batches}, Items={Items}, Errors={Errors}")]
    internal static partial void Stopped(
        ILogger logger,
        int index,
        long batches,
        long items,
        long errors);

    [LoggerMessage(
        EventId = 202,
        Level = LogLevel.Warning,
        Message = "Worker {Index}: no decoder for encoding {Encoding}. NodeId={NodeId}, Len={Len}")]
    internal static partial void NoDecoder(
        ILogger logger,
        int index,
        ContentEncoding encoding,
        string nodeId,
        int len);

    [LoggerMessage(
        EventId = 203,
        Level = LogLevel.Warning,
        Message = "Worker {Index}: malformed payload. Encoding={Encoding}, NodeId={NodeId}, Len={Len}")]
    internal static partial void MalformedPayload(
        ILogger logger,
        Exception exception,
        int index,
        ContentEncoding encoding,
        string nodeId,
        int len);

    [LoggerMessage(
        EventId = 204,
        Level = LogLevel.Error,
        Message = "Worker {Index}: error processing batch. NodeId={NodeId}, Len={Len}")]
    internal static partial void BatchProcessingError(
        ILogger logger,
        Exception exception,
        int index,
        string nodeId,
        int len);
}
