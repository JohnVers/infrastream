# InfraStream — Benchmarks

Performance measurements for InfraStream. Every published number must
include enough context to be reproducible and comparable.

## Contents

- [Hardware](hardware.md) — reference and commodity machines used.
- [Ingress](ingress.md) — throughput of the ingress pipeline with
  different content encodings (identity, Brotli, gzip).

Additional benchmarks (masking, exporters, commodity hardware) will be
added as measurements become available.

## Policy

- **No bare numbers.** Every figure in these documents must be
  accompanied by hardware, workload, and tooling. A number without
  context is not a benchmark.
- **Reproducible first.** If a figure cannot be reproduced from the
  documented command line and configuration, it does not belong here.
- **Reference vs commodity.** Numbers from the reference machine
  (`hardware.md`) are labelled as such. Commodity-hardware numbers are
  labelled separately and take precedence for real-world expectations.
- **No superlatives.** We do not claim "fastest", "best", or "#1". We
  report what we measured, on what hardware, and under what conditions.
- **Comparisons.** Any comparison with other projects follows the rules
  in the internal roadmap (equal conditions, disclosed methodology, no
  disparagement).

## How to reproduce

Each benchmark document contains:

1. The exact command (docker compose invocation or `dotnet run`).
2. The relevant configuration (environment variables, `appsettings.json`
   overrides).
3. The tool and version used for load generation.
4. What is *not* measured.
