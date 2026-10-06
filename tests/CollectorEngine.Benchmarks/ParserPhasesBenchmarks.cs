using System.Text;
using BenchmarkDotNet.Attributes;
using InfraStream.Core.Models;
using InfraStream.CollectorEngine.Parsing;

namespace CollectorEngine.Benchmarks;

/// <summary>
/// Decomposition of TelemetryBatchParser by phase:
///   - NoopConsumer: parsing only (Utf8JsonReader + GetSlice), without OnItemParsed.
///   - RecordingConsumer: full parsing + OnItemParsed.
///
/// The difference between them is the cost of OnItemParsed.
/// </summary>
[MemoryDiagnoser]
public class ParserPhasesBenchmarks
{
    private byte[] _json = null!;

    [Params(1_000, 10_000)]
    public int BatchSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var sb = new StringBuilder(BatchSize * 300);
        sb.Append("{\"node_id\":\"load-generator-01\",\"environment\":\"production\",\"payload\":[");
        for (int i = 0; i < BatchSize; i++)
        {
            if (i > 0) sb.Append(',');

            sb.Append("{\"timestamp\":\"2026-09-30T00:00:00.000000Z\",");
            sb.Append("\"level\":\"INFO\",");
            sb.Append("\"message\":\"User logged in successfully [req=abc12345] status=200 card=4111-1111-1111-1111 password=secret_token_123\",");
            sb.Append("\"component\":\"auth-service\",");
            sb.Append("\"trace_id\":\"").Append(Guid.NewGuid().ToString("N")).Append("\",");
            sb.Append("\"span_id\":\"").Append(Guid.NewGuid().ToString("N").Substring(0, 16)).Append("\"}");
        }
        sb.Append("]}");

        _json = Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Empty consumer — parsing only.
    /// </summary>
    private struct NoopConsumer : ITelemetryItemConsumer
    {
        public void OnItemParsed(in TelemetryItem item) { /* nothing */ }
    }

    /// <summary>
    /// Consumer that reads the fields (exporter emulation).
    /// </summary>
    private struct RecordingConsumer : ITelemetryItemConsumer
    {
        public long TotalBytes;

        public void OnItemParsed(in TelemetryItem item)
        {
            TotalBytes += item.Message.Length
                        + item.Level.Length
                        + item.Component.Length
                        + item.RawExtensionJson.Length;
        }
    }

    [Benchmark(Baseline = true)]
    public int Parse_NoopConsumer()
    {
        var consumer = new NoopConsumer();
        TelemetryBatchParser.Parse(_json, _json.Length, ref consumer);
        return 0;
    }

    [Benchmark]
    public long Parse_RecordingConsumer()
    {
        var consumer = new RecordingConsumer();
        TelemetryBatchParser.Parse(_json, _json.Length, ref consumer);
        return consumer.TotalBytes;
    }
}
