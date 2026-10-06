using System.Text;
using System.Text.Json;
using InfraStream.Core.Models;
using InfraStream.CollectorEngine.Parsing;
using Xunit;

namespace CollectorEngine.Tests.Parsing;

public class TelemetryBatchParserTests
{
    // ================================================================
    //  Test consumer — struct holding a reference to List<TelemetryItem>
    // ================================================================
    private struct RecordingConsumer : ITelemetryItemConsumer
    {
        private readonly List<TelemetryItem> _items;

        public RecordingConsumer(List<TelemetryItem> items)
        {
            _items = items;
        }

        public void OnItemParsed(in TelemetryItem item)
        {
            // Copy — `item` points into the buffer, which will be returned to
            // the pool (or mutated) after Parse returns.
            _items.Add(new TelemetryItem(
                item.Timestamp,
                item.Level.ToArray(),
                item.Message.ToArray(),
                item.Component.ToArray(),
                item.Attributes.ToArray(),
                item.RawExtensionJson.ToArray()));
        }
    }

    // ================================================================
    //  No-op consumer for zero-alloc tests
    // ================================================================
    private struct NoopConsumer : ITelemetryItemConsumer
    {
        public void OnItemParsed(in TelemetryItem item)
        {
            // Intentionally do nothing.
        }
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private static byte[] BuildJson(string json) => Encoding.UTF8.GetBytes(json);

    private static string AsString(ReadOnlyMemory<byte> mem) =>
        Encoding.UTF8.GetString(mem.Span);

    /// <summary>
    /// Parses JSON and returns the list of items (with copies of the fields).
    /// Copies are required because the buffer is invalid after Parse returns.
    /// </summary>
    private static List<TelemetryItem> Parse(string json)
    {
        byte[] bytes = BuildJson(json);
        var items = new List<TelemetryItem>();
        var consumer = new RecordingConsumer(items);
        TelemetryBatchParser.Parse(bytes, bytes.Length, ref consumer);
        return items;
    }

    // ================================================================
    //  BASIC TESTS
    // ================================================================

