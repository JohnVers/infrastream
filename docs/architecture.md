# Architecture

This document describes the internal design of InfraStream: the pipeline,
the key components, and the reasoning behind the main design decisions.

For a quick overview, see the root [`README.md`](../README.md). For current
limitations, see [`KnownIssues.md`](../KnownIssues.md). For measured
numbers, see [`docs/benchmarks/ingress.md`](benchmarks/ingress.md).

---

## Overview

InfraStream is an **edge gateway for telemetry**: it receives log batches
from many nodes over HTTP (HTTP/1.1, HTTP/2, or gRPC), validates them,
masks PII, parses JSON or protobuf, and forwards them to one or more
exporters.

It is not a general-purpose log shipper. It is a **focused ingress
component**, designed to be embedded into an existing .NET application, run
as a sidecar, or deployed standalone.

### Design goals

1. **Zero allocations on the hot path.** Parsing, validation, and masking
   must not allocate managed objects per item.
2. **UTF-8 end-to-end.** No UTF-16 conversions during parsing.
3. **Backpressure by default.** When downstream is slow, the gateway must
   apply backpressure — not buffer without limit.
4. **Small footprint.** A single CPU core must be enough for meaningful
   throughput.
5. **Native .NET integration.** `ILogger`, DI, `IOptions`, `Span<T>`.

### Non-goals

- General-purpose log processing (filtering, transforms, plugins).
- Multi-tenancy with authentication (planned).
- Persistent buffering on disk for all exporters (Kafka has a spill
  manager; generic disk buffering is planned).

---

## Pipeline

```mermaid
flowchart LR
    subgraph Ingress["Ingress protocols (exactly one per process)"]
        H1CH[Http1ConnectionHandlerProtocol]
        H1K[Http1KestrelProtocol]
        H2K[Http2KestrelProtocol]
        GRPC[GrpcIngressProtocol]
    end

    subgraph Queue["Bounded channel"]
        TCQ[TelemetryChannelQueue]
    end

    subgraph Workers["N × TelemetryProcessorWorker"]
        direction TB
        DEC[IContentDecoder<br/>identity / Brotli / gzip]
        FMT{ContentFormat}
        JSON[TelemetryBatchParser<br/>Utf8JsonReader]
        PB[TelemetryBatch.Parser<br/>ParseFrom]
        MASK[PII masking<br/>MaskInPlace / MaskEntireValue]
        DISP[EnginePipelineDispatcher]
        PROC[ITelemetryProcessor array]
        EXP[ITelemetryExporter array]
    end

    H1CH -->|TelemetryBatchPayload| TCQ
    H1K --> TCQ
    H2K --> TCQ
    GRPC --> TCQ
    TCQ --> Workers
    Workers --> DEC --> FMT
    FMT -->|Json| JSON --> MASK
    FMT -->|Protobuf| PB --> MASK
    MASK --> DISP --> PROC --> EXP
```

**Flow:**

1. **Ingress.** One protocol is active per process (`IngressProtocolSelector`,
   enforced by `IngressOptionsValidator`):
  - **H1 (`ConnectionHandler`)** — `Http1ConnectionHandlerProtocol` parses
    HTTP/1.1 on raw TCP, extracts `X-Node-Id`, `X-Environment`,
    `Content-Encoding`, and reads the body into a pooled buffer.
  - **H1 (`Kestrel`)** — `Http1KestrelProtocol` uses the standard ASP.NET
    Core pipeline; `IngestHandler` maps the endpoint.
  - **H2** — `Http2KestrelProtocol` on a dedicated port; `IngestHandler`
    maps the endpoint.
  - **gRPC** — `GrpcIngressProtocol` on a dedicated port;
    `IngestGrpcService` receives `TelemetryBatch` and enqueues native
    protobuf bytes (`ContentFormat.Protobuf`).
2. **Queue.** The body is wrapped in a `TelemetryBatchPayload` (rented from
   `TelemetryBatchPool`) and pushed into a bounded channel
   (`TelemetryChannelQueue`). If the channel is full, backpressure is
   applied via the TCP window (H1/H2) or HTTP/2 flow control (gRPC).
