using InfraStream.Contracts;

namespace LoadTests.Payloads;

/// <summary>
/// Generator for a pool of protobuf <see cref="TelemetryBatch"/> instances.
/// Created ONCE before the test and reused.
/// </summary>
public static class GrpcPayloadGenerator
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

    public static TelemetryBatch[] Generate(int poolSize, int batchSize, int seed = 42)
    {
        var random = new Random(seed);
        var result = new TelemetryBatch[poolSize];
        for (int i = 0; i < poolSize; i++)
            result[i] = BuildBatch(random, batchSize);

        Console.WriteLine($"[GrpcPayloadGenerator] poolSize={poolSize}, batchSize={batchSize}");
        return result;
    }

    private static TelemetryBatch BuildBatch(Random random, int batchSize)
    {
        var batch = new TelemetryBatch
        {
            NodeId = "load-generator-01",
            Environment = "production",
        };

        string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ");
        for (int i = 0; i < batchSize; i++)
        {
            string level = random.Next(10) < 9 ? "INFO" : "WARN";
            string action = Actions[random.Next(Actions.Length)];
            string detail = Guid.NewGuid().ToString("N").AsSpan(0, 8).ToString();

            batch.Payload.Add(new TelemetryRecord
            {
                Timestamp = now,
                Level = level,
                Message = $"{action} [req={detail}] status=200 " +
                          "card=4111-1111-1111-1111 password=secret_token_123",
                Component = Services[random.Next(Services.Length)],
                TraceId = Guid.NewGuid().ToString("N"),
                SpanId = Guid.NewGuid().ToString("N").AsSpan(0, 16).ToString(),
            });
        }

        return batch;
    }
}
