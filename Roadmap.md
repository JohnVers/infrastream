# InfraStream — Roadmap

What's planned for InfraStream. For what's already done, see the commit
history and the release notes.

---

## Phase 3: Documentation

- `docs/getting-started-embedded.md` — embedding into an ASP.NET Core app.
- `docs/getting-started-sidecar.md` — sidecar deployment in Kubernetes.
- `docs/getting-started-standalone.md` — standalone deployment.
- `docs/extensions.md` — how to write `ITelemetryProcessor` and
  `ITelemetryExporter`.
- `docs/configuration.md` — every configuration parameter.
- `docs/masking.md` — how PII masking works, and what is not masked.
- `docs/benchmarks.md` — methodology, results, and cost analysis.
- `docs/comparison.md` — comparison with Fluent Bit and Vector.

---

## Phase 4: Packaging

- NuGet packages: `InfraStream.Core`, `InfraStream.CollectorEngine`.
- Docker image: `infrastream/gateway:latest` (multi-arch).
- Helm chart: `charts/infrastream-gateway/`.
- Release workflow on GitHub Actions.

---

## Phase 5: Additional Features

### Priority 1 (critical)

- Prometheus `/metrics` on the management port.
- OpenTelemetry integration with `ILogger`.
- Graceful shutdown (drain in-flight batches on SIGTERM).
- `/ready` endpoint for Kubernetes.

### Priority 2 (important)

- Retry / dead-letter for exporters.
- Disk buffering when downstream is unavailable.
- Additional protocols: HTTP/2, gRPC, UDP.
- Real exporters: Kafka, ClickHouse, S3, ElasticSearch.
- Authentication: API key, JWT.

### Priority 3 (nice to have)

- Extended masker with custom patterns (C#).
- Sampling and filtering plugins.
- Configuration hot reload.
- Web UI for monitoring.

---

## Known limitations

InfraStream is under active development. The current version has the
following known limitations:

- **Escaped JSON strings** skip PII masking (rare in practice).
- **Only Brotli** is supported for compressed payloads. gzip, zstd, and
  snappy are not.
- **Fixed decompression buffer** (16 MB). Very large batches may fail to
  decompress.
- **Static intern cache** for well-known tokens only (log levels and a few
  component names).
- **No graceful shutdown yet.** In-flight batches may be dropped on
  termination. Planned for Phase 5.
- **No `/metrics` or `/ready` endpoints yet.** Planned for Phase 5.
- **Real exporters (Kafka, ClickHouse, OTLP) are stubs.** Planned for
  Phase 5.
- **Performance numbers** in the README are measured with `NullExporter` on
  reference hardware. Benchmarks on commodity hardware are in progress.
