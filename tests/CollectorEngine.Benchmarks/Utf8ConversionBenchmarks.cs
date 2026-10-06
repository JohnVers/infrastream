using System.Text;
using BenchmarkDotNet.Attributes;

namespace CollectorEngine.Benchmarks;

/// <summary>
/// Benchmarks the cost of UTF-8 → UTF-16 (string) conversion.
///
/// Goal: understand how much time <c>Encoding.UTF8.GetString</c> takes
/// when parsing 10 000 JSON lines.
///
/// If GetString &gt;&gt; 200 µs — a UTF-8 refactor would pay off.
/// If &lt; 200 µs — not critical, leave as is.
/// </summary>
[MemoryDiagnoser]
public class Utf8ConversionBenchmarks
{
    private byte[] _utf8Payload = null!;
    private string[] _jsonStringValues = null!;

    [Params(1_000, 10_000)]
    public int BatchSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Emulate real JSON: 10 000 lines with message, level, component fields.
        // `message` is unique (as in real logs: UUID, req_id, etc.).
        // `level` / `component` are repeated (interned in MessageInterner).
        var sb = new StringBuilder(BatchSize * 300);
        _jsonStringValues = new string[BatchSize];

        for (int i = 0; i < BatchSize; i++)
        {
            string message = $"User logged in successfully [req={Guid.NewGuid():N}] status=200 card=4111-1111-1111-1111 password=secret_token_123";
            _jsonStringValues[i] = message;
        }

        // Build a "raw" JSON-like buffer (no real structure — for conversion measurement only).
        for (int i = 0; i < BatchSize; i++)
        {
            sb.Append("\"message\":\"");
            sb.Append(_jsonStringValues[i]);
            sb.Append("\",");
        }

        _utf8Payload = Encoding.UTF8.GetBytes(sb.ToString());
    }

    // ================================================================
    //  1. BASE MEASUREMENT: current approach (GetString)
    // ================================================================

    /// <summary>
    /// Current approach: each line is converted to string via GetString.
    /// This is what MessageInterner.AsMemory does for unique values.
    /// </summary>
    [Benchmark(Baseline = true)]
    public int Current_GetString_Allocations()
    {
        int totalLength = 0;

        // Emulate: call GetString for each line.
        for (int i = 0; i < _jsonStringValues.Length; i++)
        {
            // In reality, MessageInterner calls GetString over Span<byte>
            // from reader.ValueSpan. Here we emulate via Encoding.
            string s = Encoding.UTF8.GetString(_utf8Payload.AsSpan(0, Math.Min(200, _utf8Payload.Length)));
            totalLength += s.Length;
        }

        return totalLength;
    }

    // ================================================================
    //  2. ALTERNATIVE: GetChars with pooled char[]
    // ================================================================

    /// <summary>
    /// Alternative: UTF-8 → UTF-16 without an intermediate string.
    /// Encoding.UTF8.GetChars writes directly into Span<char>.
    /// With a pooled char[] there are almost no allocations.
    /// </summary>
    [Benchmark]
    public int Alternative_GetChars_Pooled()
    {
        // One shared buffer of 4096 chars (256 bytes UTF-8 ≈ 256 chars).
        char[] buffer = System.Buffers.ArrayPool<char>.Shared.Rent(4096);
        int totalLength = 0;

        try
        {
            for (int i = 0; i < _jsonStringValues.Length; i++)
            {
                var source = _utf8Payload.AsSpan(0, Math.Min(200, _utf8Payload.Length));
                int written = Encoding.UTF8.GetChars(source, buffer);
                totalLength += written;
            }
        }
        finally
        {
            System.Buffers.ArrayPool<char>.Shared.Return(buffer);
        }

        return totalLength;
    }

    // ================================================================
    //  3. NO CONVERSION: byte length only
    // ================================================================

    /// <summary>
    /// No conversion: length count only. Ideal case (UTF-8 everywhere).
    /// </summary>
    [Benchmark]
    public int NoConversion_BytesOnly()
    {
        int totalLength = 0;

        for (int i = 0; i < _jsonStringValues.Length; i++)
        {
            var source = _utf8Payload.AsSpan(0, Math.Min(200, _utf8Payload.Length));
            totalLength += source.Length;
        }

        return totalLength;
    }

    // ================================================================
    //  4. REALISTIC SCENARIO: GetString for all lines with interning
    // ================================================================

    /// <summary>
    /// Realistic scenario: 90% of lines are unique (message),
    /// 10% are interned (level INFO, component nginx).
    /// GetString is called only for the unique ones.
    /// </summary>
    [Benchmark]
    public int Realistic_10Percent_Interned()
    {
        int totalLength = 0;
        int uniqueCount = (_jsonStringValues.Length * 90) / 100;

        for (int i = 0; i < uniqueCount; i++)
        {
            string s = Encoding.UTF8.GetString(_utf8Payload.AsSpan(0, Math.Min(200, _utf8Payload.Length)));
            totalLength += s.Length;
        }

        return totalLength;
    }

    // ================================================================
    //  5. CLONE ONLY: minimal baseline
    // ================================================================

    /// <summary>
    /// Zero point: copy the buffer into a new array.
    /// Shows the memory cost without conversion.
    /// </summary>
    [Benchmark]
    public int CloneOnly()
    {
        byte[] clone = (byte[])_utf8Payload.Clone();
        return clone.Length;
    }
}
