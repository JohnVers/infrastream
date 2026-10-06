using System.Net.Http.Headers;
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
    public const string ScenarioName = "gateway_ingest";

    private static HttpClient? _httpClient;

    public static ScenarioProps Build(
        string url,
        byte[][] payloads,
        int copies,
        TimeSpan duration,
        bool compress)
    {
        return Scenario.Create(ScenarioName, async context =>
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

                if (compress)
                    request.Content!.Headers.ContentEncoding.Add("br");

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
