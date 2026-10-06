using InfraStream.CollectorEngine.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Logging: Dev -> single-line console, Prod -> JSON.
builder.AddInfraStreamLogging();

// Collector Engine + exporters (fluent chain).
builder.AddCollectorEngine()
    .AddNullExporter();

var app = builder.Build();

// Health endpoint.
var config = app.Services.GetRequiredService<CollectorEngineConfig>();

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    workerCount = config.WorkerCount
}));

// Startup log.
app.Logger.LogInformation(
    "InfraStream Engine started. Ingress={IngressPort}, Management={ManagementPort}, " +
    "Workers={Workers}, QueueCapacity={QueueCapacity}",
    config.IngressPort,
    config.ManagementPort,
    config.WorkerCount,
    config.QueueCapacity);

app.Run();
