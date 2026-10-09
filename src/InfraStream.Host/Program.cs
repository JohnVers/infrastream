using InfraStream.CollectorEngine.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Logging: Dev -> single-line console, Prod -> JSON.
builder.AddInfraStreamLogging();

// Collector Engine + exporters + management endpoints.
builder.AddCollectorEngine()
    .AddNullExporter()
    .WithManagementEndpoints();

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
