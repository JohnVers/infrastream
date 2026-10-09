#!/bin/bash
#
# run-bench.sh — InfraStream load test suite.
#
# Matrix: 1/2/4 CPU × {Brotli, gzip, identity} = 9 runs.
# Each run uses NullExporter, PII masking enabled, 10k-line batches.
#
# Usage:
#   cd deploy
#   chmod +x run-bench.sh
#   ./run-bench.sh
#
# Output:
#   reports/<test_name>_loadtests.log  — NBomber report
#   reports/<test_name>_gateway.log    — worker metrics
#   reports/summary.txt                — summary table
#

set -e

# ---------- Colors ----------
RED='\033[0;31m'
GREEN='\033[0;32m'
BLUE='\033[0;34m'
NC='\033[0m'

# ---------- Environment check ----------
if ! command -v docker &> /dev/null; then
  echo -e "${RED}docker not found in PATH${NC}"
  exit 1
fi

if ! docker compose version &> /dev/null; then
  echo -e "${RED}docker compose (v2) not found${NC}"
  exit 1
fi

# ---------- Preparation ----------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

mkdir -p reports
rm -f reports/summary.txt

echo "=========================================="
echo "  InfraStream Load Test Suite"
echo "  Date: $(date)"
echo "=========================================="
echo ""

# ---------- Wait for healthcheck ----------
wait_healthy() {
  local container=$1
  local max_wait=${2:-60}
  echo -n "Waiting for $container to become healthy"
  for i in $(seq 1 $max_wait); do
    status=$(docker inspect "$container" --format '{{.State.Health.Status}}' 2>/dev/null || echo "missing")
    if [ "$status" = "healthy" ]; then
      echo -e " ${GREEN}OK (${i}s)${NC}"
      return 0
    fi
    echo -n "."
    sleep 1
  done
  echo -e " ${RED}TIMEOUT${NC}"
  return 1
}

# ---------- Peak-sample extraction ----------
# Extracts the metrics line with the highest batches/s from a log file.
# Usage: peak_line=$(extract_peak_line <log-file> <grep-pattern>)
extract_peak_line() {
  local log_file="$1"
  local pattern="$2"
  if [ ! -f "$log_file" ]; then
    echo ""
    return
  fi
  grep "$pattern" "$log_file" \
    | awk -F'batches/s=' '{ split($2, a, " "); print a[1] "\t" $0 }' \
    | sort -n \
    | tail -1 \
    | cut -f2- || true
}

