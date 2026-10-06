using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;

namespace CollectorEngine.Benchmarks;

/// <summary>
/// Benchmarks the raw Utf8JsonReader — how long token enumeration takes
/// without GetSlice and without OnItemParsed.
/// </summary>
[MemoryDiagnoser]
public class ReaderOnlyBenchmarks
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

    [Benchmark]
    public int Reader_ReadAllTokens()
    {
        var reader = new Utf8JsonReader(_json, isFinalBlock: true, state: default);
        int count = 0;

        while (reader.Read())
        {
            count++;
        }

        return count;
    }

    [Benchmark]
    public int Reader_ReadAndSkip()
    {
        var reader = new Utf8JsonReader(_json, isFinalBlock: true, state: default);
        int count = 0;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                reader.Read();
                reader.Skip();
            }
            count++;
        }

        return count;
    }
}