    [Fact]
    public void Parses_Simple_Item()
    {
        string json = """
        {"payload":[{"timestamp":"2024-01-01T00:00:00Z","level":"INFO","message":"hello","component":"auth-service"}]}
        """;

        var items = Parse(json);

        Assert.Single(items);
        var item = items[0];

        Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), item.Timestamp);
        Assert.Equal("INFO", AsString(item.Level));
        Assert.Equal("hello", AsString(item.Message));
        Assert.Equal("auth-service", AsString(item.Component));
    }

    [Fact]
    public void Parses_Multiple_Items()
    {
        string json = """
        {"payload":[
            {"timestamp":"2024-01-01T00:00:00Z","level":"INFO","message":"m1","component":"a"},
            {"timestamp":"2024-01-01T00:00:01Z","level":"WARN","message":"m2","component":"b"},
            {"timestamp":"2024-01-01T00:00:02Z","level":"ERROR","message":"m3","component":"c"}
        ]}
        """;

        var items = Parse(json);

        Assert.Equal(3, items.Count);
        Assert.Equal("m1", AsString(items[0].Message));
        Assert.Equal("m2", AsString(items[1].Message));
        Assert.Equal("m3", AsString(items[2].Message));
    }

    [Fact]
    public void Parses_Empty_Payload()
    {
        string json = """{"payload":[]}""";
        var items = Parse(json);
        Assert.Empty(items);
    }

    [Fact]
    public void Missing_Payload_Returns_Nothing()
    {
        string json = """{"node_id":"n1","environment":"prod"}""";
        var items = Parse(json);
        Assert.Empty(items);
    }

    [Fact]
    public void Invalid_Json_Throws()
    {
        string json = """{"payload":[{""";
        byte[] bytes = BuildJson(json);

        var items = new List<TelemetryItem>();
        var consumer = new RecordingConsumer(items);

        Assert.ThrowsAny<JsonException>(() =>
        {
            TelemetryBatchParser.Parse(bytes, bytes.Length, ref consumer);
        });
    }

    // ================================================================
    //  INTERNED TOKENS
    // ================================================================

    [Theory]
    [InlineData("INFO")]
    [InlineData("WARN")]
    [InlineData("ERROR")]
    [InlineData("DEBUG")]
    [InlineData("TRACE")]
    [InlineData("FATAL")]
    public void Known_Levels_Use_Interned_Memory(string level)
    {
        string json = $$"""{"payload":[{"level":"{{level}}","message":"m"}]}""";
        var items = Parse(json);

        Assert.Single(items);
        Assert.Equal(level, AsString(items[0].Level));
    }

    [Theory]
    [InlineData("nginx")]
    [InlineData("auth-service")]
    [InlineData("payment-processor")]
    public void Known_Components_Use_Interned_Memory(string component)
    {
        string json = $$"""{"payload":[{"component":"{{component}}","message":"m"}]}""";
        var items = Parse(json);

        Assert.Single(items);
        Assert.Equal(component, AsString(items[0].Component));
    }

    // ================================================================
    //  CUSTOM ATTRIBUTES
    // ================================================================

    [Fact]
    public void Parses_Custom_Attributes()
    {
        string json = """
        {"payload":[{"message":"m","trace_id":"abc","span_id":"def"}]}
        """;

        var items = Parse(json);

        Assert.Single(items);
        var attrs = items[0].Attributes;

        Assert.Equal(2, attrs.Length);
        Assert.Equal("trace_id", AsString(attrs.Span[0].Key));
        Assert.Equal("abc", AsString(attrs.Span[0].Value));
        Assert.Equal("span_id", AsString(attrs.Span[1].Key));
        Assert.Equal("def", AsString(attrs.Span[1].Value));
    }

    [Fact]
    public void Limits_Attributes_To_16()
    {
        // Generate 20 custom fields — expect the limit of 16.
        var sb = new StringBuilder("""{"payload":[{"message":"m",""");
        for (int i = 0; i < 20; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append($"\"field{i}\":\"value{i}\"");
        }
        sb.Append("}]}");

        var items = Parse(sb.ToString());

        Assert.Single(items);
        Assert.Equal(16, items[0].Attributes.Length);
    }

    // ================================================================
    //  RAW EXTENSION JSON
    // ================================================================

    [Fact]
    public void RawExtensionJson_Contains_Original_Object()
    {
        string json = """
        {"payload":[{"timestamp":"2024-01-01T00:00:00Z","level":"INFO","message":"hello"}]}
        """;

        var items = Parse(json);

        Assert.Single(items);
        string raw = AsString(items[0].RawExtensionJson);

        Assert.StartsWith("{", raw);
        Assert.EndsWith("}", raw);
        Assert.Contains("\"message\":\"hello\"", raw);
    }

    // ================================================================
    //  ESCAPED STRINGS
    // ================================================================

    [Fact]
    public void Parses_Message_With_Escaped_Quotes()
    {
        // In JSON: "message": "user said \"hi\""
        string json = """{"payload":[{"message":"user said \"hi\""}]}""";

        var items = Parse(json);

        Assert.Single(items);
        Assert.Equal("user said \"hi\"", AsString(items[0].Message));
    }

    [Fact]
    public void Parses_Message_With_Backslash()
    {
        string json = """{"payload":[{"message":"path: C:\\temp\\file"}]}""";
        var items = Parse(json);

        Assert.Single(items);
        Assert.Equal("path: C:\\temp\\file", AsString(items[0].Message));
    }

    // ================================================================
    //  ZERO-ALLOC CHECKS
    // ================================================================

    [Fact]
    public void Parse_Does_Not_Allocate_On_Known_Tokens()
    {
        string json = """
        {"payload":[{"timestamp":"2024-01-01T00:00:00Z","level":"INFO","message":"unique message","component":"nginx"}]}
        """;

        byte[] bytes = BuildJson(json);

        // Warm-up — fill ArrayPool<LogAttribute> and JIT the hot path.
        for (int i = 0; i < 100; i++)
        {
            var noop = new NoopConsumer();
            TelemetryBatchParser.Parse(bytes, bytes.Length, ref noop);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            var noop = new NoopConsumer();
            TelemetryBatchParser.Parse(bytes, bytes.Length, ref noop);
        }
        long after = GC.GetAllocatedBytesForCurrentThread();

        long allocated = after - before;

        // Expect ~0 allocations per line. Allow up to ~500 bytes per batch
        // (ArrayPool may allocate occasionally, but not 4 KB × 1000).
        Assert.True(allocated < 500_000,
            $"Parser allocated {allocated} bytes for 1000 iterations. Expected < 500 KB.");
    }

    // ================================================================
    //  EDGE CASES
    // ================================================================

    [Fact]
    public void Handles_Empty_Message()
    {
        string json = """{"payload":[{"message":""}]}""";
        var items = Parse(json);

        Assert.Single(items);
        Assert.True(items[0].Message.IsEmpty);
    }

    [Fact]
    public void Handles_Missing_Message()
    {
        string json = """{"payload":[{"level":"INFO"}]}""";
        var items = Parse(json);

        Assert.Single(items);
        Assert.True(items[0].Message.IsEmpty);
    }

    [Fact]
    public void Handles_Missing_Timestamp()
    {
        string json = """{"payload":[{"message":"m"}]}""";
        var items = Parse(json);

        Assert.Single(items);
        Assert.Equal(default(DateTime), items[0].Timestamp);
    }

    [Fact]
    public void Skips_Non_Object_Array_Items()
    {
        // The array contains a number and a string — they must be skipped.
        string json = """{"payload":[123,"string",{"message":"valid"}]}""";
        var items = Parse(json);

        Assert.Single(items);
        Assert.Equal("valid", AsString(items[0].Message));
    }
}