# ---------- Single test ----------
# Parameters:
#   $1 name        — test name
#   $2 cpu         — GATEWAY_CPU (e.g. 1.0, 2.0, 4.0)
#   $3 mem         — GATEWAY_MEMORY (e.g. 1G, 2G)
#   $4 workers     — WORKER_COUNT (number of workers)
#   $5 gc_limit    — GC_HEAP_HARD_LIMIT (hex, e.g. 0x30000000)
#   $6 copies      — NBomber parallel clients
#   $7 batch       — batch size (lines)
#   $8 duration    — test duration (seconds)
#   $9 encoding    — identity | br | gzip
run_test() {
  local name="$1"
  local cpu="$2"
  local mem="$3"
  local workers="$4"
  local gc_limit="$5"
  local copies="$6"
  local batch="$7"
  local duration="${8:-60}"
  local encoding="${9:-br}"

  # Extract integer CPU value for DOTNET_PROCESSOR_COUNT.
  local cpu_int="${cpu%.*}"

  # Raw JSON batches are much larger than compressed ones; raise
  # MaxBodySize for identity so the request is not rejected with 413.
  local max_body
  if [ "$encoding" = "identity" ]; then
    max_body="4194304"   # 4 MB
  else
    max_body="1048576"   # 1 MB
  fi

  echo ""
  echo -e "${BLUE}==========================================${NC}"
  echo -e "${BLUE}  Test: $name${NC}"
  echo -e "${BLUE}  CPU: ${cpu} (int=${cpu_int}), MEM: ${mem}, Workers: ${workers}, GC: ${gc_limit}${NC}"
  echo -e "${BLUE}  Copies: ${copies}, Batch: ${batch}, Duration: ${duration}s, Encoding: ${encoding}${NC}"
  echo -e "${BLUE}  MaxBodySize: ${max_body} bytes${NC}"
  echo -e "${BLUE}==========================================${NC}"

  # 1. Stop previous containers.
  docker compose down 2>/dev/null || true

  # 2. Start.
  GATEWAY_CPU="$cpu" \
  GATEWAY_CPU_INT="$cpu_int" \
  GATEWAY_MEMORY="$mem" \
  WORKER_COUNT="$workers" \
  GC_HEAP_HARD_LIMIT="$gc_limit" \
  MAX_BODY_SIZE="$max_body" \
  LOAD_COPIES="$copies" \
  LOAD_BATCH_SIZE="$batch" \
  LOAD_DURATION="$duration" \
  LOAD_ENCODINGS="$encoding" \
    docker compose up --build -d

  # 3. Wait for healthcheck.
  if ! wait_healthy "infrastream_gateway" 60; then
    echo -e "${RED}Gateway failed to start. Skipping test $name.${NC}"
    docker compose logs infrastream_gateway > "reports/${name}_gateway_failed.log" 2>&1 || true
    docker compose down 2>/dev/null || true
    return 1
  fi

  # 4. Wait for load tests to finish.
  echo "Running test for ${duration}s..."
  docker wait infrastream_loadtests > /dev/null 2>&1 || true

  # 5. Save logs.
  docker compose logs loadtests > "reports/${name}_loadtests.log" 2>&1 || true
  docker compose logs infrastream_gateway > "reports/${name}_gateway.log" 2>&1 || true

  # 6. NBomber metrics.
  local rps lines_per_s batches_ok batches_fail p50 p99
  rps=$(grep "Avg Network RPS" "reports/${name}_loadtests.log" | tail -1 | awk '{print $NF}' || echo "N/A")
  lines_per_s=$(grep "Avg Ingress lines/s" "reports/${name}_loadtests.log" | tail -1 | awk '{print $NF}' || echo "N/A")
  batches_ok=$(grep "Total Batches OK" "reports/${name}_loadtests.log" | tail -1 | awk '{print $NF}' || echo "N/A")
  batches_fail=$(grep "Total Batches FAIL" "reports/${name}_loadtests.log" | tail -1 | awk '{print $NF}' || echo "N/A")
  p50=$(grep "p50 latency" "reports/${name}_loadtests.log" | tail -1 | awk '{print $NF}' || echo "N/A")
  p99=$(grep "p99 latency" "reports/${name}_loadtests.log" | tail -1 | awk '{print $NF}' || echo "N/A")

  # 7. Worker metrics (peak-load samples).
  local worker_log="reports/${name}_gateway.log"

  local worker_peak
  worker_peak=$(extract_peak_line "$worker_log" "metrics worker=")

  local total_peak
  total_peak=$(extract_peak_line "$worker_log" "metrics total ")

  local worker_batch_s worker_items_s worker_cpu worker_rss worker_managed
  if [ -n "$worker_peak" ]; then
    worker_batch_s=$(echo "$worker_peak" | grep -oE "batches/s=[0-9]+" | cut -d= -f2 || echo "N/A")
    worker_items_s=$(echo "$worker_peak" | grep -oE "items/s=[0-9]+"   | cut -d= -f2 || echo "N/A")
    worker_cpu=$(echo     "$worker_peak" | grep -oE "cpu=[0-9]+%"      | cut -d= -f2 | tr -d '%' || echo "N/A")
    worker_rss=$(echo     "$worker_peak" | grep -oE "rss=[0-9]+MB"     | cut -d= -f2 || echo "N/A")
    worker_managed=$(echo "$worker_peak" | grep -oE "managed=[0-9]+MB" | cut -d= -f2 || echo "N/A")
  else
    worker_batch_s="N/A"; worker_items_s="N/A"; worker_cpu="N/A"
    worker_rss="N/A"; worker_managed="N/A"
  fi

  local total_workers total_batch_s total_items_s
  if [ -n "$total_peak" ]; then
    total_workers=$(echo "$total_peak" | grep -oE "workers=[0-9]+"   | cut -d= -f2 || echo "N/A")
    total_batch_s=$(echo "$total_peak" | grep -oE "batches/s=[0-9]+" | cut -d= -f2 || echo "N/A")
    total_items_s=$(echo "$total_peak" | grep -oE "items/s=[0-9]+"   | cut -d= -f2 || echo "N/A")
  else
    total_workers="N/A"; total_batch_s="N/A"; total_items_s="N/A"
  fi

  # 8. Print results.
  echo ""
  echo -e "${GREEN}--- Results for $name ---${NC}"
  echo "  Encoding           : $encoding"
  echo "  RPS                : $rps"
  echo "  lines/s            : $lines_per_s"
  echo "  Batches OK         : $batches_ok"
  echo "  Batches FAIL       : $batches_fail"
  echo "  p50 latency        : $p50 ms"
  echo "  p99 latency        : $p99 ms"
  echo "  Worker 0 batches/s : $worker_batch_s"
  echo "  Worker 0 items/s   : $worker_items_s"
  echo "  Worker 0 cpu       : ${worker_cpu}%"
  echo "  Worker 0 rss       : $worker_rss"
  echo "  Worker 0 managed   : $worker_managed"
  echo "  Total workers      : $total_workers"
  echo "  Total batches/s    : $total_batch_s"
  echo "  Total items/s      : $total_items_s"
  echo ""

  # 9. Append to summary.
  {
    echo "$name"
    echo "  CPU: $cpu, MEM: $mem, Workers: $workers, GC: $gc_limit, Encoding: $encoding"
    echo "  Copies: $copies, Batch: $batch, MaxBodySize: $max_body"
    echo "  RPS: $rps"
    echo "  lines/s: $lines_per_s"
    echo "  Batches OK: $batches_ok, FAIL: $batches_fail"
    echo "  p50: $p50 ms, p99: $p99 ms"
    echo "  Total: workers=$total_workers, batches/s=$total_batch_s, items/s=$total_items_s"
    echo "  Worker 0: batches/s=$worker_batch_s, items/s=$worker_items_s"
    echo "  Worker 0: cpu=$worker_cpu%, rss=$worker_rss, managed=$worker_managed"
    echo ""
  } >> reports/summary.txt

  # 10. Stop.
  docker compose down 2>/dev/null || true
  sleep 2
}

