using System.Text;
using BenchmarkDotNet.Attributes;
using InfraStream.CollectorEngine.Parsing;

namespace CollectorEngine.Benchmarks;

/// <summary>
/// Performance comparison of masker variants:
///   - Current: SearchValues loop (current, slow)
///   - OldStyle: 8 passes of IndexOf (fast)
///   - Disabled: baseline without masking
/// </summary>
[MemoryDiagnoser]
public class MaskerVariantsBenchmarks
{
    private byte[] _json = null!;

    [Params(1_000, 10_000)]
    public int BatchSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var sb = new StringBuilder(BatchSize * 300);
        sb.Append("{\"payload\":[");
        for (int i = 0; i < BatchSize; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"message\":\"User logged in [req=abc] card=4111-1111-1111-1111 password=secret\",");
            sb.Append("\"component\":\"auth-service\"}");
        }
        sb.Append("]}");
        _json = Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Current masker (SearchValues loop).
    /// </summary>
    [Benchmark(Baseline = true)]
    public void Current_MaskInPlace()
    {
        var buffer = (byte[])_json.Clone();
        TelemetryMasker.MaskInPlace(buffer);
    }

    /// <summary>
    /// No masking — baseline.
    /// </summary>
    [Benchmark]
    public void NoMasking()
    {
        var buffer = (byte[])_json.Clone();
        // Do nothing.
        _ = buffer;
    }
}
