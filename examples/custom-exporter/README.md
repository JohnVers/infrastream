# Custom Exporter Example

Write your own `ITelemetryExporter` in C# and plug it into InfraStream.

The example implements `FileExporter` — a minimal exporter that writes each
telemetry item as a single JSON line (JSONL) to a file.

---

## What this example shows

- Implementing `ITelemetryExporter` from scratch.
- Respecting the ownership contract: `ReadOnlyMemory<byte>` values point
  into the batch buffer and must be **copied** if they are retained.
- Buffering items in memory and flushing them to disk on `Flush()`.
- Thread safety: `ExportItem` may be called concurrently from several
  workers.
- Registering the exporter with `.AddCustomExporter<T>()`.

---

## When to write a custom exporter

| Scenario | Why a custom exporter |
|----------|-----------------------|
| Downstream not covered by built-in exporters | ClickHouse, S3, Elastic, custom HTTP API, etc. |
| Specific serialization format | JSONL, Parquet, CSV, protobuf, line-oriented text |
| Compliance / audit | Custom retention rules, signed write, immutable store |
| Testing / mocking | Deterministic in-memory sink for unit tests |
| Multi-destination fan-out | One exporter writes to several targets |

---

## When NOT to write a custom exporter

| Scenario | Better option |
|----------|---------------|
| You just need a null sink | `AddNullExporter()` |
| You want console output | `AddConsoleExporter()` |
| Your target is already supported (OTLP, Kafka) | Built-in exporter (once implemented) |

---

## Files in this directory

```
custom-exporter/
├── README.md              # this file
├── Program.cs             # host + registration
├── FileExporter.cs        # the custom exporter
├── CustomExporter.csproj
└── appsettings.json
```

---

## The ownership contract (read this first)

Every `TelemetryItem` passed to `ExportItem` contains `ReadOnlyMemory<byte>`
values that point **into the batch buffer**. The buffer is returned to a
pool as soon as `ExportItem` returns.

This means:

- You **may** read the bytes synchronously.
- You **must not** store the `ReadOnlyMemory<byte>` for later use.
- If you need the data after `ExportItem` returns, **copy it**:

```csharp
byte[] copy = item.Message.ToArray();
```

`FileExporter` in this example copies every field it wants to keep, so
nothing dangles after `ExportItem` returns.

---

## Running the example

From this directory:

```bash
dotnet run
```

Send a test batch to `http://localhost:5005`:

```bash
curl -X POST http://localhost:5005/ \
     -H "Content-Type: application/json" \
     -H "X-Node-Id: demo-node" \
     -H "X-Environment: dev" \
     --data-binary '{"payload":[
       {"timestamp":"2026-10-05T12:00:00Z","level":"INFO","component":"demo","message":"hello"},
       {"timestamp":"2026-10-05T12:00:01Z","level":"WARN","component":"demo","message":"careful","password":"hunter2"}
     ]}'
```

The gateway responds with `202 Accepted`. The exporter flushes periodically
(see `FlushInterval` below). After a few seconds, inspect the output file:

```bash
cat telemetry.jsonl
```

You should see one JSON object per line, with sensitive fields already
masked (the `password` attribute value is replaced with `*`).

---

## How the exporter is wired up

`Program.cs`:

```csharp
builder.AddInfraStreamLogging();

builder.AddCollectorEngine()
       .AddCustomExporter<FileExporter>();
```

`FileExporter` is registered as a singleton `ITelemetryExporter`. Its
parameterless constructor is used (see the `AddCustomExporter<T>()`
overload). For DI-based construction, use the other overload:

```csharp
builder.AddCollectorEngine()
       .AddCustomExporter<FileExporter>(sp => new FileExporter(
           sp.GetRequiredService<ILogger<FileExporter>>(),
           path: "telemetry.jsonl"));
```

---

## Configuration

`appsettings.json`:

```json
{
  "InfraStream": {
    "IngressPort": 5005,
    "ManagementPort": 5002,
    "WorkerCount": 2,
    "QueueCapacity": 64
  },
  "AllowedHosts": "*"
}
```

All settings can be overridden via `InfraStream__*` environment variables.

The ingress port `5005` is chosen to avoid a clash with the ASP.NET Core
development defaults (`5000` for HTTP, `5001` for HTTPS).

---

## Notes

- `FileExporter` is intentionally simple: it writes JSONL to a single file
  with a periodic flush. It is **not** production-grade (no rotation, no
  compression, no retry).
- For real downstreams (Kafka, ClickHouse, S3), wait for the built-in
  exporters in Phase 5, or write a production-grade implementation based on
  this example.
- For embedded usage, see `examples/embedded-aspnet/`.
- For Kubernetes deployment, see `examples/sidecar-k8s/`.
