# Contributing to InfraStream

Thanks for your interest in improving InfraStream.

This document explains how to build the project, run tests, and submit
changes. By participating, you agree to the
[Code of Conduct](CODE_OF_CONDUCT.md).

---

## Prerequisites

- **.NET 10 SDK** — [download](https://dotnet.microsoft.com/download)
- **Git**
- Optional: **Docker** (for `deploy/` and `examples/sidecar-k8s/`)
- Optional: **`brotli` CLI** (for `examples/embedded-aspnet/curl-example.sh`)

---

## Building

```bash
git clone https://github.com/JohnVers/infrastream.git
cd infrastream
dotnet build InfraStream.Net.slnx
```

The main solution (`InfraStream.Net.slnx`) contains **all** projects:
`src/`, `tests/`, and `examples/`.

---

## Two solutions: which one to use?

| Solution | Purpose | Contents |
|----------|---------|----------|
| `InfraStream.Net.slnx` | Local development | Everything: `src/`, `tests/`, `examples/` |
| `InfraStream.CI.slnx`  | CI and quick local checks | `src/` + `tests/CollectorEngine.Tests/` only |

**Use `InfraStream.Net.slnx`** when you are working on the project
normally.

**Use `InfraStream.CI.slnx`** when you want to reproduce exactly what CI
runs — for example, before opening a pull request:

```bash
dotnet build InfraStream.CI.slnx
dotnet test InfraStream.CI.slnx
```

CI deliberately excludes `CollectorEngine.Benchmarks` and `LoadTests`
because they require dedicated hardware and run manually (see
`deploy/run_bench.sh`).

---

## Running tests

```bash
dotnet test InfraStream.Net.slnx --filter "FullyQualifiedName!~LoadTests&FullyQualifiedName!~Benchmarks"
```

Or, to run only the unit tests (fast path):

```bash
dotnet test tests/CollectorEngine.Tests/CollectorEngine.Tests.csproj
```

Test projects:

| Project | What it covers |
|---------|----------------|
| `tests/CollectorEngine.Tests/` | Unit tests (xUnit) |
| `tests/CollectorEngine.Benchmarks/` | Micro-benchmarks (BenchmarkDotNet) |
| `tests/LoadTests/` | End-to-end load tests (NBomber 4.x — see [Dependencies](#dependencies-and-licenses)) |

---

## Submitting a pull request

1. **Fork** the repository and create a feature branch:
   ```bash
   git checkout -b feature/my-change
   ```
2. **Make your changes.** Keep commits focused and small.
3. **Run the CI solution locally** to catch issues early:
   ```bash
   dotnet build InfraStream.CI.slnx
   dotnet test InfraStream.CI.slnx
   ```
4. **Sign off your commits** — see [DCO](#developer-certificate-of-origin-dco) below.
5. **Open a pull request** against `main`. Fill in the PR template.

CI will run `dotnet build` and `dotnet test` on `InfraStream.CI.slnx`.
The DCO check will verify that every commit is signed off.

---

## Developer Certificate of Origin (DCO)

This project uses the **Developer Certificate of Origin** instead of a
Contributor License Agreement (CLA). The DCO is a lightweight statement
that you have the right to submit the code you are contributing.

By signing off on a commit, you certify that:

1. You are the author of the contribution, **or** you have the right to
   submit it on behalf of the author.
2. You license the contribution under the project's license (**MIT**).
3. You understand that the contribution is public and recorded in the
   project's git history.

The full text of the DCO is available at
[developercertificate.org](https://developercertificate.org/).

### How to sign off

Add the `-s` flag to `git commit`:

```bash
git commit -s -m "Add support for UDP ingress"
```

This appends a line to the commit message:

```
Signed-off-by: Your Name <your.email@example.com>
```

The name and email must match the identity you want recorded in the
project's git history. If you have already made commits without `-s`, you
can amend them:

```bash
git commit --amend -s
```

To sign off a range of commits, use an interactive rebase:

```bash
git rebase --signoff HEAD~3
```

The DCO check runs automatically on every PR via GitHub Actions
(`.github/workflows/dco.yml`). PRs with unsigned commits will not be merged.

---

## Adding a new project

If you add a new `.csproj` to the repository, you must register it in the
appropriate solution file(s):

| Where you add the project | Update which solution(s) |
|---------------------------|--------------------------|
| `src/`                    | Both `InfraStream.Net.slnx` **and** `InfraStream.CI.slnx` |
| `tests/` (unit tests)     | Both `InfraStream.Net.slnx` **and** `InfraStream.CI.slnx` |
| `tests/` (benchmarks, load tests) | Only `InfraStream.Net.slnx` |
| `examples/`               | Only `InfraStream.Net.slnx` |

Both `.slnx` files are plain XML and can be edited by hand or via
`dotnet sln add <path>`. CI runs on `InfraStream.CI.slnx`, so a project
that is missing from it will **not** be built or tested in CI.

---

## Code style

- **All comments, XML-doc, log messages, and exception messages are in
  English.**
- **Follow `.editorconfig`** — it defines formatting, naming, and analyzer
  rules. Most editors apply it automatically.
- **Public APIs must have XML-doc.**
- **Do not introduce new allocations on the hot path** unless discussed in
  an issue first. See `docs/architecture.md` for the design principles.
- **Keep PRs focused.** One logical change per PR.

---

## Reporting issues

> **This is a volunteer-maintained project.** There is no SLA for bug
> fixes. Maintainers will respond to issues when time permits, with
> priority given to security vulnerabilities and regressions.

The maintainer **does** read every issue and will respond when possible.
Well-written reports with a minimal reproduction are addressed fastest.

Use the GitHub issue templates:

- **Bug report** — include .NET version, OS, steps to reproduce, expected
  vs actual behavior, and any relevant logs.
- **Feature request** — explain the use case and why existing
  functionality is insufficient.

For security issues, **do not** open a public issue. See
[SECURITY.md](SECURITY.md) instead.

---

## Dependencies and licenses

All dependencies must be **compatible with the project's MIT license**.

Notably:

- **NBomber 5+** is **not** open source (free for personal use only;
  commercial license required for organizations). **Pin to NBomber 4.x
  (Apache 2.0).**
- Prefer MIT / Apache 2.0 / BSD dependencies. Avoid GPL, AGPL, and
  commercial licenses unless discussed in an issue first.

If you are unsure whether a dependency is compatible, ask in the PR before
adding it.

---

## License

By contributing, you agree that your contributions will be licensed under
the [MIT License](LICENSE).
