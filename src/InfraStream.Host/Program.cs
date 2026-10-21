using InfraStream.CollectorEngine.Configuration;
using InfraStream.CollectorEngine.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Logging: Dev -> single-line console, Prod -> JSON.
builder.AddInfraStreamLogging();

// Collector Engine + exporters + management endpoints.
//
// Configuration is validated before any DI registrations or Kestrel
// listeners are set up. On failure, we print a clear message to stderr
// and exit with a non-zero code, so the host does not start with an
// invalid configuration.
try
{
    builder.AddCollectorEngine()
        .AddNullExporter()
        .WithManagementEndpoints();
}
catch (ConfigurationValidationException ex)
{
    // The logger is not initialized at this point; write to stderr and
    // exit. Docker/Kubernetes will see the process as failed (non-zero
    // exit code), and the message will appear in `docker logs` /
    // `kubectl logs`.
    Console.Error.WriteLine(ex.Message);
    return 1;
}

var app = builder.Build();

// Management endpoints: /health, /ready, /metrics.
var config = app.Services.GetRequiredService<CollectorEngineConfig>();
app.MapCollectorEngineEndpoints(config.ManagementPort);

// Startup log.
app.Logger.LogInformation(
    "InfraStream Engine started. Ingress={IngressPort}, Management={ManagementPort}, " +
    "Workers={Workers}, QueueCapacity={QueueCapacity}",
    config.IngressPort,
    config.ManagementPort,
    config.WorkerCount,
    config.QueueCapacity);

app.Run();
return 0;
