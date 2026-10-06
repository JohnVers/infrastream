using InfraStream.CollectorEngine.Extensions;
using InfraStream.Examples.CustomExporter;

// Minimal ASP.NET Core host demonstrating a custom ITelemetryExporter.
//
// The integration is the same as in examples/embedded-aspnet, except that
// instead of the built-in NullExporter we register our own FileExporter:
//
//   builder.AddCollectorEngine()
//          .AddCustomExporter<FileExporter>();
//
// See FileExporter.cs for the ownership contract in action
// (ReadOnlyMemory<byte> values are copied before they are retained).

var builder = WebApplication.CreateBuilder(args);

// --- InfraStream integration -------------------------------------------------
builder.AddInfraStreamLogging();

builder.AddCollectorEngine()
    .AddCustomExporter<FileExporter>();

// --- Application HTTP API ----------------------------------------------------
// The API uses the ASP.NET Core default (http://localhost:5000 in development).
// The InfraStream telemetry ingress listens on port 5005 by default, so there
// is no clash with the ASP.NET Core dev ports (5000 for HTTP, 5001 for HTTPS).

var app = builder.Build();

app.MapGet("/", () => "Custom exporter example. See /health.");

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    telemetryIngressPort = 5005,
    exporter = "FileExporter"
}));

app.Run();
