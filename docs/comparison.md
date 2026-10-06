# Comparison with Other Tools

> **Status: DRAFT — not published.**
>
> This document is a **local draft** and is **not linked from any public
> material** (README, Roadmap, docs). It will be finalized and published
> once commodity-hardware benchmarks are available, so that any performance
> claims can be backed by a reproducible methodology.
>
> Until then:
> - The root `README.md` states that the comparison is "in progress".
> - Performance claims are limited to the reference-hardware numbers in the
    >   README, explicitly labelled as not representative.
>
> **Do not link to this document** until the DRAFT label is removed.

This document compares InfraStream with **Fluent Bit** and **Vector** for the
specific case of **.NET teams** who need a telemetry ingress.

It is not a "which is best" document. Fluent Bit and Vector are mature,
excellent tools. The question is **which one fits your stack**.

---

## TL;DR — when to choose what

| If you... | Choose |
|-----------|--------|
| Are a .NET team and want telemetry ingress **inside your existing process** | **InfraStream** (embedded) |
| Are a .NET team and want a **sidecar** with a single language across the stack | **InfraStream** (sidecar) |
| Need **native `ILogger` / DI / `Span<T>` integration** | **InfraStream** |
| Need **PII masking in the hot path** without external plugins | **InfraStream** |
| Need a **general-purpose log shipper** with hundreds of input/output plugins | **Fluent Bit** |
| Already run **Fluent Bit or Vector** at scale and they work | **Stay with them** |
| Need **syslog, Windows Event Log, journald, dozens of inputs** | **Fluent Bit** or **Vector** |
| Need **VRL transforms**, routing, sampling, deduplication | **Vector** |
| Need a **Rust-based, single-binary** shipper | **Vector** |

InfraStream is a **focused ingress component**. Fluent Bit and Vector are
**general-purpose data pipelines**. They overlap, but they are not the same
category of tool.

---

## Feature matrix

| Feature | InfraStream | Fluent Bit | Vector |
|---------|-------------|------------|--------|
| **License** | MIT | Apache 2.0 | MPL 2.0 |
| **Language** | C# (.NET 10) | C | Rust |
| **Runtime requirement** | .NET 10 | Native binary | Native binary |
| **Embeddable as a library** | Yes | No | No |
| **Runs as a process** | Yes | Yes | Yes |
| **Runs as a sidecar** | Yes | Yes | Yes |
| **Input: raw TCP** | Yes | Yes | Yes |
| **Input: HTTP** | Via TCP handler | Yes | Yes |
| **Input: syslog** | No | Yes | Yes |
| **Input: Windows Event Log** | No | Yes | Yes |
| **Input: journald** | No | Yes | Yes |
| **Input: dozens of others** | No | Yes | Yes |
| **Brotli decompression** | Yes | No | No |
| **gzip / zstd / snappy** | No (only Brotli) | Yes | Yes |
| **PII masking (built-in, hot path)** | Yes | No | No |
| **Credit-card masking (Luhn)** | Yes | No | No |
| **Zero-allocation hot path** | Yes | Yes (C) | Yes (Rust) |
| **Native .NET `ILogger` integration** | Yes | No | No |
| **Native .NET DI integration** | Yes | No | No |
| **`Span<T>` / `Utf8JsonReader`** | Yes | N/A | N/A |
| **Configuration model** | `appsettings.json` + env | YAML / classic | TOML / YAML |
| **Env override convention** | `InfraStream__*` (ASP.NET Core) | Custom | Custom |
| **Built-in transforms** | No (plugins via C#) | Lua / filters | VRL |
| **Ecosystem / plugins** | Small (young project) | Large | Growing |
| **Age / maturity** | New | Mature (2015+) | Mature (2019+) |
| **Community size** | Small | Large | Large |

---

## License comparison

License is the one difference that has **legal** consequences for
commercial .NET teams, so it deserves its own section.

| License | InfraStream | Fluent Bit | Vector |
|---------|-------------|------------|--------|
| **Type** | MIT (permissive) | Apache 2.0 (permissive) | MPL 2.0 (weak copyleft) |
| **Use in closed-source products** | Yes | Yes | Yes |
| **Modify and keep private** | Yes | Yes | Modifications to Vector itself must be published under MPL 2.0 |
| **Embed as a library** | Yes | N/A (not a library) | N/A (not a library) |
| **Patent grant** | No | Yes | No |
| **NOTICE file required** | No | Yes | No |
| **Copyleft obligations** | None | None | File-level (MPL) |

### What this means in practice

- **InfraStream (MIT)** and **Fluent Bit (Apache 2.0)** are equally
  unproblematic for embedding in commercial, closed-source products. There
  are no copyleft obligations beyond preserving the license notice.
- **Vector (MPL 2.0)** is **also** usable in closed-source products, but
  **if you modify Vector's own source files**, you must publish those
  modifications under MPL 2.0. Many enterprise legal teams accept MPL 2.0,
  but it requires a review that MIT and Apache 2.0 do not.
- **None of the three** gives a patent grant. If patent risk is a concern,
  Apache 2.0 (Fluent Bit) is the safest of the three; MIT and MPL do not
  address patents.

For a .NET team that wants to **embed the gateway in their own product**
without a legal review, MIT is the simplest option.

---

## Performance and cost

Performance numbers and cost-per-million-lines estimates are **not** in this
document. A detailed comparison on equal conditions requires commodity
hardware measurements and a documented methodology, and will be published
alongside the finalized version of this document.

For reference-hardware numbers (measured with `NullExporter`), see the root
`README.md`.

**Do not compare InfraStream with Fluent Bit or Vector on performance alone**
until equal-condition benchmarks are published. The honest comparison must
use equal conditions (same compression, same batch sizes, same hardware) and
must state which exporter was used.

---

## Migration

### From Fluent Bit to InfraStream

**You gain:**

- Native .NET integration (`ILogger`, DI, `Span<T>`).
- Built-in PII masking in the hot path.
- Brotli decompression.
- Embedding in your existing ASP.NET Core process (no extra container).
- MIT license.

**You lose:**

- Fluent Bit's plugin ecosystem (syslog, Windows Event Log, journald,
  dozens of outputs).