3. **Workers.** N `TelemetryProcessorWorker` instances (one per CPU core, or
   configured) read payloads from the channel. Each worker:
  - Decodes the payload via `IContentDecoder` (`identity`, Brotli, gzip;
    skipped when `ContentEncoding.Identity`).
  - Branches on `ContentFormat`:
    - **Json** — parses with `Utf8JsonReader` (`TelemetryBatchParser`),
      masks PII in place.
    - **Protobuf** — parses with `TelemetryBatch.Parser.ParseFrom`,
      converts fields to UTF-8, masks sensitive attributes fully and
      `message` partially.
  - Dispatches each item to the configured processors and exporters
    (`EnginePipelineDispatcher`).
4. **Export.** Each `ITelemetryExporter` receives items synchronously via
   `ExportItem(in TelemetryItem)`. `TelemetryProcessorWorker` calls
   `Flush()` every `FlushEveryNBatches` (100) batches and once more in its
   `finally` block on shutdown; a failure in one exporter is logged and does
   not block the others.
5. **Return.** After the worker finishes the batch, the payload is returned
   to the pool (`TelemetryBatchPool.Return`) and the array to
   `ArrayPool<byte>`.

---

## Components

### `InfraStream.Core` — models and interfaces

| Type | Purpose |
|------|---------|
| `TelemetryItem` | A single log line. All string-like fields are `ReadOnlyMemory<byte>` (UTF-8). |
| `LogAttribute` | Key-value pair, UTF-8. |
| `ITelemetryExporter` | Sink contract. `ExportItem(in TelemetryItem)` + `Flush()`. |
| `ITelemetryProcessor` | Filter/processor contract. `Process(ref LogProcessingContext)`. |
| `LogProcessingContext` | Mutable context passed to processors (`ref struct`). |
| `NullExporter` | No-op sink. Used for benchmarks. |
| `ConsoleExporter` | Prints items to the console. Dev/troubleshooting only. |
| `OpenTelemetryExporter` | Stub. Planned. |
| `KafkaExporter` | Implemented (spill manager, backpressure, DLQ). |

### `InfraStream.CollectorEngine` — the pipeline

| Type | Purpose |
|------|---------|
| `IIngressProtocol` | Contract for one ingress protocol (listener + services + endpoints). |
| `IngressProtocolSelector` | Selects the single enabled protocol from options. |
| `IngressOptionsValidator` | Fail-fast validation (exactly one protocol, port conflicts, etc.). |
| `Http1ConnectionHandlerProtocol` | H1 over raw TCP (`ConnectionHandler`). |
| `Http1KestrelProtocol` | H1 via Kestrel. |
| `Http2KestrelProtocol` | H2 (h2c) via Kestrel. |
| `GrpcIngressProtocol` | gRPC via Kestrel (HTTP/2 cleartext). |
| `IngestHandler` | Endpoint for Kestrel-mode H1/H2 ingress. |
| `IngestGrpcService` | gRPC service; enqueues native protobuf bytes. |
| `ContentFormat` | Enum: `Json` (HTTP) or `Protobuf` (gRPC). |
| `TelemetryChannelQueue` | Bounded channel between ingress and workers. Provides backpressure. |
| `TelemetryBatchPayload` | Pooled container for a batch (`Array`, `Length`, `ContentFormat`, `ContentEncoding`). |
| `TelemetryBatchPool` | Lock-free pool of `TelemetryBatchPayload` instances. |
| `TelemetryProcessorWorker` | Background worker. Decodes, parses (JSON or protobuf), masks, dispatches, flushes exporters. |
| `TelemetryWorkerHost` | Wraps a worker as an `IHostedService`. |
| `TelemetryBatchParser` | Zero-allocation JSON parser using `Utf8JsonReader`. |
| `TelemetryMasker` | In-place PII masking (`MaskInPlace`, `MaskEntireValue`). |
| `MessageInterner` | Static cache for well-known tokens (log levels, component names). |
| `EnginePipelineDispatcher` | Runs processors, then exporters. |
| `ITelemetryItemConsumer` | Contract for the parser's sink. |
| `ContentDecoderRegistry` / `IContentDecoder` | Pluggable content decoding (identity, Brotli, gzip). |
| `InfraStreamOptions` | Configuration model (ports, workers, queue, protocols, stream limits). |
| `CollectorEngineExtensions` | `AddCollectorEngine()` entry point. |
| `ExporterExtensions` | Fluent exporter registration. |
| `LoggingExtensions` | Dev/Prod logging setup. |