# ============================================================
#  TESTS
# ============================================================
#
# Format:
#   run_test "<name>" <cpu> <mem> <workers> <gc_limit> <copies> <batch> <duration> <encoding>
#
# Memory allocation scales with CPU count:
#   1 CPU → 1G, 2 CPU → 1G, 4 CPU → 2G
#
# GC heap hard limit:
#   1–2 CPU → 0x30000000 (~768 MB)
#   4 CPU   → 0x60000000 (~1.5 GB)
#

# ============================================================
#  GROUP A: 1 CPU, 1 worker
# ============================================================

#run_test "A1_1cpu_1w_10k_br" \
#  1.0 1G 1 0x30000000 \
#  60 10000 60 br
#
#run_test "A2_1cpu_1w_10k_gzip" \
#  1.0 1G 1 0x30000000 \
#  60 10000 60 gzip
#
#run_test "A3_1cpu_1w_10k_identity" \
#  1.0 1G 1 0x30000000 \
#  60 10000 60 identity

# ============================================================
#  GROUP B: 2 CPU, 2 workers
# ============================================================

#run_test "B1_2cpu_2w_10k_br" \
#  2.0 1G 2 0x30000000 \
#  60 10000 60 br
#
#run_test "B2_2cpu_2w_10k_gzip" \
#  2.0 1G 2 0x30000000 \
#  60 10000 60 gzip
#
#run_test "B3_2cpu_2w_10k_identity" \
#  2.0 1G 2 0x30000000 \
#  60 10000 60 identity

# ============================================================
#  GROUP C: 4 CPU, 4 workers
# ============================================================

#run_test "C1_4cpu_4w_10k_br" \
#  4.0 2G 4 0x60000000 \
#  120 10000 60 br

#run_test "C2_4cpu_4w_10k_gzip" \
#  4.0 2G 4 0x60000000 \
#  120 10000 60 gzip
#
#run_test "C3_4cpu_4w_10k_identity" \
#  4.0 2G 4 0x60000000 \
#  120 10000 60 identity


# ============================================================
#  GROUP D: baseline — 1 line per request (no batching).
#  Measures pure request-handling capacity: HTTP parse, handoff,
#  worker dispatch, minimal JSON parse + masking.
#  Encoding: Brotli (recommended). Not for encoding comparison.
# ============================================================

run_test "D1_1cpu_1w_1_br" \
  1.0 1G 1 0x30000000 \
  60 1 60 br

run_test "D2_2cpu_2w_1_br" \
  2.0 1G 2 0x30000000 \
  60 1 60 br

run_test "D3_4cpu_4w_1_br" \
  4.0 2G 4 0x60000000 \
  120 1 60 br

# ============================================================
#  GROUP D: baseline — batch = 1, Brotli.
#  Pure request-handling capacity with minimal payloads.
# ============================================================

run_test "D1_1cpu_1w_1_br" \
  1.0 1G 1 0x30000000 \
  60 1 60 br

run_test "D2_2cpu_2w_1_br" \
  2.0 1G 2 0x30000000 \
  60 1 60 br

run_test "D3_4cpu_4w_1_br" \
  4.0 2G 4 0x60000000 \
  120 1 60 br

# ============================================================
#  GROUP E: baseline — batch = 1, Gzip.
# ============================================================

run_test "E1_1cpu_1w_1_gzip" \
  1.0 1G 1 0x30000000 \
  60 1 60 gzip

run_test "E2_2cpu_2w_1_gzip" \
  2.0 1G 2 0x30000000 \
  60 1 60 gzip

run_test "E3_4cpu_4w_1_gzip" \
  4.0 2G 4 0x60000000 \
  120 1 60 gzip

# ============================================================
#  GROUP F: baseline — batch = 1, Identity (raw).
#  Note: no compression, so MaxBodySize=4 MB is used by run_test().
# ============================================================

run_test "F1_1cpu_1w_1_identity" \
  1.0 1G 1 0x30000000 \
  60 1 60 identity

run_test "F2_2cpu_2w_1_identity" \
  2.0 1G 2 0x30000000 \
  60 1 60 identity

run_test "F3_4cpu_4w_1_identity" \
  4.0 2G 4 0x60000000 \
  120 1 60 identity




# ============================================================
#  SUMMARY
# ============================================================

echo ""
echo "=========================================="
echo -e "${GREEN}  All tests complete!${NC}"
echo "=========================================="
echo ""
echo "Summary:"
cat reports/summary.txt
echo ""
echo "Reports saved in: $SCRIPT_DIR/reports/"
ls -la reports/ | grep -v "^total"
