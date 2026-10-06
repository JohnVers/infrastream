---
name: Feature request
about: Suggest an idea for InfraStream
title: "[Feature] "
labels: ["enhancement"]
assignees: []
---

<!--
Before opening a feature request:
- Search existing issues and the Roadmap.md to avoid duplicates.
- Check if the feature is already planned (see Roadmap.md §Phase 5).
- For usage questions, use GitHub Discussions.
-->

## Problem

What problem are you trying to solve? Describe the use case, not just the
solution.

Example: "I want to send telemetry to ClickHouse, but InfraStream only has
Null/Console exporters implemented."

## Proposed solution

What would you like to see? Be as specific as you can.

Example: "A `ClickHouseExporter` that batches items and inserts them via
the ClickHouse HTTP interface."

## Alternatives considered

What other approaches did you try or consider? Why do they not work?

Example: "I tried using a custom exporter via `AddCustomExporter<T>()`,
but I had to write the batching logic myself."

## Additional context

- Does this align with the project's positioning (edge gateway for .NET)?
- Does it fit into an existing Roadmap phase?
- Are you willing to contribute a PR?
- Any relevant links, benchmarks, or references.
