using System.Net.Http.Headers;
using LoadTests.Payloads;
using NBomber.CSharp;
using NBomber.Http.CSharp;
using NBomber.Contracts;

namespace LoadTests.Scenarios;

/// <summary>
/// Scenario: POST a telemetry batch to the gateway.
/// Closed-loop load model (KeepConstant).
/// </summary>
public static class GatewayIngestScenario
{
    private static HttpClient? _httpClient;

    /// <summary>
    /// Returns the NBomber scenario name for a given encoding.
    /// The encoding is part of the name so that reports from different
    /// encodings are distinguishable.
    /// </summary>
    public static string GetScenarioName(PayloadEncoding encoding)
        => $"gateway_ingest_{encoding.ToString().ToLowerInvariant()}";

    /// <summary>
    /// Builds a scenario for a specific payload encoding.
    /// </summary>
    public static ScenarioProps Build(
        string url,
        byte[][] payloads,
        int copies,
        TimeSpan duration,
        PayloadEncoding encoding)
    {
        string scenarioName = GetScenarioName(encoding);

        return Scenario.Create(scenarioName, async context =>
            {
                int idx = (int)(context.InvocationNumber % payloads.Length);
                byte[] payload = payloads[idx];

                var httpClient = _httpClient
                                 ?? throw new InvalidOperationException("HttpClient not initialized");

                var request = Http.CreateRequest("POST", url)
                    .WithHeader("X-Node-Id", "load-generator-01")
                    .WithHeader("X-Environment", "production")
                    .WithHeader("X-Api-Key", "system-gateway-secure-token-777")
                    .WithBody(new ByteArrayContent(payload));

                switch (encoding)
                {
                    case PayloadEncoding.Brotli:
                        request.Content!.Headers.ContentEncoding.Add("br");
                        break;
                    case PayloadEncoding.Gzip:
                        request.Content!.Headers.ContentEncoding.Add("gzip");
                        break;
                    case PayloadEncoding.Identity:
                        // No Content-Encoding header.
                        break;
                }

                request.Content!.Headers.ContentType = new MediaTypeHeaderValue("application/json")
                {
                    CharSet = "utf-8"
                };

                var response = await Http.Send(httpClient, request);
                return response;
            })
            .WithInit(context =>
            {
                var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                _httpClient = client;
                return Task.CompletedTask;
            })
            .WithoutWarmUp()
            .WithLoadSimulations(
                Simulation.KeepConstant(copies: copies, during: duration)
            );
    }
}
