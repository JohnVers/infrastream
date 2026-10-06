# InfraStream Examples

Runnable examples showing how to integrate InfraStream in different
deployment scenarios.

Each subdirectory contains a self-contained example with its own
`README.md`, build files, and run instructions.

---

## Which example should I look at?

| If you want to... | Go to |
|-------------------|-------|
| Add telemetry ingress to an existing ASP.NET Core service | [`embedded-aspnet/`](embedded-aspnet/) |
| Run the gateway as a sidecar container in Kubernetes | [`sidecar-k8s/`](sidecar-k8s/) |
| Write your own `ITelemetryExporter` | [`custom-exporter/`](custom-exporter/) |

---

## Examples

### `embedded-aspnet/` — embedded in an existing ASP.NET Core app

Shows the smallest possible integration: two lines in `Program.cs` and the
application already accepts telemetry over raw TCP on port `5005`.

- **Good for:** existing .NET services, local debugging, internal gateways,
  multi-tenant SaaS, legacy adapters.
- **Not for:** high telemetry RPS alongside high API RPS, separate security
  perimeters, different lifecycles.
- See [`embedded-aspnet/README.md`](embedded-aspnet/README.md).

### `sidecar-k8s/` — sidecar in Kubernetes

Deploys InfraStream as a sidecar container next to your application in the
same pod. The app talks to the gateway at `127.0.0.1:5005`; telemetry traffic
never leaves the pod until it reaches the configured exporter.

- **Good for:** process isolation, separate CPU/RAM budgets, independent
  lifecycles, compliance boundaries.
- **Not for:** very tight resource budgets, cluster-wide gateways.
- See [`sidecar-k8s/README.md`](sidecar-k8s/README.md).

### `custom-exporter/` — write your own exporter

Implements a minimal `FileExporter` that writes every telemetry item as JSONL
to a file. Demonstrates the ownership contract, thread safety, and deferred
I/O.

- **Good for:** downstreams not covered by built-in exporters, custom
  serialization formats, compliance / audit sinks, tests.
- **Not for:** cases covered by `AddNullExporter()` / `AddConsoleExporter()`.
- See [`custom-exporter/README.md`](custom-exporter/README.md).

---

## Prerequisites

All examples require the **.NET 10 SDK**.

Additionally:

| Example | Extra requirements |
|---------|-------------------|
| `embedded-aspnet/` | `curl`, `brotli` CLI (for the compressed test batch) |
| `sidecar-k8s/`     | A Kubernetes cluster (`kind`, `minikube`, `k3d`, ...), `kubectl`, the `infrastream/gateway:latest` image |
| `custom-exporter/` | `curl` |

Install hints:

```bash
# brotli (macOS)
brew install brotli

# brotli (Debian / Ubuntu)
sudo apt install brotli

# kind (macOS)
brew install kind
```

---

## Running any example

Each example is a standalone project. From its directory:

```bash
dotnet run
```

Then follow the example-specific `README.md` for sending test batches and
inspecting the output.

---

## Notes

- These examples are intentionally minimal. They are **not** production-grade:
  no authentication, no TLS, no persistence, no retry logic.
- For the full feature set and roadmap, see the repository root `README.md`
  and `Roadmap.md`.
- For design documentation, see [`docs/architecture.md`](../docs/architecture.md).
