using InfraStream.CollectorEngine.Extensions;

// Minimal ASP.NET Core host with InfraStream embedded.
//
// Integration is two lines:
//   - AddInfraStreamLogging()  -> Dev: SimpleConsole, Prod: JsonConsole
//   - AddCollectorEngine()     -> registers the telemetry ingress
//         .AddNullExporter()   -> default zero-overhead sink
//
// Everything else below is a normal ASP.NET Core minimal API.

var builder = WebApplication.CreateBuilder(args);

// --- InfraStream integration -------------------------------------------------
builder.AddInfraStreamLogging();

builder.AddCollectorEngine()
    .AddNullExporter();

// To see the parsed and masked telemetry on the console, replace the line
// above with:
//
// builder.AddCollectorEngine()
//        .AddConsoleExporter();

// --- Application HTTP API ----------------------------------------------------
// The API uses the ASP.NET Core default (http://localhost:5000 in development).
// The InfraStream telemetry ingress listens on port 5005 by default, so there
// is no clash with the ASP.NET Core dev ports (5000 for HTTP, 5001 for HTTPS).

var app = builder.Build();

app.MapGet("/", () => "Embedded ASP.NET Core + InfraStream. See /health.");

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    telemetryIngressPort = 5005
}));

app.Run();
