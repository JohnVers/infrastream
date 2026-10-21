# InfraStream — Known Issues

Known limitations and issues in the current version of InfraStream. For
what's already done, see the commit history and the release notes. For
planned work, see the internal roadmap.

InfraStream is under active development.

---

## Masking

- **Escaped JSON strings** skip PII masking (rare in practice).
- **Plain-text `message` and PII.** The masker targets JSON keys
  (`"password"`, `"secret"`, `"token"`, `"api_key"`) and Luhn-valid
  16-digit card numbers. Plain-text patterns like `password=...` or
  `secret=...` **inside a plain-text `message` field are not masked**.
  This applies to both the HTTP/1.1 and gRPC paths: `message` is treated
  as plain text, and the masker has no JSON-key rules to match against.
  Card numbers in `message` are still masked.
- **Custom attributes with sensitive keys are fully masked.** Values of
  attributes named `password`, `secret`, `token`, `api_key` (and common
  variants) are replaced entirely with `*`, on both paths.
- **`message` as a JSON fragment is not supported by the masker.** If
  `message` is itself a JSON document (e.g. nested structured logging),
  the current masker will not descend into it. Pre-format such payloads
  at the source, or extend the masker.

## Input

- **Supported encodings:** identity, Brotli, gzip. zstd and snappy are
  not implemented yet.
- **Decompression buffer** is configurable via
  ``InfraStream:Ingress:DecompressedBufferSize`` (default 16 MB). A batch
  whose decompressed size exceeds this limit is rejected as a batch
  error; the worker logs a warning and increments its error counter.

## gRPC

- **No compression yet.** gRPC requests are sent uncompressed (protobuf
  only); gzip/Brotli over gRPC is not wired. This inflates the wire
  footprint compared to HTTP/1.1 with Brotli/gzip for the same payload.
- **``ToByteArray()`` allocation.** Google.Protobuf 3.30.0 does not expose
  ``WriteTo(Span<byte>)``; ``IngestGrpcService`` allocates a full byte
  array per request. Planned: ``CodedOutputStream`` over a pooled buffer.
- **No raw JSON passthrough.** ``TelemetryItem.RawExtensionJson`` is empty
  on the gRPC path; exporters that rely on raw JSON will not receive it.
- **PII masking** — same rules as the HTTP/1.1 path; see [Masking](#masking).
- **Performance characteristics differ from HTTP ingress.** See
  [`docs/benchmarks/ingress.md`](docs/benchmarks/ingress.md) for the
  measured numbers; gRPC is a separate protocol path with its own
  trade-offs.

## Internals

- **Static intern cache** for well-known tokens only (log levels and a few
  component names).

## Operations

- **Management endpoints** are available on the management port
  (default ``5002``): ``/health``, ``/ready``, and Prometheus ``/metrics``.
- **No authentication** on the management port yet. Planned for a future
  release.
- **``/ready`` reflects worker registration**, not in-flight queue state.
  A gateway with a full queue and zero free workers will still report
  ready. This is intentional for the current version; queue-aware
  readiness is future work.

## Exporters

- **Only the Kafka exporter is implemented.** ClickHouse, S3, ElasticSearch,
  and OTLP are not available yet. Planned for a future release.
- **Exporter buffers are flushed periodically and on shutdown.**
  ``TelemetryProcessorWorker`` calls ``ITelemetryExporter.Flush()`` every
  ``FlushEveryNBatches`` processed batches (currently 100) and once more in
  the worker's ``finally`` block. A failure in one exporter is logged and
  does not block the others.
- **No built-in retry / dead-letter / disk buffering.** Exporters that
  need those must implement them (Kafka has a spill manager; others do
  not).

## Logging

- **Silencing.** All InfraStream log messages can be disabled via
  ``Logging__LogLevel__InfraStream=None`` (or the ``INFA_LOG_LEVEL=None``
  shorthand in ``docker-compose.yml``). Because all messages use
  source-generated ``[LoggerMessage]`` delegates, disabled logs incur no
  allocations and no formatting cost.
- **No OTLP log export yet.** Logs are written to stdout/console only.
  OpenTelemetry log export (e.g. to Grafana Loki) is planned for a future
  release.

## Performance

- **Performance numbers** in the README are measured with ``NullExporter``
  on reference hardware. The overhead of the Kafka exporter is not yet
  benchmarked; it will be documented in
  [`docs/benchmarks/`](docs/benchmarks/). Benchmarks on commodity hardware
  are in progress.
- **Throughput depends on batch size.** See
  [`docs/benchmarks/ingress.md`](docs/benchmarks/ingress.md) for the
  measured numbers (batch=10k vs batch=1, per protocol and encoding).
- **HTTP/2 ``MaxStreamsPerConnection``** defaults to 100 (Kestrel default).
  High-parallelism clients (e.g. load tests with more than 100 concurrent
  copies) may observe client-side queuing at the default value; the limit
  is configurable via
  ``InfraStream:Protocols:Http2:MaxStreamsPerConnection`` and
  ``InfraStream:Protocols:Grpc:MaxStreamsPerConnection``.

## Memory

- **Raw (uncompressed) large batches** use more resident memory than
  compressed ones: a 10,000-line raw batch is ~3 MB and stresses the
  payload pool. Compression is recommended for memory-constrained
  deployments. See [`docs/benchmarks/ingress.md`](docs/benchmarks/ingress.md)
  for measured RSS values.
- **gRPC at large batches** holds the full ``TelemetryBatch`` plus a
  serialized byte array per in-flight request, so resident memory can be
  higher than on the HTTP path for the same batch size. See
  [`docs/benchmarks/ingress.md`](docs/benchmarks/ingress.md) for details.