- Lua filters.
- Years of community hardening.
- Battle-tested production deployments.

**You should migrate** only if your telemetry ingress is .NET-heavy and the
plugin ecosystem is not a blocker.

### From Vector to InfraStream

Same as above, plus:

**You gain:**

- MIT instead of MPL 2.0 (no file-level copyleft).

**You lose:**

- VRL transforms and Vector's routing / sampling / deduplication features.
- A large and active community.

### From InfraStream to Fluent Bit or Vector

You should consider switching if:

- You need a plugin ecosystem InfraStream does not have (syslog, Windows
  Event Log, journald, dozens of outputs).
- Your stack is not .NET-heavy.
- You need built-in transforms (Lua, VRL).
- You need years of production hardening.

InfraStream is honest about being a **focused tool**, not a universal one.

---

## When NOT to use InfraStream

- You need **syslog**, **Windows Event Log**, or **journald** inputs.
- You need **dozens of output plugins** out of the box.
- You need **built-in transforms** (Lua, VRL) without writing C#.
- Your team is **not** .NET-based.
- You need **years of production hardening** and a large community.
- You need **multi-protocol ingress** beyond raw TCP (HTTP/2, gRPC, UDP) —
  these are on the roadmap (Phase 5), not in the current version.

For these cases, **Fluent Bit** or **Vector** are the right tools.

---

## Compliance notes

This document follows these rules:

- **No superlatives** ("best", "#1", "fastest") without a verifiable
  criterion.
- **No disparagement** of Fluent Bit or Vector. Their strengths are stated
  alongside the differences.
- **All performance claims** are deferred until equal-condition benchmarks
  are published.
- **Key facts** (licenses, feature support) are stated accurately and
  without framing.

If you find an inaccuracy in this document, please open an issue or a PR.

---

## See also

- `README.md` — project overview.
- `docs/architecture.md` — internal design.
- `CONTRIBUTING.md` — how to contribute.
