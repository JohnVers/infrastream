# Custom Exporter Example

Write your own `ITelemetryExporter` in C# and plug it into InfraStream.

The example implements `FileExporter` — a minimal exporter that writes each
telemetry item as a single JSON line (JSONL) to a file.

---

## What this example shows

- Implementing `ITelemetryExporter` from scratch.
- Respecting the ownership contract: `ReadOnlyMemory<byte>` values point
  into the batch buffer and must be **copied** if they are retained.
- Accumulating items in a pooled byte buffer and writing them to disk on
  `Flush()` (called periodically by the worker, and on `Dispose` at shutdown).
- Thread safety: `ExportItem` may be called concurrently from several
  workers.
- Registering the exporter with `.AddCustomExporter<T>()`.
- Implementing `IDisposable` so buffered data reaches the sink on shutdown.

> **Not production-grade.** `FileExporter` writes bytes directly
> (`ArrayBufferWriter<byte>` + `FileStream.Write`), but still lacks
> rotation, compression, retry, and backpressure. See the XML-doc on
> `FileExporter` and the tech-debt notes in `local/Roadmap.md`.

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
| Your target is already supported (Kafka) | Built-in `AddKafkaExporter()` |
| Your target is OTLP, ClickHouse, S3, Elastic | Planned; not implemented yet |

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

`FileExporter` in this example passes the spans to `Utf8JsonWriter`, which
serializes them into the exporter's own buffer before `ExportItem`
returns. Nothing points back into the batch buffer after the call.

---

## Running the example

From this directory:

```bash
dotnet run
```

Send a test batch to `http://localhost:5005/`:

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

The gateway responds with `202 Accepted`.

### When does the file appear?

`TelemetryProcessorWorker` flushes exporters **every 100 processed batches**
(see `FlushEveryNBatches` in `TelemetryProcessorWorker`), and once more on
shutdown (`Dispose` / `SIGTERM`). With a single `curl` batch, either send
enough batches to reach the next flush, or stop the host (`Ctrl+C`) to
trigger the final flush, then inspect the file:

```bash
cat telemetry.jsonl
```

You should see one JSON object per line, with sensitive fields already
masked (the `password` attribute value is replaced with `*`).

> **PII masking and the gRPC path.** This example uses HTTP/1.1 ingress,
> where masking covers `message` and sensitive attribute keys
> (`password`, `secret`, `token`, `api_key`) plus Luhn-valid card numbers.
> On the gRPC path, `message` is plain text (not a JSON fragment), so card
> numbers are masked but `password=` / `secret=` patterns are not. See
> `KnownIssues.md` §Masking.

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

- `FileExporter` writes bytes directly (`ArrayBufferWriter<byte>` +
  `FileStream.Write`) but is still a reference, not production code: no
  rotation, no compression, no retry, no backpressure. Add those on top
  of this pattern for real downstreams.
- For real downstreams: Kafka is available today
  (`.AddKafkaExporter(...)`); ClickHouse, S3, Elastic, and OTLP are planned.
- For embedded usage, see `examples/embedded-aspnet/`.
- For Kubernetes deployment, see `examples/sidecar-k8s/`.
