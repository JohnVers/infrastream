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
- **Small footprint.** 2.15M log lines/s with PII masking on **1 CPU core**
  and **229 MB RAM**. Scales to 7.75M lines/s on 4 cores.
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

Measured with PII masking enabled, batch size 10 000 lines, Brotli-compressed
payloads.

### Reference hardware (macOS M5 Max via Docker Desktop)

| Config | RPS | lines/s | p50 | p99 | RAM |
|---|---|---|---|---|---|
| 1 CPU, 1 worker | 215 | 2.15M | 262 ms | 1000 ms | 229 MB |
| 2 CPU, 2 workers | 408 | 4.07M | 144 ms | 436 ms | 189 MB |
| **4 CPU, 4 workers** | **775** | **7.75M** | **149 ms** | **383 ms** | **264 MB** |

> **These numbers are not representative of typical production hardware.**
> Benchmarks on commodity x64 hardware are in progress.
>
> **Note:** all numbers above are measured with `NullExporter` — a
> zero-overhead sink used for benchmarking. Real exporters (Kafka,
> ClickHouse, OTLP) will add serialization, network I/O, and copy costs.

---

## Features

- **Raw TCP ingress** with HTTP/1.1 header parsing (`X-Node-Id`,
  `X-Environment`, `Content-Encoding`).
- **Brotli decompression** transparently handled if the payload is
  compressed.
- **PII masking** on `message` and sensitive attribute values
  (`password`, `secret`, `token`, `api_key`) — applied **in place**, without
  allocations on the hot path.
- **Credit-card masking** (16-digit sequences that pass the Luhn check).
- **Zero-allocation JSON parsing** via `Utf8JsonReader`.
- **Bounded channel with backpressure** — when workers cannot keep up,
  the kernel shrinks the TCP window instead of buffering without limit.
- **Fluent API + DI integration** — `AddCollectorEngine()`,
  `AddNullExporter()`, `AddConsoleExporter()`, `AddOpenTelemetryExporter()`,
  `AddKafkaExporter()`, `AddCustomExporter<T>()`.
- **`appsettings.json` + env overrides** — standard ASP.NET Core
  configuration (`InfraStream__WorkerCount=4`, etc.).

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
- [`Roadmap.md`](Roadmap.md) — what's planned (Phase 3–5) and known
  limitations of the current version.
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
