#!/usr/bin/env bash
#
# curl-example.sh
#
# Sends three test batches to a locally running embedded-aspnet example.
#
#   1. Plain batch  — two log lines, no sensitive fields.
#   2. PII batch    — two log lines with `password`, `api_key`, and a
#                     credit-card number (Luhn-valid) in `message`.
#   3. Brotli batch — the PII batch, compressed with Content-Encoding: br.
#
# Prerequisites:
#   - The example is running: `dotnet run` in this directory.
#   - `curl` is available.
#   - `brotli` CLI is available for request #3
#     (on macOS: `brew install brotli`; on Debian/Ubuntu: `apt install brotli`).
#
# Usage:
#   ./curl-example.sh
#
set -euo pipefail

ENDPOINT="http://localhost:5005"
NODE_ID="edge-node-42"
ENVIRONMENT="prod"

# -----------------------------------------------------------------------------
# 1. Plain batch
# -----------------------------------------------------------------------------
PLAIN_BATCH='{
  "payload": [
    {
      "timestamp": "2026-10-05T12:34:56.789Z",
      "level": "INFO",
      "component": "auth-service",
      "message": "User logged in successfully"
    },
    {
      "timestamp": "2026-10-05T12:34:57.123Z",
      "level": "WARN",
      "component": "payment-processor",
      "message": "Retry attempt 2 of 3 for downstream call",
      "attempt": "2",
      "endpoint": "/v1/charge"
    }
  ]
}'

echo "==> 1. Plain batch"
curl -sS -o /dev/null -w "HTTP %{http_code}\n" \
    -X POST "${ENDPOINT}/" \
    -H "Content-Type: application/json" \
    -H "X-Node-Id: ${NODE_ID}" \
    -H "X-Environment: ${ENVIRONMENT}" \
    --data-binary "${PLAIN_BATCH}"
echo

# -----------------------------------------------------------------------------
# 2. PII batch
#    - `password` and `api_key` are sensitive attribute keys -> masked.
#    - The credit-card number inside `message` is Luhn-valid -> masked.
# -----------------------------------------------------------------------------
PII_BATCH='{
  "payload": [
    {
      "timestamp": "2026-10-05T12:35:01.001Z",
      "level": "INFO",
      "component": "auth-service",
      "message": "Login attempt for user=alice from 10.0.0.1",
      "password": "hunter2",
      "api_key": "sk-live-1234567890abcdef"
    },
    {
      "timestamp": "2026-10-05T12:35:02.002Z",
      "level": "ERROR",
      "component": "payment-processor",
      "message": "Payment failed for card 4242 4242 4242 4242, retrying",
      "token": "tok_visa_test_4242"
    }
  ]
}'

echo "==> 2. PII batch (masking applies to password / api_key / token / card number)"
curl -sS -o /dev/null -w "HTTP %{http_code}\n" \
    -X POST "${ENDPOINT}/" \
    -H "Content-Type: application/json" \
    -H "X-Node-Id: ${NODE_ID}" \
    -H "X-Environment: ${ENVIRONMENT}" \
    --data-binary "${PII_BATCH}"
echo

# -----------------------------------------------------------------------------
# 3. Brotli batch
#    Same payload as the PII batch, compressed with Brotli.
#    The parser decompresses it transparently before masking.
# -----------------------------------------------------------------------------
echo "==> 3. Brotli batch (Content-Encoding: br, same PII payload)"

if ! command -v brotli >/dev/null 2>&1; then
    echo "SKIP: 'brotli' CLI not found. Install it and re-run this step:" >&2
    echo "  macOS:         brew install brotli" >&2
    echo "  Debian/Ubuntu: sudo apt install brotli" >&2
    exit 0
fi

TMP_BR="$(mktemp -t infrastream-pii-XXXXXX.br)"
trap 'rm -f "${TMP_BR}"' EXIT

printf '%s' "${PII_BATCH}" | brotli --stdout > "${TMP_BR}"

curl -sS -o /dev/null -w "HTTP %{http_code}\n" \
    -X POST "${ENDPOINT}/" \
    -H "Content-Type: application/json" \
    -H "Content-Encoding: br" \
    -H "X-Node-Id: ${NODE_ID}" \
    -H "X-Environment: ${ENVIRONMENT}" \
    --data-binary "@${TMP_BR}"
echo

echo "Done. Expected response for each batch: HTTP 202."
