# InfraStream — Known Issues

Known limitations and issues in the current version of InfraStream. For
what's already done, see the commit history and the release notes. For
planned work, see the internal roadmap.

InfraStream is under active development.

---

## Masking

- **Escaped JSON strings** skip PII masking (rare in practice).

## Input

- **Only Brotli** is supported for compressed payloads. gzip, zstd, and
  snappy are not.
- **Fixed decompression buffer** (16 MB). Very large batches may fail to
  decompress.

## Internals

- **Static intern cache** for well-known tokens only (log levels and a few
  component names).

## Lifecycle

- **No graceful shutdown yet.** In-flight batches may be dropped on
  termination. Planned for a future release.

## Operations

- **No `/metrics` or `/ready` endpoints yet.** Planned for a future release.

## Exporters

- **Only the Kafka exporter is implemented.** ClickHouse, S3, ElasticSearch,
  and OTLP are not available yet. Planned for a future release.

## Performance

- **Performance numbers** in the README are measured with `NullExporter` on
  reference hardware. The overhead of the Kafka exporter is not yet
  benchmarked; it will be documented in `docs/benchmarks.md`. Benchmarks on
  commodity hardware are in progress.
