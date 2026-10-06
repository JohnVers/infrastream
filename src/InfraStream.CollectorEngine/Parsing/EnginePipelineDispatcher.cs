using InfraStream.Core.Exporters;
using InfraStream.Core.Models;
using InfraStream.Core.Plugins;

namespace InfraStream.CollectorEngine.Parsing;

/// <summary>
/// High-throughput dispatcher for the log-processing pipeline.
/// Operates on UTF-8 bytes — no conversion to <see cref="string"/>.
/// </summary>
/// <remarks>
/// Implements <see cref="ITelemetryItemConsumer"/> and is invoked by
/// <see cref="TelemetryBatchParser"/> for every parsed telemetry item.
/// The dispatcher runs the configured processors first (as a filtering
/// stage) and forwards the item to the exporters only if all processors
/// accept it.
/// </remarks>
public struct EnginePipelineDispatcher : ITelemetryItemConsumer
{
    private readonly ITelemetryProcessor[] _processors;
    private readonly ITelemetryExporter[] _exporters;

    private readonly ReadOnlyMemory<byte> _nodeIdMemory;
    private readonly ReadOnlyMemory<byte> _environmentMemory;

    /// <summary>
    /// Number of items that passed all processors and were forwarded
    /// to the exporters.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="EnginePipelineDispatcher"/> struct.
    /// </summary>
    /// <param name="processors">
    /// Processors executed in order as a filtering stage.
    /// </param>
    /// <param name="exporters">
    /// Exporters invoked once an item passes all processors.
    /// </param>
    /// <param name="nodeId">Identifier of the node emitting telemetry.</param>
    /// <param name="environment">Environment name (e.g. <c>prod</c>).</param>
    public EnginePipelineDispatcher(
        ITelemetryProcessor[] processors,
        ITelemetryExporter[] exporters,
        string nodeId,
        string environment)
    {
        _processors = processors;
        _exporters = exporters;

        _nodeIdMemory = Encoding.UTF8.GetBytes(nodeId);
        _environmentMemory = Encoding.UTF8.GetBytes(environment);

        Count = 0;
    }

    /// <inheritdoc />
    public void OnItemParsed(in TelemetryItem item)
    {
        var context = new LogProcessingContext
        {
            NodeId = _nodeIdMemory.Span,
            Environment = _environmentMemory.Span,
            IsValid = true
        };

        for (int i = 0; i < _processors.Length; i++)
        {
            _processors[i].Process(ref context);
            if (!context.IsValid) return;
        }

        Count++;
        for (int i = 0; i < _exporters.Length; i++)
        {
            _exporters[i].ExportItem(in item);
        }
    }
}