---

## Key design decisions

### 1. Zero allocations on the hot path

Every parsed item allocates **nothing**. This is achieved by:

- **`Utf8JsonReader`** — reads directly from a `ReadOnlySpan<byte>` without
  materializing strings.
- **`ReadOnlyMemory<byte>` slices** — `TelemetryItem` fields point **into
  the batch buffer**, not into copies.
- **`ArrayPool<byte>`** — buffers for decompression are rented and returned.
- **`TelemetryBatchPool`** — payload objects are reused.
- **`MessageInterner`** — well-known tokens (e.g. `INFO`, `nginx`) are
  returned from a static cache, not allocated.

**Why it matters:** the GC is the biggest enemy of throughput in .NET
services under high load. Eliminating allocations eliminates GC pauses.

### 2. UTF-8 end-to-end

The entire pipeline works with UTF-8 bytes. Conversion to `string` happens
**only** inside exporters that need it (e.g. `ConsoleExporter`).

**Why it matters:** UTF-16 ↔ UTF-8 conversions are expensive and allocate.
Keeping everything in UTF-8 removes the cost.

### 3. In-place PII masking

`TelemetryMasker.MaskInPlace` modifies the batch buffer **directly**:
- Replaces bytes with `*` at the right positions.
- Does not copy the message.
- Does not allocate.

For custom attributes whose key is sensitive (`password`, `secret`,
`token`, `api_key`), the parser uses `TelemetryMasker.MaskEntireValue`,
which replaces the **entire value** with `*` (the key already identifies it
as PII; there is no JSON structure to search inside). For `message`, the
masker searches for card numbers (Luhn) and JSON-key patterns inside the
text.

**Why it matters:** masking is applied on the hot path. A copy-based
implementation would allocate per item — defeating goal #1.

**Trade-off:** because masking mutates the source buffer, the
`rawJsonSlice` field of `TelemetryItem` contains **already-masked** data.
Consumers that expect pristine JSON must be aware of this.

**Known limitation:** plain-text patterns like `password=...` /
`secret=...` inside a plain-text `message` field are **not** masked on
either path. See [`KnownIssues.md`](../KnownIssues.md) §Masking.

### 4. Ownership contract

`TelemetryItem` contains `ReadOnlyMemory<byte>` values that point **into the
batch buffer**. The buffer is returned to the pool as soon as `ExportItem`
returns.

**The contract:**
- Read the bytes **synchronously**.
- Do **not** retain references.
- **Copy** if you need the data later.

This is documented in `ITelemetryExporter` and `LogAttribute` and enforced
by API shape (`in TelemetryItem`).

**Why it matters:** it makes the zero-allocation design possible. Without
this contract, every item would need to own its own copy of the data.

### 5. Backpressure via bounded channel

`TelemetryChannelQueue` is a `BoundedChannel<TelemetryBatchPayload>` with
`FullMode = BoundedChannelFullMode.Wait`.

When the channel is full:
- Ingress threads **wait** asynchronously in `WriteAsync`.
- The kernel **shrinks the TCP window** (H1/H2); HTTP/2 flow control
  applies (gRPC).
- Producers (edge nodes) are throttled by the transport itself.

**Why it matters:** without backpressure, a slow downstream would cause
unbounded memory growth. With backpressure, the gateway applies **flow
control** at the network layer — no queues explode, no OOM.

### 6. Parallel workers

Each `TelemetryProcessorWorker` runs on its own task. The number of workers
is `Environment.ProcessorCount` by default, or the value of
`InfraStream__WorkerCount`.

**Why it matters:** parsing and masking are CPU-bound. Running N workers on
N cores gives near-linear scaling for large batches (see
`docs/benchmarks/ingress.md`).

### 7. No metrics in the hot path

