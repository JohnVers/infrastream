# Ingress benchmark

Throughput and latency of the ingress pipeline. Measures HTTP/1.1,
HTTP/2, and gRPC ingest, decompression, JSON/protobuf parsing, PII
masking, and dispatch to a null exporter. No downstream I/O.

_Measured: 2026-10-09, updated 2026-10-21 on the reference machine
([hardware.md](hardware.md))._

> **Number formatting.** Numbers use `,` as the thousands separator
> (en-US convention). For example, `237,748` means 237 748 requests per
> second, and `7,393,167` means 7 393 167 lines per second.

## Hardware

See [hardware.md](hardware.md). Reference machine: Apple M5 Max, 4 CPUs,
2 GB RAM, Docker Desktop (arm64 containers).

## Configuration

- Gateway: ``WorkerCount={1|2|4}``, ``QueueCapacity=64``, server GC,
  ``GCHeapHardLimit`` sized to the CPU limit (``0x30000000`` for 1–2 CPUs,
  ``0x60000000`` for 4 CPUs).
- Ingress: ``MaxBodySize=1 MB`` for compressed payloads, ``4 MB`` for raw;
  ``DecompressedBufferSize=16 MB``.
- Decoders: ``identity``, Brotli, gzip enabled; zstd disabled.
- HTTP/2 and gRPC: ``MaxStreamsPerConnection=500`` for batch=1
  (Kestrel default 100 creates client-side queuing at 120 copies),
  ``100`` for batch=10k. gRPC ``MaxReceiveMessageSize=16 MB``.
- Exporter: ``NullExporter``.
- Masking: enabled (test card numbers and ``password=`` values in the
  payload are masked in-place on the hot path).

## Terminology

- **Raw** — request has no ``Content-Encoding`` header, payload sent as-is.
  Handled by the ``identity`` decoder.
- **Brotli / Gzip** — corresponding HTTP encodings.
- **H1** — HTTP/1.1, ``ConnectionHandler`` or ``Kestrel`` mode.
- **H2** — HTTP/2 cleartext (h2c), Kestrel listener.
- **gRPC** — HTTP/2 + protobuf, Kestrel listener.

## Workload

- Tool: NBomber 4.1.0, closed-loop model (``KeepConstant``), 60 copies for
  1–2 CPU, 120 copies for 4 CPU.
- Duration: 60 s per scenario, no warm-up.
- Two batch sizes:
  - **10,000 lines per request** — batched telemetry. Raw ~2.6–3 MB;
    Brotli/gzip ~300–500 KB; protobuf ~1.2–1.8 MB.
  - **1 line per request** — baseline for request-handling capacity.

## How to reproduce

```bash
cd deploy
chmod +x run_bench.sh

# Full matrix (H1 + H2 + gRPC)
HTTP2_ENABLED=true GRPC_ENABLED=true ./run_bench.sh

# Only H1
./run_bench.sh

# Only H2
HTTP2_ENABLED=true HTTP1_MODE=__none__ ./run_bench.sh

# Only gRPC
GRPC_ENABLED=true HTTP1_MODE=__none__ ./run_bench.sh
```

Filters: ``ENCODINGS``, ``CPU_CONFIGS``, ``BATCHES``, ``HTTP1_MODE``,
``HTTP2_ENABLED``, ``GRPC_ENABLED``.

## H1 — batch = 10,000

| CPU | Encoding | RPS | Lines/s | p50 (ms) | p99 (ms) | RSS (MB) |
|-----|----------|-----|---------|----------|----------|----------|
| 1 | Brotli | 213.4 | 2,134,000 | 274.9 | 826.4 | 232 |
| 1 | Gzip | 200.8 | 2,007,833 | 285.7 | 949.8 | 221 |
| 1 | Raw | 168.8 | 1,688,333 | 353.8 | 704.5 | 691 |
| 2 | Brotli | 398.4 | 3,984,167 | 148.0 | 447.2 | 196 |
| 2 | Gzip | 367.1 | 3,670,500 | 160.9 | 485.9 | 162 |
| 2 | Raw | 315.6 | 3,156,333 | 188.9 | 479.2 | 656 |
| 4 | Brotli | 739.3 | 7,393,167 | 156.2 | 402.9 | 261 |
| 4 | Gzip | 713.4 | 7,134,167 | 163.7 | 419.1 | 230 |
| 4 | Raw | 603.6 | 6,041,333 | 196.6 | 433.7 | 996 |

### Observations

- **In the measured conditions, Brotli showed the highest RPS among the
  tested encodings for batched telemetry**, about 22–32 % above raw,
  with a smaller wire footprint and different ``ArrayPool`` behavior.
- **Raw payloads were associated with higher resident memory** (up to
  about 1 GB at 4 CPU in this configuration).
