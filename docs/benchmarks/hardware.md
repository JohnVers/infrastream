# InfraStream — Benchmark Hardware

## Reference machine

Used for all numbers currently published in `docs/benchmarks/`. Readers
who do not have this hardware should treat these numbers as an upper
bound, not as a prediction of their own performance.

| Component | Value |
|-----------|-------|
| CPU       | Apple M5 Max |
| OS        | macOS 26 |
| Runtime   | Docker Desktop |
| Allocated | 4 CPUs, 2 GB RAM |
| .NET      | .NET 10 |
| GC        | Server GC, `GCHeapHardLimit=0x60000000` (1.5 GiB) |

## Commodity x64 machine

Not yet measured. Planned as part of the internal roadmap Phase 0
(x64 testing).

When available, this section will include at least:

- CPU model (e.g. AMD Ryzen 7 7800X3D or a modest cloud VM).
- vCPU count(s) used (1, 2, 4).
- OS and Docker version.
- .NET version.
- Cost-per-million-lines estimates derived from the same workload.

## Notes

- The M5 Max numbers are useful for relative comparison (Brotli vs gzip
  vs identity on the same machine) and for tracking regressions across
  releases. They are not a substitute for commodity-hardware numbers.
- Container resource limits are applied via `deploy.resources.limits` in
  `docker-compose.yml`.
