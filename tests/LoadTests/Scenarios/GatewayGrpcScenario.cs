using Grpc.Net.Client;
using InfraStream.Contracts;
using NBomber;
using NBomber.CSharp;
using NBomber.Contracts;

namespace LoadTests.Scenarios;

/// <summary>
/// Scenario: send a telemetry batch to the gRPC ingress.
/// Closed-loop load model (KeepConstant).
/// </summary>
public static class GatewayGrpcScenario
{
    public static ScenarioProps Build(
        string url,
        TelemetryBatch[] pool,
        int copies,
        TimeSpan duration)
    {
        var clientPool = new ClientPool<Ingest.IngestClient>();

        return Scenario.Create("grpc_ingest", async context =>
            {
                var client = clientPool.GetClient(context.ScenarioInfo);
                var request = pool[(int)(context.InvocationNumber % pool.Length)];

                try
                {
                    var reply = await client.SendBatchAsync(request);
                    return Response.Ok(statusCode: reply.Accepted.ToString());
                }
                catch (Grpc.Core.RpcException ex)
                {
                    context.Logger.Warning("gRPC failed: {0}", ex.Status.Detail);
                    return Response.Fail(message: ex.Status.Detail);
                }
            })
            .WithInit(context =>
            {
                for (int i = 0; i < copies; i++)
                {
                    var channel = GrpcChannel.ForAddress(url, new GrpcChannelOptions
                    {
                        HttpHandler = new SocketsHttpHandler
                        {
                            EnableMultipleHttp2Connections = true,
                        }
                    });
                    clientPool.AddClient(new Ingest.IngestClient(channel));
                }

                context.Logger.Information(
                    "gRPC scenario initialized: url={0}, copies={1}, pool={2}",
                    url, copies, pool.Length);

                return Task.CompletedTask;
            })
            .WithClean(_ =>
            {
                clientPool.DisposeClients();
                return Task.CompletedTask;
            })
            .WithLoadSimulations(
                Simulation.KeepConstant(copies: copies, during: duration)
            );
    }
}
