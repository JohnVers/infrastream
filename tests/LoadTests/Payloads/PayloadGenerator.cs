using System.IO.Compression;
using System.Text.Json;

namespace LoadTests.Payloads;

/// <summary>
/// Generator for a pool of Brotli-compressed (or uncompressed) JSON batches.
/// The pool is created ONCE before the test starts and reused.
/// </summary>
public static class PayloadGenerator
{
    private static readonly string[] Services = { "nginx", "auth-service", "payment-processor" };

    private static readonly string[] Actions =
    {
        "User logged in successfully",
        "Payment processed for order",
        "Order created",
        "Cache miss",
        "Database query executed"
    };

    private static readonly string[] Levels =
        Enumerable.Repeat("INFO", 90)
            .Concat(Enumerable.Repeat("WARN", 8))
            .Concat(Enumerable.Repeat("ERROR", 2))
            .ToArray();

    /// <summary>
    /// Generates <paramref name="poolSize"/> batches of <paramref name="batchSize"/> lines each.
    /// If <paramref name="compress"/> is <see langword="true"/>, compresses with Brotli;
    /// otherwise returns raw JSON.
    /// </summary>
    public static byte[][] Generate(int poolSize, int batchSize, int seed = 42, bool compress = true)
    {
        var random = new Random(seed);
        var result = new byte[poolSize][];

        long totalRaw = 0;
        long totalStored = 0;

        for (int i = 0; i < poolSize; i++)
        {
            byte[] jsonBytes = BuildJson(random, batchSize);
            totalRaw += jsonBytes.Length;

            result[i] = compress ? BrotliCompress(jsonBytes) : jsonBytes;
            totalStored += result[i].Length;
        }

        Console.WriteLine($"[PayloadGenerator] poolSize={poolSize}, batchSize={batchSize}, compress={compress}");
        Console.WriteLine($"[PayloadGenerator] Total raw: {totalRaw / 1024 / 1024} MB");
        Console.WriteLine($"[PayloadGenerator] Total stored: {totalStored / 1024 / 1024} MB");
        Console.WriteLine($"[PayloadGenerator] Avg raw per batch: {totalRaw / poolSize / 1024} KB");
        Console.WriteLine($"[PayloadGenerator] Avg stored per batch: {totalStored / poolSize / 1024} KB");
        if (compress && totalStored > 0)
            Console.WriteLine($"[PayloadGenerator] Compression ratio: {(double)totalRaw / totalStored:F2}×");

        return result;
    }

    private static byte[] BuildJson(Random random, int batchSize)
    {
        using var ms = new MemoryStream(capacity: batchSize * 300);
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            writer.WriteString("node_id", "load-generator-01");
            writer.WriteString("environment", "production");
            writer.WriteStartArray("payload");

            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ");

            for (int j = 0; j < batchSize; j++)
            {
                string level = Levels[random.Next(Levels.Length)];
                string action = Actions[random.Next(Actions.Length)];
                string detail = Guid.NewGuid().ToString("N").Substring(0, 8);

                string message =
                    $"{action} [req={detail}] status=200 " +
                    "card=4111-1111-1111-1111 password=secret_token_123";

                writer.WriteStartObject();
                writer.WriteString("timestamp", now);
                writer.WriteString("level", level);
                writer.WriteString("message", message);
                writer.WriteString("component", Services[random.Next(Services.Length)]);
                writer.WriteString("trace_id", Guid.NewGuid().ToString("N"));
                writer.WriteString("span_id", Guid.NewGuid().ToString("N").Substring(0, 16));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return ms.ToArray();
    }

    private static byte[] BrotliCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            brotli.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }
}
