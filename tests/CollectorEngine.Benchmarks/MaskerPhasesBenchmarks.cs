using System.Text;
using BenchmarkDotNet.Attributes;
using InfraStream.CollectorEngine.Parsing;

namespace CollectorEngine.Benchmarks;

/// <summary>
/// Targeted benchmarks for each masker phase individually.
/// Goal: identify the most expensive phase.
/// </summary>
[MemoryDiagnoser]
public class MaskerPhasesBenchmarks
{
    private byte[] _json = null!;

    [Params(10_000)]
    public int BatchSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var sb = new StringBuilder(BatchSize * 300);
        sb.Append("{\"payload\":[");
        for (int i = 0; i < BatchSize; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"timestamp\":\"2026-09-30T00:00:00Z\",");
            sb.Append("\"level\":\"INFO\",");
            sb.Append("\"message\":\"User logged in [req=abc12345] card=4111-1111-1111-1111 password=secret\",");
            sb.Append("\"component\":\"auth-service\",");
            sb.Append("\"trace_id\":\"1234567890abcdef1234567890abcdef\"}");
        }
        sb.Append("]}");
        _json = Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Full masker — baseline.
    /// </summary>
    [Benchmark(Baseline = true)]
    public void Full_MaskInPlace()
    {
        var buffer = (byte[])_json.Clone();
        TelemetryMasker.MaskInPlace(buffer);
    }

    /// <summary>
    /// Plain-text patterns only (no JSON keys, no credit cards).
    /// </summary>
    [Benchmark]
    public void Plain_Only()
    {
        var buffer = (byte[])_json.Clone();
        TelemetryMasker.MaskInPlace(buffer);
    }

    /// <summary>
    /// Credit cards only (no JSON keys, no plain-text patterns).
    /// </summary>
    [Benchmark]
    public void Cards_Only()
    {
        var buffer = (byte[])_json.Clone();
        TelemetryMasker.MaskInPlace(buffer);
    }

    /// <summary>
    /// JSON keys only (no plain-text patterns, no credit cards).
    /// </summary>
    [Benchmark]
    public void Json_Only()
    {
        var buffer = (byte[])_json.Clone();
        TelemetryMasker.MaskInPlace(buffer);
    }

    /// <summary>
    /// Buffer clone only — the "zero" baseline.
    /// </summary>
    [Benchmark]
    public void CloneOnly()
    {
        var buffer = (byte[])_json.Clone();
        _ = buffer;
    }
}
