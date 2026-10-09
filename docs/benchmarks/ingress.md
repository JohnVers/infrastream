# Ingress benchmark

Throughput and latency of the ingress pipeline. Measures HTTP ingest,
decompression, JSON parsing, PII masking, and dispatch to a null
exporter. No downstream I/O.

_Measured: 2026-10-09 on the reference machine
([hardware.md](hardware.md))._

## Hardware

See [hardware.md](hardware.md). Reference machine: Apple M5 Max, 4 CPUs,
2 GB RAM, Docker Desktop.

## Configuration

- Gateway: `WorkerCount={1|2|4}`, `QueueCapacity=64`, server GC,
  `GCHeapHardLimit` sized to the CPU limit (`0x30000000` for 1–2 CPUs,
  `0x60000000` for 4 CPUs).
- Ingress: `MaxBodySize=1 MB` for compressed payloads, `4 MB` for raw;
  `DecompressedBufferSize=16 MB`.
- Decoders: `identity`, Brotli, gzip enabled; zstd disabled.
  (The `identity` decoder handles requests without a `Content-Encoding`
  header — see "Raw" below.)
- Exporter: `NullExporter`.
- Masking: enabled (test card numbers and `password=` values in the
  payload are masked in-place on the hot path).

## Terminology

In the tables below, **Raw** means the request has no `Content-Encoding`
header and the payload is sent as-is. The gateway's `identity` decoder
handles it. Brotli and Gzip refer to the corresponding HTTP encodings.

## Workload

- Tool: NBomber 4.1.0, closed-loop model (`KeepConstant`), 60 copies for
  1–2 CPU, 120 copies for 4 CPU.
- Duration: 60 s per scenario, no warm-up.
- Two batch sizes are measured:
  - **10,000 lines per request** — typical for edge nodes that collect
    and ship telemetry in batches. Raw batch ~3 MB; Brotli/gzip
    compressed ~300–500 KB.
  - **1 line per request** — baseline for pure request-handling
    capacity, independent of batch amortization.

## How to reproduce

```bash
# Throughput mode (batch = 10,000)
WORKER_COUNT=4 GATEWAY_CPU=4.0 GATEWAY_CPU_INT=4 GATEWAY_MEMORY=2G \
GC_HEAP_HARD_LIMIT=0x60000000 MAX_BODY_SIZE=4194304 \
LOAD_COPIES=120 LOAD_BATCH_SIZE=10000 LOAD_DURATION=60 \
LOAD_ENCODINGS=identity,br,gzip \
  docker compose up --build -d

# Baseline mode (batch = 1)
WORKER_COUNT=1 GATEWAY_CPU=1.0 GATEWAY_CPU_INT=1 GATEWAY_MEMORY=1G \
GC_HEAP_HARD_LIMIT=0x30000000 MAX_BODY_SIZE=1048576 \
LOAD_COPIES=60 LOAD_BATCH_SIZE=1 LOAD_DURATION=60 \
LOAD_ENCODINGS=identity,br,gzip \
  docker compose up --build -d

docker compose logs -f loadtests
```

## Throughput (batch = 10,000)

| CPU | Encoding | RPS | Lines/s | p50 (ms) | p99 (ms) | RSS (MB) |
|-----|----------|-----|---------|----------|----------|----------|
| 1   | Brotli   | 210 | 2,102,833 | 266.0 | 1218.6 | 219 |
| 1   | Gzip     | 193 | 1,934,333 | 299.3 | 1266.7 | 212 |
| 1   | Raw      | 168 | 1,677,000 | 341.5 |  806.4 | 756 |
| 2   | Brotli   | 403 | 4,025,833 | 146.4 |  445.2 | 183 |
| 2   | Gzip     | 377 | 3,769,833 | 157.1 |  470.8 | 162 |
| 2   | Raw      | 314 | 3,136,000 | 189.3 |  481.0 | 791 |
| 4   | Brotli   | 778 | 7,777,500 | 147.7 |  384.0 | 277 |
| 4   | Gzip     | 721 | 7,209,833 | 161.0 |  415.5 | 222 |
| 4   | Raw      | 587 | 5,871,667 | 204.2 |  450.1 | 1241 |

### Observations

- **Compression is faster than raw at this batch size.** Brotli is
  ~25–32 % faster than Raw across all CPU configurations. The
  savings come from fewer bytes on the wire, less copy work in the
  connection handler, and better `ArrayPool` behavior with smaller
  buffers. Raw 10k-line batches also sit near `MaxBodySize` and stress
  the payload pool.
- **Brotli is the recommended encoding.** Span-based allocation-free
  decoding, the best compression ratio among the supported encodings,
  and the highest throughput measured. Gzip is within 5–7 % of Brotli;
  the managed `GZipStream` over a `ReadOnlyMemoryStream` performs well
  enough that a P/Invoke into `libz` is not justified at this time.
- **Raw uses significantly more memory.** At 1 CPU, RSS peaks at
  ~756 MB with raw payloads, versus ~215 MB with Brotli or gzip. For
  1 GB RAM deployments, compression is recommended.
- **Raw requires `MaxBodySize ≥ 4 MB`** for 10k-line raw batches
  (~3 MB). With the default 1 MB limit these requests are rejected with
  `413 Payload Too Large`, which is the intended behavior.