- Scaling for Brotli/gzip was close to linear (about ×1.9 per doubling)
  in the measured range.

## H2 — batch = 10,000 and batch = 1

HTTP/2 cleartext (h2c), NBomber with ``RequestVersionOrHigher``.

| CPU | Encoding | Batch | RPS | p50 (ms) | p99 (ms) |
|-----|----------|-------|-----|----------|----------|
| 4 | Brotli | 10k | 666.7 | 177.5 | 430.9 |
| 4 | Identity | 10k | 549.3 | 258.8 | 662.5 |
| 1 | Brotli | 1 | 119,353 | 0.3 | 1.5 |
| 4 | Brotli | 1 | 226,710 | 0.5 | 1.9 |
| 4 | Gzip | 1 | 227,052 | 0.5 | 1.9 |
| 4 | Identity | 1 | 237,748 | 0.4 | 1.8 |

### Observations

- **At batch=10k, H2 showed lower RPS than H1/Brotli in this setup**
  (666 vs 739 RPS). HPACK, frame processing, and flow control add CPU
  work; multiplexing did not provide a measurable benefit for large
  payloads here.
- **At batch=1, H2 showed the highest RPS among the tested protocols**
  (238k RPS, p99 1.8 ms) in this setup, together with low per-request
  overhead.
- **p99 at batch=1 was lower than H1 in these runs** (1.8 vs 4.3 ms).

## gRPC — batch = 10,000 and batch = 1

Protobuf, HTTP/2 cleartext, native protobuf path in the worker.

| CPU | Batch | RPS | p50 (ms) | p99 (ms) | FAIL |
|-----|-------|-----|----------|----------|------|
| 1 | 10k | 14.8 | 4710 | 5464 | 19 |
| 2 | 10k | 62.1 | 517 | 4460 | 280 |
| 4 | 10k | 129.6–192.8 | 301–1002 | 499–1296 | 0 |
| 1 | 1 | 93,013 | 0.5 | 1.4 | 0 |
| 2 | 1 | 132,584 | 0.4 | 1.3 | 0 |
| 4 | 1 | 168,679 | 0.6 | 2.1 | 0 |

### Observations

- **At batch=10k, gRPC showed substantially lower RPS than H1 in these
  runs (4–5×), and FAILs were observed on 1–2 CPU.** Double protobuf
  parsing, ``ToByteArray()``, and UTF-8 conversion contribute to the
  difference.
- **At batch=1, gRPC was within the same order of magnitude as the
  HTTP protocols** (169k RPS, p99 2.1 ms), but below H2 (238k) in this
  setup. Protobuf marshaling on the client and double parsing on the
  server are among the contributing factors.
- **gRPC was measured without compression.** H1/H2 used Brotli/gzip;
  enabling gzip on gRPC (not yet wired) would reduce the wire footprint
  but not necessarily the CPU cost.

## Cross-protocol summary (4 CPU)

| Scenario | Highest RPS in this run | RPS |
|---|---|---|
| batch=10k | H1 + Brotli | 739–754 |
| batch=10k | H2 | 667 |
| batch=10k | gRPC | 130–193 |
| batch=1 | H2 | 237,748 |
| batch=1 | H1 | 203,785 |
| batch=1 | gRPC | 168,679 |

**Summary of the measured results:** in this setup, H1 + Brotli showed
the highest RPS for large batches, H2 showed the highest RPS for small
high-frequency requests, and gRPC was below H2 at batch=1 while offering
a typed protobuf contract. These results are specific to the measured
hardware, payload, and configuration; other workloads may favor
different choices.

## Memory

| Batch | Protocol | RSS at 4 CPU |
|---|---|---|
| 10k | H1 Brotli | ~261 MB |
| 10k | H2 Brotli | ~235 MB |
| 10k | gRPC identity | ~611–1313 MB |
| 1 | H2 | ~120–135 MB |
| 1 | gRPC | ~119–125 MB |

## Scaling

| Protocol | Batch | ×1→2 | ×2→4 |
|---|---|---|---|
| H1 Brotli | 10k | ×1.87 | ×1.86 |
| H2 identity | 10k | ×2.52 | ×1.77 |
| gRPC identity | 1 | ×1.43 | ×1.27 |

H1 batch=10k scaled close to linear in the measured range. H2/gRPC at
batch=1 flattened above 2 CPU (loopback path, accept queue, and other
shared resources may contribute).

## What is not measured

- Real exporters (Kafka, ClickHouse, …).
- Commodity x64 hardware.
- Long-running stability.
- Network overhead beyond loopback inside a Docker bridge.
- gRPC with compression (gzip/Brotli).
- Exact crossover point between "compression helps" and "compression hurts".