`TelemetryProcessorWorker` does **not** use `Stopwatch`, `Process`, or
per-item counters in the hot loop. Metrics are aggregated **per batch** and
logged **only when the worker stops**.

**Why it matters:** per-item timing or counting would allocate, branch, and
pollute the cache — a measurable cost at multi-million lines/s.

Uptime and rate metrics are expected to come from **host metrics**
(Prometheus / OpenTelemetry), not from the gateway itself.

### 8. Pluggable ingress protocols

Exactly one protocol is enabled per process (`IngressProtocolSelector`,
enforced by `IngressOptionsValidator`). This keeps the hot path free of
protocol branching and lets each deployment choose the transport that fits
its workload (see `docs/benchmarks/ingress.md` for measured differences).

---

## Concurrency model

```mermaid
flowchart LR
    K1[Ingress thread 1] --> Q[(Bounded channel)]
    K2[Ingress thread 2] --> Q
    KN[Ingress thread N] --> Q
    Q --> W1[Worker 1]
    Q --> W2[Worker 2]
    Q --> WN[Worker N]
```

- **Many writers** — ingress threads (H1/H2/gRPC), one per connection.
- **Several readers** — N workers, one per CPU core.
- **Single bounded channel** — no per-connection queues.

**Why single channel:** it gives a **single point of backpressure**. If a
worker is stuck, the channel fills, and **all** ingress threads experience
backpressure equally.

---

## Memory model

| Buffer | Owner | Lifetime | Pool |
|--------|-------|----------|------|
| Request body | `TelemetryBatchPayload.Array` | From rent to `Dispose` | `ArrayPool<byte>` + `TelemetryBatchPool` |
| Decompressed body | Worker-local `decompressedBuffer` | Worker lifetime | `ArrayPool<byte>` (rented once per worker) |
| Attribute array | `TelemetryBatchParser` | Per-batch | `ArrayPool<LogAttribute>` |
| Interned tokens | `MessageInterner` | Process lifetime | Static |
| JSON slices | `TelemetryItem` fields | Until `ExportItem` returns | Point into batch buffer |

---

## What is NOT in the hot path

- **Logging** — aggregated, logged on worker stop.
- **Metrics** — expected from host (Prometheus / OTel), not from the gateway.
- **String conversions** — only in exporters that need them.
- **Allocations** — none per item on the JSON path (see above). The gRPC
  path allocates per record (UTF-8 conversion from protobuf `string`s) and
  per request (`ToByteArray()`); see [`KnownIssues.md`](../KnownIssues.md)
  §gRPC.

---

## Trade-offs and known limits

- **Escaped JSON strings** skip masking and allocate (rare in practice).
- **`DecompressedBufferSize`** is configurable via
  `InfraStream:Ingress:DecompressedBufferSize` (default 16 MB). Batches
  whose decompressed size exceeds it are rejected as a batch error.
- **`MessageInterner`** only knows a fixed set of tokens. Extending to a
  dynamic cache is future work.
- **Real exporters** will add copy, serialization, and network cost.
- **gRPC path** lacks compression and double-parses protobuf (gRPC layer →
  `ToByteArray()` → worker `ParseFrom`); see [`KnownIssues.md`](../KnownIssues.md)
  §gRPC.
- **Plain-text `message` PII** is not masked on either path; see
  [`KnownIssues.md`](../KnownIssues.md) §Masking.
- **Graceful shutdown** is implemented: on `SIGTERM` the ingress queue is
  completed and workers drain in-flight batches, bounded by
  `HostOptions.ShutdownTimeout`.

See [`KnownIssues.md`](../KnownIssues.md) for the full list of current
limitations, and [`local/Roadmap.md`](Roadmap.md) for planned work.

---

## See also

- [`README.md`](../README.md) — overview and quick start.
- [`KnownIssues.md`](../KnownIssues.md) — current limitations.
- [`docs/benchmarks/ingress.md`](benchmarks/ingress.md) — measured numbers
  (H1, H2, gRPC; batch=10k and batch=1).
- [`examples/`](../examples/) — runnable examples.
- [`CONTRIBUTING.md`](../CONTRIBUTING.md) — how to contribute.
