# Embedded ASP.NET Core Example

Minimal example showing how to embed InfraStream directly into an existing
ASP.NET Core application.

The goal is to demonstrate that integration takes **two lines** in
`Program.cs`, while the application keeps its normal HTTP API and
configuration model.

---

## What this example shows

- Registering InfraStream inside an existing `WebApplicationBuilder`.
- Receiving telemetry batches over raw TCP on port `5005`.
- Keeping the application's own HTTP API (`/health`, `/`) intact.
- Reusing the application's DI container, `IOptions`, and `appsettings.json`.
- Optional: seeing the telemetry flow with `ConsoleExporter` instead of
  `NullExporter`.

---

## When to use embedded mode

Embedded mode is a good fit when the telemetry ingress should live **inside
an existing ASP.NET Core process** rather than in a separate gateway
container or sidecar.

| # | Scenario | Why embedded works |
|---|----------|-------------------|
| 1 | You already have an ASP.NET Core service and want it to accept telemetry over the network | Two lines in `Program.cs`, no extra process, no IPC |
| 2 | Internal gateway for microservices | One or two gateway services accept telemetry from many internal services; PII masking is centralized |
| 3 | Multi-tenant SaaS receiving telemetry from client-side agents | The API gateway already authenticates clients; `X-Node-Id` / `X-Environment` identify tenants |
| 5 | Local debug / troubleshooting | `dotnet run` brings up the API **and** the telemetry ingress; no Kafka, no Loki, no Docker |
| 7 | Legacy integration | A thin .NET adapter wraps a legacy system; the adapter is the single long-lived process |

---

## When NOT to use embedded mode

| Scenario | Why embedded is a poor fit | Better option |
|----------|---------------------------|---------------|
| High telemetry RPS + high API RPS | Telemetry competes for CPU with the API; API latency suffers | Sidecar or dedicated Deployment |
| Different lifecycles (API restarts often, gateway rarely) | Each API restart may drop buffered telemetry | Separate process |
| Different security perimeters (API public, telemetry internal) | Mixing ports in one process complicates firewalling | Separate process |
| Compliance: telemetry must not share a process with PII APIs | Audit / isolation requirements | Separate process |

If any of the rows above apply, see `examples/sidecar-k8s/` instead.

---

## Project layout

```
embedded-aspnet/
├── Program.cs             # 2 InfraStream lines + minimal API
├── appsettings.json       # minimal InfraStream configuration
├── EmbeddedAspNet.csproj
├── curl-example.sh        # sends three test batches (plain, PII, Brotli)
└── README.md              # this file
```

`ConsoleExporter` lives in the main project
(`src/InfraStream.Core/Exporters/ConsoleExporter.cs`) and is registered via
`.AddConsoleExporter()` — no extra file is needed in this example.

---

## Prerequisites

- .NET 10 SDK.
- `curl` for sending test batches.
- `brotli` CLI for the compressed test batch (request #3 in `curl-example.sh`).
  - macOS: `brew install brotli`
  - Debian / Ubuntu: `sudo apt install brotli`

---

## Running the example

From this directory:

```bash
dotnet run
```

The application starts two listeners:

- **HTTP API** — `http://localhost:5000` (`/` and `/health`).
- **Telemetry ingress** — raw TCP on port `5005`.

The ingress port is chosen to avoid a clash with the ASP.NET Core development
defaults (`5000` for HTTP, `5001` for HTTPS).

---

## Sending a test batch

In a second terminal:

```bash
chmod +x curl-example.sh   # first time only
./curl-example.sh
```

The script sends three requests to `http://localhost:5005`:

1. **Plain batch** — two log lines, no sensitive fields.
2. **PII batch** — log lines with `password`, `api_key`, and a credit-card
   number in the `message` and in custom attributes.
3. **Brotli batch** — the same payload as (2), compressed with
   `Content-Encoding: br`.

With the default `NullExporter`, each batch is accepted (`HTTP 202`) and
counted, but nothing is written anywhere. To actually see the parsed and
masked items, switch to `ConsoleExporter` (see below).

---

## Seeing the telemetry flow

By default the example uses `NullExporter` — a zero-overhead sink that only
counts items. This keeps the example focused on integration.

To print every parsed and masked telemetry item to the console, open
`Program.cs` and replace:

```csharp
builder.AddCollectorEngine()
       .AddNullExporter();
```

with:

```csharp
builder.AddCollectorEngine()
       .AddConsoleExporter();
```

Then re-run `dotnet run` and execute `./curl-example.sh` again. You should
see output similar to:

```
[ConsoleExporter] 2026-10-05T12:35:01.0010000Z INFO auth-service
  message: Login attempt for user=alice from 10.0.0.1
  attributes:
    password = ******
    api_key = **************************
[ConsoleExporter] 2026-10-05T12:35:02.0020000Z ERROR payment-processor
  message: Payment failed for card 4242 **** **** 4242, retrying
  attributes:
    token = ******************
```

Note how:

- `password`, `api_key`, and `token` values are fully replaced with `*`
  (the original length is preserved).
- The credit-card number inside `message` is partially masked
  (positions 4–11 of the 16 digits).

> `ConsoleExporter` is intended for local development only. Do not use it in
> production — writing to the console becomes a bottleneck at high RPS.

---

## What InfraStream adds to `Program.cs`

```csharp
builder.AddInfraStreamLogging();      // Dev: SimpleConsole, Prod: JsonConsole

builder.AddCollectorEngine()          // registers the telemetry ingress
       .AddNullExporter();            // replace with a real exporter later
```

That is the entire integration. The rest of `Program.cs` is a normal
ASP.NET Core minimal API.

---

## Configuration

`appsettings.json` contains only the InfraStream section (and the standard
`AllowedHosts`):

```json
{
  "InfraStream": {
    "IngressPort": 5005,
    "ManagementPort": 5002,
    "WorkerCount": 0,
    "QueueCapacity": 64
  },
  "AllowedHosts": "*"
}
```

All settings can be overridden via environment variables using the standard
ASP.NET Core convention:

```bash
InfraStream__IngressPort=6005 dotnet run
```

---

## Notes

- The telemetry ingress listens on port `5005`, chosen to avoid a clash with
  the ASP.NET Core development defaults (`5000` for HTTP, `5001` for HTTPS).
  The application's own HTTP API uses the ASP.NET Core default (`5000`).
- The example is intentionally minimal: no authentication, no TLS, no
  persistence.
- For Kubernetes deployment patterns, see `examples/sidecar-k8s/`.
- For a custom `ITelemetryExporter`, see `examples/custom-exporter/`.
