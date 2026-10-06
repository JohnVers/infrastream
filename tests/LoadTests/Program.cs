using System.Diagnostics;
using LoadTests.Payloads;
using LoadTests.Scenarios;
using NBomber.CSharp;
using NBomber.Contracts.Stats;

namespace LoadTests;

public static class Program
{
    private const string DefaultUrl = "http://infrastream_gateway:5001/api/v1/ingest";
    private const int DefaultBatchSize = 10_000;
    private const int DefaultPoolSize = 32;
    private const int DefaultCopies = 60;
    private const int DefaultDurationSeconds = 60;

    public static int Main(string[] args)
    {
        string url = Environment.GetEnvironmentVariable("LOAD_URL") ?? DefaultUrl;
        int batchSize = ParseInt("LOAD_BATCH_SIZE", DefaultBatchSize);
        int poolSize = ParseInt("LOAD_POOL_SIZE", DefaultPoolSize);
        int copies = ParseInt("LOAD_COPIES", DefaultCopies);
        int durationSeconds = ParseInt("LOAD_DURATION", DefaultDurationSeconds);
        bool compress = !string.Equals(
            Environment.GetEnvironmentVariable("LOAD_COMPRESS"),
            "false",
            StringComparison.OrdinalIgnoreCase);

        Console.WriteLine("=== InfraStream NBomber Load Test ===");
        Console.WriteLine($"URL          : {url}");
        Console.WriteLine($"Batch size   : {batchSize:N0} lines/batch");
        Console.WriteLine($"Pool size    : {poolSize}");
        Console.WriteLine($"Concurrency  : {copies}");
        Console.WriteLine($"Duration     : {durationSeconds}s");
        Console.WriteLine($"Compression  : {(compress ? "Brotli" : "NONE (raw JSON)")}");
        Console.WriteLine();

        var genSw = Stopwatch.StartNew();
        byte[][] payloads = PayloadGenerator.Generate(poolSize, batchSize, seed: 42, compress: compress);
        genSw.Stop();

        Console.WriteLine($"Generated {payloads.Length} payloads in {genSw.ElapsedMilliseconds} ms");
        Console.WriteLine();

        var scenario = GatewayIngestScenario.Build(
            url: url,
            payloads: payloads,
            copies: copies,
            duration: TimeSpan.FromSeconds(durationSeconds),
            compress: compress);

        var result = NBomberRunner
            .RegisterScenarios(scenario)
            .WithTestName("gateway_ingest")
            .WithTestSuite("InfraStream")
            .WithReportFolder("./reports")
            .WithReportFormats(ReportFormat.Html, ReportFormat.Txt)
            .Run();

        var stats = result.ScenarioStats
            .First(s => s.ScenarioName == GatewayIngestScenario.ScenarioName);

        long okBatches = stats.Ok.Request.Count;
        long failBatches = stats.Fail.Request.Count;
        long totalLines = okBatches * batchSize;
        double wallSeconds = stats.Duration.TotalSeconds;

        Console.WriteLine();
        Console.WriteLine(new string('=', 72));
        Console.WriteLine("InfraStream Standalone Network Benchmark Result (NBomber)");
        Console.WriteLine($"Compression         : {(compress ? "Brotli" : "NONE")}");
        Console.WriteLine($"Ran for             : {wallSeconds:F1}s");
        Console.WriteLine($"Total Batches OK    : {okBatches:N0}");
        Console.WriteLine($"Total Batches FAIL  : {failBatches:N0}");
        Console.WriteLine($"Total Lines OK      : {totalLines:N0}");
        Console.WriteLine($"Avg Ingress lines/s : {(wallSeconds > 0 ? totalLines / wallSeconds : 0):N0}");
        Console.WriteLine($"Avg Network RPS     : {(wallSeconds > 0 ? okBatches / wallSeconds : 0):F1}");
        Console.WriteLine($"p50 latency (ms)    : {stats.Ok.Latency.Percent50:F1}");
        Console.WriteLine($"p95 latency (ms)    : {stats.Ok.Latency.Percent95:F1}");
        Console.WriteLine($"p99 latency (ms)    : {stats.Ok.Latency.Percent99:F1}");
        Console.WriteLine($"Max latency (ms)    : {stats.Ok.Latency.MaxMs:F1}");
        Console.WriteLine(new string('=', 72));

        return failBatches == 0 ? 0 : 1;
    }

    private static int ParseInt(string name, int defaultValue)
    {
        string? raw = Environment.GetEnvironmentVariable(name);
        return int.TryParse(raw, out int value) ? value : defaultValue;
    }
}
