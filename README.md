# InfraStream

**High-performance edge gateway for telemetry, convenient for .NET teams.**

InfraStream receives log batches from thousands of nodes over raw TCP,
validates them, masks PII, parses JSON (zero-allocation), and forwards them
downstream — all inside a single .NET 10 process.

It is not a general-purpose log shipper. It is a focused ingress gateway
for telemetry, designed to be embedded into an existing ASP.NET Core
application, deployed as a sidecar, or run standalone.

[![CI](https://github.com/JohnVers/infrastream/actions/workflows/ci.yml/badge.svg)](https://github.com/JohnVers/infrastream/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)

---

## Why InfraStream

- **Zero-allocation hot path.** `Utf8JsonReader`, `ArrayPool<byte>`,
  `BoundedChannel<T>`, and in-place PII masking. No UTF-16 conversions
  during parsing.
- **Small footprint.** ~2.1M log lines/s with PII masking on **1 CPU core**
  and **~220 MB RAM**. Scales to ~7.7M lines/s on 4 cores.
- **Native .NET integration.** `ILogger`, DI, `IOptions`, `Span<T>`,
  `appsettings.json`. One language for the whole team.
- **MIT license.** Permissive. No copyleft obligations. Safe to embed in
  closed-source and commercial applications.

---

## Quick Start

Register InfraStream in your ASP.NET Core application:

```csharp
builder.AddInfraStreamLogging();

builder.AddCollectorEngine()
       .AddNullExporter();    // replace with a real exporter later
```

That is the entire integration. The application now accepts telemetry on
port `5005` (configurable) in addition to its normal HTTP API.

See [`examples/embedded-aspnet/`](examples/embedded-aspnet/) for a complete,
runnable example.

---

## Performance

Measured with PII masking enabled, batch size 10,000 lines, Brotli-compressed
payloads, `NullExporter`.

### Reference hardware (macOS M5 Max via Docker Desktop)

| Config | RPS | lines/s | p50 | p99 | RAM |
|---|---|---|---|---|---|
| **1 CPU, 1 worker** | **210** | **2.1M** | **~270 ms** | **~930 ms** | **~220 MB** |
| 2 CPU, 2 workers | 405 | 4.1M | ~145 ms | ~440 ms | ~180 MB |
| 4 CPU, 4 workers | 770 | 7.7M | ~150 ms | ~390 ms | ~280 MB |

A single CPU core with 1 GB RAM handles ~2.1M lines/s with PII masking
enabled — enough for ~1,000 nodes emitting up to 1,000 lines/s each, with
headroom. See [`docs/benchmarks/ingress.md`](docs/benchmarks/ingress.md)
for the full results, including encoding comparison, batch-size effects,
scaling, and capacity examples.

> **These numbers are not representative of typical production hardware.**
> Benchmarks on commodity x64 hardware are in progress.
>
> **Note:** all numbers above are measured with `NullExporter` — a
> zero-overhead sink used for benchmarking. Real exporters (Kafka,
> ClickHouse, OTLP) will add serialization, network I/O, and copy costs.
>
> Per-run variance is roughly ±5 % for RPS and up to ±30 % for p99.

---

## Features

- **HTTP/1.1 ingress over raw TCP**, with `X-Node-Id`, `X-Environment`,
  and `Content-Encoding` header parsing.
- **Pluggable content decoding** — `Content-Encoding` is negotiated per
  request; identity (raw), Brotli, and gzip are supported. zstd is planned.
- **PII masking** on `message` and sensitive attribute values
  (`password`, `secret`, `token`, `api_key`) — applied **in place**, without
  allocations on the hot path.
- **Credit-card masking** (16-digit sequences that pass the Luhn check).
- **Zero-allocation JSON parsing** via `Utf8JsonReader`.
- **Bounded channel with backpressure** — when workers cannot keep up,
  the kernel shrinks the TCP window instead of buffering without limit.
- **Graceful shutdown** — on SIGTERM, the ingress queue is completed and
  workers drain remaining payloads before exit. New requests during
  shutdown receive `503 Service Unavailable`. Bounded by the host
  shutdown timeout (default 30 s).
- **Fluent API + DI integration** — `AddCollectorEngine()`,
  `AddNullExporter()`, `AddConsoleExporter()`, `AddOpenTelemetryExporter()`,
  `AddKafkaExporter()`, `AddCustomExporter<T>()`.
- **`appsettings.json` + env overrides** — standard ASP.NET Core
  configuration (`InfraStream__WorkerCount=4`, etc.).
- **Management endpoints** on the management port (default `5002`):
  `/health`, `/ready` for Kubernetes probes, and Prometheus `/metrics`
  for scraping.

---

## Examples

Three runnable examples in [`examples/`](examples/):

| Example | What it shows |
|---|---|
| [`embedded-aspnet/`](examples/embedded-aspnet/) | Embed InfraStream into an existing ASP.NET Core service |
| [`sidecar-k8s/`](examples/sidecar-k8s/) | Deploy as a sidecar container in Kubernetes |
| [`custom-exporter/`](examples/custom-exporter/) | Write your own `ITelemetryExporter` |

Each example has its own README with prerequisites and run instructions.

---

## Comparison with other tools

A detailed comparison with **Fluent Bit** and **Vector** (licenses, features,
migration notes, and equal-condition benchmarks) is **in progress**.

It will be published once commodity-hardware benchmarks are available, so
that any performance claims can be backed by a reproducible methodology.

InfraStream is positioned as an **alternative, especially convenient for
.NET teams** — not as a "better" general-purpose log shipper.

---

## Documentation

- [`docs/architecture.md`](docs/architecture.md) — internal design, pipeline
  diagram, key design decisions.
- [`docs/benchmarks/`](docs/benchmarks/) — performance measurements,
  methodology, and capacity examples.
- [`KnownIssues.md`](KnownIssues.md) — known limitations of the current
  version.
- [`examples/`](examples/) — runnable examples.

---

## Contributing

Contributions are welcome. See [`CONTRIBUTING.md`](CONTRIBUTING.md) for:

- How to build and test the project.
- The DCO requirement (`git commit -s`).
- Code style and dependency policy.

This is a volunteer-maintained project. There is **no SLA** for bug fixes.
Maintainers respond when time permits, with priority given to security
vulnerabilities and regressions.

---

## License

[MIT](LICENSE) © 2026 JohnVers
