---
name: Bug report
about: Report a bug in InfraStream
title: "[Bug] "
labels: ["bug"]
assignees: []
---

<!--
Before opening a bug report:
- Search existing issues to avoid duplicates.
- For SECURITY vulnerabilities, do NOT open a public issue.
  See SECURITY.md instead.
- For questions or usage help, use GitHub Discussions.
-->

## Description

A clear and concise description of the bug.

## Steps to reproduce

1. ...
2. ...
3. ...

Minimal reproduction (if possible): a small repo, gist, or code snippet.

## Expected behavior

What you expected to happen.

## Actual behavior

What actually happened. Include error messages, stack traces, and any
relevant logs.

```
<paste logs here>
```

## Environment

- **InfraStream version / commit:** (e.g. `main@abc1234` or a release tag)
- **.NET version:** (`dotnet --version`)
- **OS:** (Windows / Linux / macOS, with version)
- **Deployment mode:** (embedded / sidecar / standalone)
- **Exporter in use:** (Null / Console / OpenTelemetry / Kafka / custom)
- **Relevant configuration:**
  ```
  InfraStream__IngressPort=...
  InfraStream__WorkerCount=...
  ```

## Additional context

Anything else that might help: screenshots, benchmark numbers, related
issues, hypotheses.
