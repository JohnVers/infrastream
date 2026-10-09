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
        PayloadEncoding[] encodings = ParseEncodings(
            Environment.GetEnvironmentVariable("LOAD_ENCODINGS") ?? "identity,br,gzip");

        Console.WriteLine("=== InfraStream NBomber Load Test ===");
        Console.WriteLine($"URL          : {url}");
        Console.WriteLine($"Batch size   : {batchSize:N0} lines/batch");
        Console.WriteLine($"Pool size    : {poolSize}");
        Console.WriteLine($"Concurrency  : {copies}");
        Console.WriteLine($"Duration     : {durationSeconds}s per encoding");
        Console.WriteLine($"Encodings    : {string.Join(", ", encodings)}");
        Console.WriteLine();

        var summaries = new List<RunSummary>();

        foreach (var encoding in encodings)
        {
            Console.WriteLine(new string('-', 72));
            Console.WriteLine($">>> Running encoding: {encoding}");
            Console.WriteLine(new string('-', 72));

            var genSw = Stopwatch.StartNew();
            byte[][] payloads = PayloadGenerator.Generate(
                poolSize, batchSize, encoding, seed: 42);
            genSw.Stop();

            Console.WriteLine($"Generated {payloads.Length} payloads in {genSw.ElapsedMilliseconds} ms");
            Console.WriteLine();

            var scenario = GatewayIngestScenario.Build(
                url: url,
                payloads: payloads,
                copies: copies,
                duration: TimeSpan.FromSeconds(durationSeconds),
                encoding: encoding);

            string scenarioName = GatewayIngestScenario.GetScenarioName(encoding);
            string testName = $"gateway_ingest_{encoding.ToString().ToLowerInvariant()}";

            var result = NBomberRunner
                .RegisterScenarios(scenario)
                .WithTestName(testName)
                .WithTestSuite("InfraStream")
                .WithReportFolder($"./reports/{testName}")
                .WithReportFormats(ReportFormat.Html, ReportFormat.Txt)
                .Run();

            var stats = result.ScenarioStats.First(s => s.ScenarioName == scenarioName);
            var summary = RunSummary.FromStats(encoding, stats, batchSize);
            summaries.Add(summary);

            PrintSummary(summary);
        }

        PrintComparison(summaries);

        return summaries.All(s => s.FailBatches == 0) ? 0 : 1;
    }

    private static PayloadEncoding[] ParseEncodings(string raw)
    {
        var result = new List<PayloadEncoding>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "identity":
                case "raw":
                case "none":
                    result.Add(PayloadEncoding.Identity);
                    break;
                case "br":
                case "brotli":
                    result.Add(PayloadEncoding.Brotli);
                    break;
                case "gzip":
                case "gz":
                    result.Add(PayloadEncoding.Gzip);
                    break;
                default:
                    throw new ArgumentException($"Unknown encoding: '{part}'. Use: identity, br, gzip.");
            }
        }

        if (result.Count == 0)
            throw new ArgumentException("No valid encodings specified in LOAD_ENCODINGS.");

        return result.ToArray();
    }

    private static void PrintSummary(RunSummary s)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 72));
        Console.WriteLine($"InfraStream Standalone Network Benchmark Result (NBomber)");
        Console.WriteLine($"Encoding            : {s.Encoding}");
        Console.WriteLine($"Ran for             : {s.WallSeconds:F1}s");
        Console.WriteLine($"Total Batches OK    : {s.OkBatches:N0}");
        Console.WriteLine($"Total Batches FAIL  : {s.FailBatches:N0}");
        Console.WriteLine($"Total Lines OK      : {s.TotalLines:N0}");
        Console.WriteLine($"Avg Ingress lines/s : {s.LinesPerSecond:N0}");
        Console.WriteLine($"Avg Network RPS     : {s.Rps:F1}");
        Console.WriteLine($"p50 latency (ms)    : {s.P50:F1}");
        Console.WriteLine($"p95 latency (ms)    : {s.P95:F1}");
        Console.WriteLine($"p99 latency (ms)    : {s.P99:F1}");
        Console.WriteLine($"Max latency (ms)    : {s.MaxMs:F1}");
        Console.WriteLine(new string('=', 72));
        Console.WriteLine();
    }

    private static void PrintComparison(List<RunSummary> summaries)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 100));
        Console.WriteLine("Comparison across encodings");
        Console.WriteLine(new string('=', 100));
        Console.WriteLine(
            $"{"Encoding",-12} | {"RPS",10} | {"Lines/s",12} | {"p50 (ms)",10} | {"p95 (ms)",10} | {"p99 (ms)",10} | {"FAIL",6}");
        Console.WriteLine(new string('-', 100));

        foreach (var s in summaries)
        {
            Console.WriteLine(
                $"{s.Encoding,-12} | {s.Rps,10:F1} | {s.LinesPerSecond,12:N0} | {s.P50,10:F1} | {s.P95,10:F1} | {s.P99,10:F1} | {s.FailBatches,6:N0}");
        }

        Console.WriteLine(new string('=', 100));
    }

    private static int ParseInt(string name, int defaultValue)
    {
        string? raw = Environment.GetEnvironmentVariable(name);
        return int.TryParse(raw, out int value) ? value : defaultValue;
    }

    private sealed record RunSummary(
        PayloadEncoding Encoding,
        long OkBatches,
        long FailBatches,
        long TotalLines,
        double WallSeconds,
        double Rps,
        double LinesPerSecond,
        double P50,
        double P95,
        double P99,
        double MaxMs)
    {
        public static RunSummary FromStats(
            PayloadEncoding encoding,
            NBomber.Contracts.Stats.ScenarioStats stats,
            int batchSize)
        {
            long okBatches = stats.Ok.Request.Count;
            long failBatches = stats.Fail.Request.Count;
            long totalLines = okBatches * batchSize;
            double wallSeconds = stats.Duration.TotalSeconds;

            return new RunSummary(
                Encoding: encoding,
                OkBatches: okBatches,
                FailBatches: failBatches,
                TotalLines: totalLines,
                WallSeconds: wallSeconds,
                Rps: wallSeconds > 0 ? okBatches / wallSeconds : 0,
                LinesPerSecond: wallSeconds > 0 ? totalLines / wallSeconds : 0,
                P50: stats.Ok.Latency.Percent50,
                P95: stats.Ok.Latency.Percent95,
                P99: stats.Ok.Latency.Percent99,
                MaxMs: stats.Ok.Latency.MaxMs);
        }
    }
}
