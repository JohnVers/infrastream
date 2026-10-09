# InfraStream — Known Issues

Known limitations and issues in the current version of InfraStream. For
what's already done, see the commit history and the release notes. For
planned work, see the internal roadmap.

InfraStream is under active development.

---

## Masking

- **Escaped JSON strings** skip PII masking (rare in practice).

## Input

- **Supported encodings:** identity, Brotli, gzip. zstd and snappy are
  not implemented yet.
- **Decompression buffer** is configurable via
  `InfraStream:Ingress:DecompressedBufferSize` (default 16 MB). A batch
  whose decompressed size exceeds this limit is rejected as a batch
  error; the worker logs a warning and increments its error counter.

## Internals

- **Static intern cache** for well-known tokens only (log levels and a few
  component names).

## Operations

- **Management endpoints** are available on the management port
  (default `5002`): `/health`, `/ready`, and Prometheus `/metrics`.
- **No authentication** on the management port yet. Planned for a future
  release.
- **`/ready` reflects worker registration**, not in-flight queue state.
  A gateway with a full queue and zero free workers will still report
  ready. This is intentional for the current version; queue-aware
  readiness is future work.

## Exporters

- **Only the Kafka exporter is implemented.** ClickHouse, S3, ElasticSearch,
  and OTLP are not available yet. Planned for a future release.

## Performance

- **Performance numbers** in the README are measured with `NullExporter`
  on reference hardware. The overhead of the Kafka exporter is not yet
  benchmarked; it will be documented in
  [`docs/benchmarks/`](docs/benchmarks/). Benchmarks on commodity hardware
  are in progress.
- **Throughput depends on batch size.** With 10,000-line batches,
  InfraStream processes ~2.1M lines/s per CPU core. With single-line
  requests, the same core handles ~95,000 requests/s — request overhead
  dominates. See [`docs/benchmarks/ingress.md`](docs/benchmarks/ingress.md)
  for details.

## Memory

- **Raw (uncompressed) large batches** use significantly more resident
  memory than compressed ones. A 10,000-line raw batch is ~3 MB and
  stresses the payload pool: at 1 CPU / 1 GB RAM, RSS peaks around
  ~750 MB with raw payloads, versus ~220 MB with Brotli or gzip.
  Compression is recommended for memory-constrained deployments.
