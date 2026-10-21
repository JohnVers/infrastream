# Sidecar on Kubernetes Example

Deploy InfraStream as a **sidecar container** next to your application in the
same Kubernetes pod.

The application writes telemetry to `localhost:5005`; the sidecar receives it,
applies PII masking, and forwards it downstream. The telemetry traffic never
leaves the pod until it reaches the configured exporter.

---

## What this example shows

- A single `Deployment` with **two containers**: `app` (placeholder nginx)
  and `infrastream-gateway`.
- The application sending telemetry to `127.0.0.1:5005` (pod-local).
- Resource isolation: separate CPU/RAM requests and limits per container.
- TCP liveness and readiness probes for the gateway on the management port.
- Configuration through a `ConfigMap` (mounted as `appsettings.json`) plus
  env-var overrides at the Deployment level.
- Hardened `securityContext` for the gateway: non-root, read-only root
  filesystem, no privilege escalation, all Linux capabilities dropped.
- An optional `ClusterIP` Service exposing the ingress to other pods in the
  cluster.

---

## When to use sidecar mode

Sidecar mode is a good fit when the telemetry ingress should run **next to**
the application but **in a separate process**, sharing the pod's network
namespace.

| Scenario | Why sidecar works |
|----------|-------------------|
| High telemetry RPS + high API RPS | Gateway has its own CPU/RAM budget; it does not compete with the app for a single cgroup |
| Different lifecycles (app restarts often, gateway rarely) | The gateway survives app restarts within the same pod; buffered telemetry is not lost |
| Different security perimeters | The app exposes a public port; the gateway only listens on `localhost` |
| Compliance / audit isolation | Telemetry runs in a separate process image with its own security context |
| Multi-language apps | The app can be in any language; the gateway is a fixed .NET binary |

If the app is itself .NET and you do not need process isolation, **embedded
mode is simpler** — see `examples/embedded-aspnet/`.

---

## When NOT to use sidecar mode

| Scenario | Why sidecar is a poor fit | Better option |
|----------|---------------------------|---------------|
| Very tight resource budget | Two containers per pod double the baseline overhead | Embedded |
| Telemetry is not per-pod, but per-cluster | A single gateway Deployment is cheaper | Dedicated Deployment |
| You need independent scaling of app and gateway | They scale together by definition of a sidecar | Dedicated Deployment |

---

## Files in this directory

```
sidecar-k8s/
├── README.md         # this file
├── namespace.yaml    # dedicated namespace for the example
├── configmap.yaml    # InfraStream configuration (ports, workers, queue)
├── deployment.yaml   # app + gateway sidecar in one pod
└── service.yaml      # ClusterIP exposing the gateway ingress (optional)
```

---

## Prerequisites

- A running Kubernetes cluster (e.g. `kind`, `minikube`, `k3d`, or a real one).
- `kubectl` configured against the cluster.
- The gateway image available to the cluster. The public
  `infrastream/gateway:latest` image is **not published yet** (Phase 4 in
  `local/Roadmap.md`). Build it locally from `deploy/gateway.Dockerfile`
  and load it into the cluster:
  - `kind`: `kind load docker-image infrastream/gateway:latest`
  - `minikube`: `minikube image load infrastream/gateway:latest`
  - or push it to a registry your cluster can pull from.

---

## Deploying

Apply the manifests in order:

```bash
kubectl apply -f namespace.yaml
kubectl apply -f configmap.yaml
kubectl apply -f deployment.yaml
kubectl apply -f service.yaml
```

Check that the pod is running with both containers:

```bash
kubectl -n infrastream-demo get pods
kubectl -n infrastream-demo describe pod -l app=my-app
```

You should see two containers in the pod: `app` and `infrastream-gateway`.

---

## Verifying

The gateway exposes a management endpoint on port `5002`. Port-forward it
and check:

```bash
kubectl -n infrastream-demo port-forward deploy/my-app 5002:5002

# in another terminal
curl http://localhost:5002/health
```

To send a test batch from inside the pod, exec into the `app` container:

```bash
kubectl -n infrastream-demo exec -it deploy/my-app -c app -- sh

# inside the container
curl -X POST http://localhost:5005/ \
     -H "Content-Type: application/json" \
     -H "X-Node-Id: my-app" \
     -H "X-Environment: prod" \
     --data-binary '{"payload":[{"timestamp":"2026-10-05T12:00:00Z","level":"INFO","component":"demo","message":"hello from sidecar"}]}'
```

The response should be `HTTP/1.1 202 Accepted`.

---

## How the sidecar talks to the app

The two containers share the pod's network namespace, so:

