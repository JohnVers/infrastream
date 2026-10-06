# Security Policy

## Reporting a vulnerability

**Please do not open a public issue for security vulnerabilities.**

Report them privately via one of:

- **GitHub Security Advisory:**
  [Report a vulnerability](https://github.com/JohnVers/infrastream/security/advisories/new)
  *(preferred)*
- **Email:** `johnvershinin@outlook.com`

Include:

- A description of the vulnerability.
- Steps to reproduce.
- Potential impact.

We will acknowledge receipt within **7 days** and provide an estimated
timeline for a fix.

## Supported versions

Only the latest `main` branch is supported. There are no backports to
older releases.

## Scope

In scope:

- The `InfraStream.Core`, `InfraStream.CollectorEngine`, and
  `InfraStream.Host` projects.
- The Docker image in `deploy/`.
- The examples in `examples/`.

Out of scope:

- Third-party dependencies (report to their maintainers).
- Misconfiguration by the user.
- Denial-of-service through legitimate traffic patterns (the gateway is
  designed to handle high load; backpressure is a feature, not a
  vulnerability).

## Disclosure policy

We follow **coordinated disclosure**:

1. The reporter submits the vulnerability privately.
2. Maintainers confirm and assess the impact.
3. A fix is prepared and released.
4. The vulnerability is publicly disclosed **after** the fix is available.

We will credit the reporter in the release notes unless they prefer to
remain anonymous.