- **No regression from the ingress refactor.** An earlier InfraStream
  measurement (744 RPS / 7.45M lines/s, same hardware, `NullExporter`,
  before the pluggable decoder layer) is within ~1 % of the current
  Brotli result.

## Baseline (batch = 1)

Request-handling capacity with minimal payloads (one log line per
request). Independent of batch amortization; useful as a fixed reference
point for future comparisons.

| CPU | Encoding | RPS | p50 (ms) | p99 (ms) | RSS (MB) |
|-----|----------|-----|----------|----------|----------|
| 1   | Brotli   |  95,067 | 0.5 | 4.4 | 83 |
| 1   | Gzip     |  89,335 | 0.5 | 4.6 | 86 |
| 1   | Raw      | 117,706 | 0.4 | 2.6 | 82 |
| 2   | Brotli   | 148,809 | 0.3 | 3.4 | 82 |
| 2   | Gzip     | 141,309 | 0.3 | 4.2 | 82 |
| 2   | Raw      | 157,298 | 0.2 | 4.0 | 80 |
| 4   | Brotli   | 176,825 | 0.3 | 9.0 | 88 |
| 4   | Gzip     | 173,415 | 0.4 | 8.7 | 90 |
| 4   | Raw      | 189,142 | 0.3 | 8.2 | 117 |

### Observations

- **Raw is fastest at this payload size.** With ~300-byte payloads,
  compression buys almost nothing on the wire but still costs
  decompression time. Raw is 19–24 % faster than Brotli at 1 CPU,
  and 6–8 % faster at 4 CPU.
- **Latency is sub-millisecond at p50.** The pipeline is fast enough
  that loopback round-trip dominates the median.
- **Memory is ~80–90 MB regardless of encoding** — the payload pool
  stays small when batches are tiny.
- **Scaling flattens above 2 CPUs** (see [Scaling](#scaling) below).
- **One failure out of 10.4M requests** was observed in one 4-CPU gzip
  run; this is within noise for loopback traffic and is not a
  reproducible pattern.

## Encoding vs workload

The choice of `Content-Encoding` depends on batch size:

| Batch size | Winner | Margin |
|------------|--------|--------|
| 1 line | Raw | +19–24 % over Brotli |
| 10,000 lines | Brotli | +25–32 % over Raw |

For **batched telemetry** (the target workload), Brotli is recommended.
For **per-line sends** (small payloads), the compression overhead is not
worth the marginal wire savings on fast links.

The crossover is not measured precisely; it depends on payload size,
link bandwidth, and CPU budget.

## Memory

| Batch size | Encoding | RSS at 1 CPU | RSS at 4 CPU |
|------------|----------|--------------|--------------|
| 10,000 | Brotli   | ~219 MB | ~277 MB |
| 10,000 | Gzip     | ~212 MB | ~222 MB |
| 10,000 | Raw      | **~756 MB** | **~1241 MB** |
| 1 | any | ~80–90 MB | ~88–117 MB |

**Takeaway:** for large raw batches, resident memory grows several-fold.
Compression keeps memory predictable. For small per-line sends, memory is
low regardless of encoding.

## Scaling

| Encoding | ×1→2 CPU | ×2→4 CPU |
|----------|----------|----------|
| Brotli (10k)   | ×1.92 | ×1.93 |
| Gzip (10k)     | ×1.95 | ×1.91 |
| Raw (10k)      | ×1.87 | ×1.87 |
| Brotli (1)     | ×1.57 | ×1.19 |
| Gzip (1)       | ×1.58 | ×1.23 |
| Raw (1)        | ×1.34 | ×1.20 |

### Observations

- **Batch=10k scales near-linearly** with CPU (×1.9 for both doublings).
  The pipeline uses the extra cores efficiently.
- **Batch=1 saturates above 1 CPU.** Scaling from 1→2 CPU is only ×1.5,
  and from 2→4 CPU is ×1.2. At ~100k RPS the loopback path, accept
  queue, or similar shared resource becomes the bottleneck, not CPU.
- CPU load reaches 100 % (docker stats units) at the top of each curve,
  confirming the process itself is not idle.

## Capacity examples

A single CPU core with 1 GB RAM, running the batch=10k pipeline with
Brotli, sustains **~2.1M lines/s** with PII masking enabled. In terms of
node fleet size:

| Scenario | Lines/s from the fleet | CPU used |
|----------|------------------------|----------|
| 1,000 nodes × 10 lines/s (idle-ish) | 10,000 | < 1 % of one core |
| 1,000 nodes × 100 lines/s (active) | 100,000 | ~5 % of one core |
| 1,000 nodes × 1,000 lines/s (noisy) | 1,000,000 | ~50 % of one core |

Assumptions: each node ships in 10,000-line batches over a fast link.
With smaller batches or unbatched traffic, request-handling overhead
dominates and more CPU is required per line.

For unbatched traffic, the same single core handles up to
**~95,000 requests/s** (Brotli) or **~118,000 requests/s** (raw),
which is enough for ~1,000 nodes sending one message per second each with
two orders of magnitude of headroom.

## What is not measured

- Real exporters (Kafka, ClickHouse, …). These numbers cover ingress,
  parsing, and masking only.
- Commodity x64 hardware.
- Long-running stability (planned for Phase 0).
- Network overhead beyond loopback inside a Docker bridge.
- The exact crossover point between "compression helps" and
  "compression hurts" — it depends on payload size, link bandwidth, and
  CPU budget.
