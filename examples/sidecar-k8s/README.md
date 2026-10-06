# Sidecar on Kubernetes Example

Deploy InfraStream as a **sidecar container** next to your application in the
same Kubernetes pod.

The application writes telemetry to `localhost:5005`; the sidecar receives it,
applies PII masking, and forwards it downstream. The telemetry traffic never
leaves the pod until it reaches the configured exporter.

---

## What this example shows

- A single `Deployment` with **two containers**: `app` and `infrastream-gateway`.
- The application sending telemetry to `127.0.0.1:5005` (pod-local).
- Resource isolation: separate CPU/RAM requests and limits per container.
- Liveness and readiness probes for the gateway.
- Configuration through a `ConfigMap` (non-secret) plus env vars.
- A `ClusterIP` Service exposing the ingress to other pods in the cluster
  (optional — see below).

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
- The `infrastream/gateway:latest` image available to the cluster
  (see `deploy/gateway.Dockerfile` in the repo root; for `kind`, load it with
  `kind load docker-image`).

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

All InfraStream settings are provided via `configmap.yaml` and environment
variables in `deployment.yaml`:

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
```

The env-override convention is the standard ASP.NET Core one:
`InfraStream__<Key>` overrides `InfraStream:<Key>` from `appsettings.json`.

The ingress port `5005` is chosen to avoid a clash with the ASP.NET Core
development defaults (`5000` for HTTP, `5001` for HTTPS).

---

## Resource sizing

The example uses conservative defaults:

| Container | CPU request | CPU limit | RAM request | RAM limit |
|-----------|-------------|-----------|-------------|-----------|
| `app`     | 100m        | 500m      | 128Mi       | 256Mi     |
| `infrastream-gateway` | 200m | 1000m | 256Mi | 512Mi |

Adjust based on your telemetry volume.

---

## Graceful shutdown

On pod termination, Kubernetes sends `SIGTERM` to both containers. The
gateway stops accepting new batches and drains the queue before exiting.

> **Note:** graceful shutdown is part of Phase 5 (see `Roadmap.md`).
> Until then, in-flight batches may be dropped on termination.

---

## Notes

- The example uses `NullExporter` (no downstream). Replace it with a real
  exporter (`Kafka`, `ClickHouse`, etc.) by editing the gateway's command
  line in `deployment.yaml` once those exporters are implemented.
- For embedded mode (single process, no sidecar), see
  `examples/embedded-aspnet/`.
- For a custom `ITelemetryExporter`, see `examples/custom-exporter/`.
