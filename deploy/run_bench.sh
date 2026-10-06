#!/bin/bash
#
# run-bench.sh — InfraStream load test suite.
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

# ---------- Single test ----------
# Parameters:
#   $1 name        — test name
#   $2 cpu         — GATEWAY_CPU (e.g. 1.0, 2.0, 4.0)
#   $3 mem         — GATEWAY_MEMORY (e.g. 1G)
#   $4 workers     — WORKER_COUNT (number of workers)
#   $5 gc_limit    — GC_HEAP_HARD_LIMIT (hex, e.g. 0x30000000)
#   $6 copies      — number of parallel NBomber clients
#   $7 batch       — batch size (lines)
#   $8 duration    — test duration (seconds)
#   $9 compress    — true/false (Brotli / raw)
run_test() {
  local name="$1"
  local cpu="$2"
  local mem="$3"
  local workers="$4"
  local gc_limit="$5"
  local copies="$6"
  local batch="$7"
  local duration="${8:-60}"
  local compress="${9:-true}"

  # Extract integer CPU value for DOTNET_PROCESSOR_COUNT.
  local cpu_int="${cpu%.*}"

  echo ""
  echo -e "${BLUE}==========================================${NC}"
  echo -e "${BLUE}  Test: $name${NC}"
  echo -e "${BLUE}  CPU: ${cpu} (int=${cpu_int}), MEM: ${mem}, Workers: ${workers}, GC: ${gc_limit}${NC}"
  echo -e "${BLUE}  Copies: ${copies}, Batch: ${batch}, Duration: ${duration}s, Compress: ${compress}${NC}"
  echo -e "${BLUE}==========================================${NC}"

  # 1. Stop previous containers.
  docker compose down 2>/dev/null || true

  # 2. Start.
  GATEWAY_CPU="$cpu" \
  GATEWAY_CPU_INT="$cpu_int" \
  GATEWAY_MEMORY="$mem" \
  WORKER_COUNT="$workers" \
  GC_HEAP_HARD_LIMIT="$gc_limit" \
  LOAD_COPIES="$copies" \
  LOAD_BATCH_SIZE="$batch" \
  LOAD_DURATION="$duration" \
  LOAD_COMPRESS="$compress" \
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

  # 7. Worker metrics (worker 0).
  local worker_batch_s worker_items_s worker_cpu worker_rss worker_managed
  worker_batch_s=$(grep "worker" "reports/${name}_gateway.log" | tail -1 | grep -oE "batch/s=[0-9]+" | cut -d= -f2 || echo "N/A")
  worker_items_s=$(grep "worker" "reports/${name}_gateway.log" | tail -1 | grep -oE "items/s=[0-9]+" | cut -d= -f2 || echo "N/A")
  worker_cpu=$(grep "worker" "reports/${name}_gateway.log" | tail -1 | grep -oE "cpu=[0-9]+%" | cut -d= -f2 || echo "N/A")
  worker_rss=$(grep "worker" "reports/${name}_gateway.log" | tail -1 | grep -oE "rss=[0-9]+MB" | cut -d= -f2 || echo "N/A")
  worker_managed=$(grep "worker" "reports/${name}_gateway.log" | tail -1 | grep -oE "managed=[0-9]+MB" | cut -d= -f2 || echo "N/A")

  # 8. Print results.
  echo ""
  echo -e "${GREEN}--- Results for $name ---${NC}"
  echo "  RPS              : $rps"
  echo "  lines/s          : $lines_per_s"
  echo "  Batches OK       : $batches_ok"
  echo "  Batches FAIL     : $batches_fail"
  echo "  p50 latency      : $p50 ms"
  echo "  p99 latency      : $p99 ms"
  echo "  Worker batch/s   : $worker_batch_s"
  echo "  Worker items/s   : $worker_items_s"
  echo "  Worker cpu       : $worker_cpu"
  echo "  Worker rss       : $worker_rss"
  echo "  Worker managed   : $worker_managed"
  echo ""

  # 9. Append to summary.
  {
    echo "$name"
    echo "  CPU: $cpu, MEM: $mem, Workers: $workers, GC: $gc_limit, Compress: $compress"
    echo "  Copies: $copies, Batch: $batch"
    echo "  RPS: $rps"
    echo "  lines/s: $lines_per_s"
    echo "  Batches OK: $batches_ok, FAIL: $batches_fail"
    echo "  p50: $p50 ms, p99: $p99 ms"
    echo "  Worker 0: batch/s=$worker_batch_s, items/s=$worker_items_s"
    echo "  Worker 0: cpu=$worker_cpu, rss=$worker_rss, managed=$worker_managed"
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
#   run_test "<name>" <cpu> <mem> <workers> <gc_limit> <copies> <batch> <duration> <compress>
#

# ============================================================
#  GROUP A: compression comparison on 1 CPU (key experiment)
# ============================================================

run_test "A1_1cpu_1w_10k_brotli" \
  1.0 1G 1 0x30000000 \
  60 10000 60 true

run_test "A2_1cpu_1w_10k_raw" \
  1.0 1G 1 0x30000000 \
  60 10000 60 false

# ============================================================
#  GROUP B: compression comparison with small batch
# ============================================================

run_test "B1_1cpu_1w_1k_brotli" \
  1.0 1G 1 0x30000000 \
  120 1000 60 true

run_test "B2_1cpu_1w_1k_raw" \
  1.0 1G 1 0x30000000 \
  120 1000 60 false

# ============================================================
#  GROUP C: scaling (with Brotli)
# ============================================================

run_test "C1_2cpu_2w_10k_brotli" \
  2.0 1G 2 0x30000000 \
  60 10000 60 true

run_test "C2_4cpu_4w_10k_brotli" \
  4.0 2G 4 0x60000000 \
  120 10000 60 true

# ============================================================
#  GROUP D: scaling (raw, no compression)
# ============================================================

run_test "D1_2cpu_2w_10k_raw" \
  2.0 1G 2 0x30000000 \
  60 10000 60 false

run_test "D2_4cpu_4w_10k_raw" \
  4.0 2G 4 0x60000000 \
  120 10000 60 false

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
