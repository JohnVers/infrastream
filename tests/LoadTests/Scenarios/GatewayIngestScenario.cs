using System.Net;
using System.Net.Http.Headers;
using LoadTests.Payloads;
using NBomber.CSharp;
using NBomber.Contracts;

namespace LoadTests.Scenarios;

/// <summary>
/// Scenario: POST a telemetry batch to the gateway.
/// Closed-loop load model (KeepConstant).
/// </summary>
public static class GatewayIngestScenario
{
    private static HttpClient? _httpClient;
    private static Version _httpVersion = HttpVersion.Version11;
    private static bool _useHttp2;

    /// <summary>
    /// Returns the NBomber scenario name for a given encoding.
    /// </summary>
    public static string GetScenarioName(PayloadEncoding encoding)
        => $"gateway_ingest_{encoding.ToString().ToLowerInvariant()}";

    /// <summary>
    /// Builds a scenario for a specific payload encoding.
    /// </summary>
    /// <param name="useHttp2">
    /// When <see langword="true"/>, requests are sent with HTTP/2
    /// (prior knowledge, h2c, no TLS).
    /// </param>
    public static ScenarioProps Build(
        string url,
        byte[][] payloads,
        int copies,
        TimeSpan duration,
        PayloadEncoding encoding,
        bool useHttp2)
    {
        string scenarioName = GetScenarioName(encoding);

        return Scenario.Create(scenarioName, async context =>
            {
                int idx = (int)(context.InvocationNumber % payloads.Length);
                byte[] payload = payloads[idx];

                var httpClient = _httpClient
                    ?? throw new InvalidOperationException("HttpClient not initialized");

                var content = new ByteArrayContent(payload);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
                {
                    CharSet = "utf-8"
                };

                switch (encoding)
                {
                    case PayloadEncoding.Brotli:
                        content.Headers.ContentEncoding.Add("br");
                        break;
                    case PayloadEncoding.Gzip:
                        content.Headers.ContentEncoding.Add("gzip");
                        break;
                    case PayloadEncoding.Identity:
                        // No Content-Encoding header.
                        break;
                }

                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = content,
                    Version = _httpVersion,
                    // RequestVersionOrHigher: allow the client to fall back to a lower
                    // version if the server does not support the requested one.
                    VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
                };

                request.Headers.Add("X-Node-Id", "load-generator-01");
                request.Headers.Add("X-Environment", "production");
                request.Headers.Add("X-Api-Key", "system-gateway-secure-token-777");

                try
                {
                    using var response = await httpClient
                        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None)
                        .ConfigureAwait(false);

                    // Diagnostic: log the actual response version once per scenario
                    // to confirm whether h2c negotiation succeeded.
                    if (_useHttp2 && context.InvocationNumber < 3)
                    {
                        context.Logger.Information(
                            "response.Version={0}, request.Version={1}, status={2}",
                            response.Version, request.Version, (int)response.StatusCode);
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        return Response.Fail(
                            statusCode: ((int)response.StatusCode).ToString());
                    }

                    return Response.Ok(
                        statusCode: ((int)response.StatusCode).ToString());
                }
                catch (Exception ex)
                {
                    context.Logger.Warning("Request failed: {0}", ex.Message);
                    return Response.Fail(message: ex.Message);
                }
            })
            .WithInit(context =>
            {
                _useHttp2 = useHttp2;

                var handler = new SocketsHttpHandler
                {
                    EnableMultipleHttp2Connections = useHttp2,
                    // Force cleartext HTTP/2 negotiation for h2c.
                    // (kept for clarity; ignored for non-HTTP/2 clients)
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                };

                var client = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(30),
                };

                if (useHttp2)
                {
                    _httpVersion = HttpVersion.Version20;
                    client.DefaultRequestVersion = HttpVersion.Version20;
                    client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
                }
                else
                {
                    _httpVersion = HttpVersion.Version11;
                    client.DefaultRequestVersion = HttpVersion.Version11;
                    client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
                }

                _httpClient = client;

                context.Logger.Information(
                    "Scenario initialized: useHttp2={0}, defaultVersion={1}, defaultPolicy={2}",
                    useHttp2, client.DefaultRequestVersion, client.DefaultVersionPolicy);

                return Task.CompletedTask;
            })
            .WithoutWarmUp()
            .WithLoadSimulations(
                Simulation.KeepConstant(copies: copies, during: duration)
            );
    }
}