- The **app** reaches the gateway at `127.0.0.1:5005` — no Service needed.
- The **gateway** is not exposed outside the pod unless you apply
  `service.yaml`.

This is the key property of the sidecar pattern: telemetry traffic stays
inside the pod until it is forwarded by the gateway's exporter.

---

## Configuration

Configuration is layered:

1. **`configmap.yaml`** stores the full `appsettings.json` as a single key.
   It is mounted into the gateway container at `/app/appsettings.json`
   (via `subPath`, so it does not shadow the rest of `/app`).
2. **`deployment.yaml`** overrides individual keys with
   `InfraStream__*` environment variables. The example duplicates the
   ConfigMap values on purpose — it shows how to override specific keys at
   the Deployment level without editing the ConfigMap. Env overrides
   `appsettings.json` (standard ASP.NET Core convention).

```yaml
env:
  - name: InfraStream__IngressPort
    value: "5005"
  - name: InfraStream__ManagementPort
    value: "5002"
  - name: InfraStream__WorkerCount
    value: "2"
  - name: InfraStream__QueueCapacity
    value: "64"
  - name: DOTNET_gcServer
    value: "1"
  - name: DOTNET_GCLatencyMode
    value: "3"
```

The ingress port `5005` is chosen to avoid a clash with the ASP.NET Core
development defaults (`5000` for HTTP, `5001` for HTTPS).

---

## Resource sizing

The example uses conservative defaults:

| Container | CPU request | CPU limit | RAM request | RAM limit |
|-----------|-------------|-----------|-------------|-----------|
| `app`     | 100m        | 500m      | 128Mi       | 256Mi     |
| `infrastream-gateway` | 200m | 1000m | 256Mi | 512Mi |

Two notes:

- **`WorkerCount: 2` with a `1000m` CPU limit.** One worker per vCPU is a
  better default: for a 1-vCPU limit, set `WorkerCount: 1`. Adjust to your
  resource envelope.
- **The `app` container is a placeholder** (`nginx:alpine`). Its
  `containerPort: 80` is nginx's HTTP port, not the gateway's. Replace the
  image and ports with your real application.

---

## Security posture

Both containers run with a hardened `securityContext`:

- `runAsNonRoot: true`, `runAsUser: 1000`.
- `allowPrivilegeEscalation: false`.
- `capabilities.drop: ["ALL"]`.
- `readOnlyRootFilesystem: true` for the gateway (with `/tmp` provided as
  an `emptyDir`).

The gateway image is expected to run as a non-root user (see
`deploy/gateway.Dockerfile`).

---

## Probes

The gateway uses **TCP** probes on the management port (`5002`):

- `readinessProbe`: TCP, `initialDelaySeconds: 2`, `periodSeconds: 5`.
- `livenessProbe`: TCP, `initialDelaySeconds: 10`, `periodSeconds: 10`.

A TCP probe is used because the base image does not ship `curl` or `wget`.
This is noted in `KnownIssues.md`; switching to an HTTP probe against
`/health` or `/ready` is future work.

---

## Graceful shutdown

Graceful shutdown is **implemented** (see `Done.md` §Observability &
lifecycle): on `SIGTERM`, the gateway completes the ingress queue and drains
in-flight batches before exiting, bounded by `HostOptions.ShutdownTimeout`
(default 30 s). New requests during shutdown receive `503 Service
Unavailable`.

> Kubernetes sends `SIGTERM` to both containers. If the app keeps writing
> to `127.0.0.1:5005` after the gateway starts draining, those requests get
> `503` — the app should stop writing on its own `SIGTERM`.

---

## Protocol scope

This example enables the **HTTP/1.1** ingress on `5005` only. To enable
HTTP/2 (`5006`) or gRPC (`5007`):

1. Add the corresponding `containerPort` entries in `deployment.yaml`.
2. Set exactly one of `InfraStream__Protocols__Http2__Enabled=true` or
   `InfraStream__Protocols__Grpc__Enabled=true` (the validator enforces
   "exactly one protocol per process"; disable H1 with
   `InfraStream__Protocols__Http1__Enabled=false`).
3. Optionally extend `service.yaml` with the new ports.

See `docs/benchmarks/ingress.md` for measured differences between H1, H2,
and gRPC.

---

## Notes

- The example uses `NullExporter` (no downstream). Replace it with a real
  exporter (Kafka is available today; ClickHouse/S3/OTLP are planned) by
  editing the gateway's configuration in `configmap.yaml` and/or
  `deployment.yaml`.
- For embedded mode (single process, no sidecar), see
  `examples/embedded-aspnet/`.
- For a custom `ITelemetryExporter`, see `examples/custom-exporter/`.
