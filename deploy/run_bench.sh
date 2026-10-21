#!/bin/bash
#
# run-bench.sh — InfraStream load test suite (NBomber only).
#
# Matrix:
#   H1 (ConnectionHandler, Kestrel) × {br, gzip, identity} × {1, 2, 4} CPU × {10k, 1} batch
#   H2 (NBomber, HTTP/2) × {br, gzip, identity} × {1, 2, 4} CPU × {10k, 1} batch
#   gRPC (NBomber, protobuf) × identity × {1, 2, 4} CPU × {10k, 1} batch
#
# Usage:
#   cd deploy
#   chmod +x run-bench.sh
#   ./run-bench.sh
#
# Filters (env, comma-separated; empty = all):
#   ENCODINGS=br,gzip             identity | br | gzip
#   CPU_CONFIGS=1,4               1 | 2 | 4
#   BATCHES=10000                 10000 | 1
#   HTTP1_MODE=ConnectionHandler  ConnectionHandler | Kestrel
#   HTTP2_ENABLED=true            run HTTP/2 group (NBomber)
#   GRPC_ENABLED=true             run gRPC group (NBomber, protobuf)
#
# Output:
#   reports/<test_name>_loadtests.log
#   reports/<test_name>_gateway.log
#   reports/summary.txt
#   reports/skipped.txt
#

set -e

RED='\033[0;31m'
GREEN='\033[0;32m'
BLUE='\033[0;34m'
NC='\033[0m'

if ! command -v docker &> /dev/null; then
  echo -e "${RED}docker not found in PATH${NC}"
  exit 1
fi

if ! docker compose version &> /dev/null; then
  echo -e "${RED}docker compose (v2) not found${NC}"
  exit 1
fi

ENCODINGS_FILTER="${ENCODINGS:-}"
CPU_FILTER="${CPU_CONFIGS:-}"
BATCH_FILTER="${BATCHES:-}"
HTTP1_MODE_FILTER="${HTTP1_MODE:-}"
HTTP2_ENABLED="${HTTP2_ENABLED:-false}"
GRPC_ENABLED="${GRPC_ENABLED:-false}"

matches_filter() {
  local value="$1"
  local filter="$2"
  [ -z "$filter" ] && return 0
  case ",$filter," in
    *",$value,"*) return 0 ;;
    *) return 1 ;;
  esac
}

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

mkdir -p reports
rm -f reports/summary.txt
rm -f reports/skipped.txt

echo "=========================================="
echo "  InfraStream Load Test Suite (NBomber)"
echo "  Date: $(date)"
echo "=========================================="
echo "  Filters:"
echo "    ENCODINGS    = ${ENCODINGS_FILTER:-<all>}"
echo "    CPU_CONFIGS  = ${CPU_FILTER:-<all>}"
echo "    BATCHES      = ${BATCH_FILTER:-<all>}"
echo "    HTTP1_MODE   = ${HTTP1_MODE_FILTER:-<all>}"
echo "    HTTP2_ENABLED= ${HTTP2_ENABLED}"
echo "    GRPC_ENABLED = ${GRPC_ENABLED}"
echo "=========================================="
echo ""

kv() {
  printf "  %-18s : %s\n" "$1" "$2"
}

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

extract_field() {
  local line="$1"
  local regex="$2"
  local fallback="$3"
  if [ -z "$line" ]; then echo "$fallback"; return; fi
  local val
  val=$(printf '%s' "$line" | grep -oE "$regex" | head -1 | sed 's/^[^=]*=//' || true)
  if [ -z "$val" ]; then echo "$fallback"; else echo "$val"; fi
}

extract_peak_line() {
  local log_file="$1"
  local pattern="$2"
  if [ ! -f "$log_file" ]; then echo ""; return; fi
  grep "$pattern" "$log_file" \
    | awk -F'batches/s=' '{ split($2, a, " "); print a[1] "\t" $0 }' \
    | sort -n | tail -1 | cut -f2- || true
}

collect_worker_metrics() {
  local name="$1"
  local worker_log="reports/${name}_gateway.log"

  local worker_peak total_peak
  worker_peak=$(extract_peak_line "$worker_log" "metrics worker=")
  total_peak=$(extract_peak_line "$worker_log" "metrics total ")

  W_BATCH_S=$(extract_field "$worker_peak" 'batches/s=[0-9]+'  "N/A")
  W_ITEMS_S=$(extract_field "$worker_peak" 'items/s=[0-9]+'    "N/A")
  W_CPU=$(    extract_field "$worker_peak" 'cpu=[0-9.]+%'      "N/A")
  W_CPU="${W_CPU%\%}"
  if [[ "$W_CPU" =~ ^[0-9.]+$ ]]; then
    W_CPU=$(printf '%.0f' "$W_CPU")
  fi
  W_RSS=$(    extract_field "$worker_peak" 'rss=[0-9]+MB'      "N/A")
  W_MANAGED=$(extract_field "$worker_peak" 'managed=[0-9]+MB'  "N/A")

  T_WORKERS=$(extract_field "$total_peak" 'workers=[0-9]+'     "N/A")
  T_BATCH_S=$(extract_field "$total_peak" 'batches/s=[0-9]+'   "N/A")
  T_ITEMS_S=$(extract_field "$total_peak" 'items/s=[0-9]+'     "N/A")
}

resolve_metrics_src() {
  local name="$1"
  local stdout_log="reports/${name}_loadtests.log"
  if [ -f "$stdout_log" ] && grep -q "Avg Network RPS" "$stdout_log"; then
    echo "$stdout_log"; return
  fi
  local nbomber_txt
  nbomber_txt=$(find "$SCRIPT_DIR" -path "*gateway_ingest_*/*.txt" 2>/dev/null | tail -1 || true)
  if [ -n "$nbomber_txt" ] && [ -f "$nbomber_txt" ]; then
    echo "stdout incomplete; using NBomber report: $nbomber_txt" >&2
    echo "$nbomber_txt"; return
  fi
  echo "$stdout_log"
}

print_results() {
  local name="$1" encoding="$2" mode="$3" url="$4"
  local rps="$5" lines_per_s="$6" ok="$7" fail="$8" p50="$9" p99="${10}"

  echo ""
  echo -e "${GREEN}--- Results for $name ---${NC}"
  kv "Encoding" "$encoding"
  kv "Mode" "$mode"
  kv "URL" "$url"
  kv "RPS" "$rps"
  kv "lines/s" "$lines_per_s"
  kv "Batches OK" "$ok"
  kv "Batches FAIL" "$fail"
  kv "p50 latency" "$p50 ms"
  kv "p99 latency" "$p99 ms"
  kv "Worker 0 batches/s" "$W_BATCH_S"
  kv "Worker 0 items/s" "$W_ITEMS_S"
  kv "Worker 0 cpu" "${W_CPU}%"
  kv "Worker 0 rss" "$W_RSS"
  kv "Worker 0 managed" "$W_MANAGED"
  kv "Total workers" "$T_WORKERS"
  kv "Total batches/s" "$T_BATCH_S"
  kv "Total items/s" "$T_ITEMS_S"
  echo ""
}

write_summary() {
  local name="$1" cpu="$2" mem="$3" workers="$4" gc="$5" encoding="$6"
  local mode="$7" url="$8" copies="$9" batch="${10}" max_body="${11}"
  local rps="${12}" lines_per_s="${13}" ok="${14}" fail="${15}" p50="${16}" p99="${17}"

  {
    echo "$name"
    kv "CPU" "$cpu"
    kv "MEM" "$mem"
    kv "Workers" "$workers"
    kv "GC" "$gc"
    kv "Encoding" "$encoding"
    kv "Mode" "$mode"
    kv "URL" "$url"
    kv "Copies" "$copies"
    kv "Batch" "$batch"
    kv "MaxBodySize" "$max_body"
    kv "RPS" "$rps"
    kv "lines/s" "$lines_per_s"
    kv "Batches OK" "$ok"
    kv "Batches FAIL" "$fail"
    kv "p50 latency" "$p50 ms"
    kv "p99 latency" "$p99 ms"
    kv "Worker 0 batches/s" "$W_BATCH_S"
    kv "Worker 0 items/s" "$W_ITEMS_S"
    kv "Worker 0 cpu" "${W_CPU}%"
    kv "Worker 0 rss" "$W_RSS"
    kv "Worker 0 managed" "$W_MANAGED"
    kv "Total workers" "$T_WORKERS"
    kv "Total batches/s" "$T_BATCH_S"
    kv "Total items/s" "$T_ITEMS_S"
    echo ""
  } >> reports/summary.txt
}

# ---------- Http1 (NBomber) ----------
run_test() {
  local base_name="$1" cpu="$2" mem="$3" workers="$4" gc_limit="$5"
  local copies="$6" batch="$7" duration="${8:-60}" encoding="${9:-br}"
  local http1_mode="${10:-ConnectionHandler}"

  if ! matches_filter "$encoding" "$ENCODINGS_FILTER"; then
    echo "skip $base_name: encoding=$encoding" >> reports/skipped.txt; return 0; fi
  if ! matches_filter "${cpu%.*}" "$CPU_FILTER"; then
    echo "skip $base_name: cpu=$cpu" >> reports/skipped.txt; return 0; fi
  if ! matches_filter "$batch" "$BATCH_FILTER"; then
    echo "skip $base_name: batch=$batch" >> reports/skipped.txt; return 0; fi
  if ! matches_filter "$http1_mode" "$HTTP1_MODE_FILTER"; then
    echo "skip $base_name: mode=$http1_mode" >> reports/skipped.txt; return 0; fi

  local prefix; if [ "$http1_mode" = "Kestrel" ]; then prefix="H1K"; else prefix="H1CH"; fi
  local name="${prefix}_${base_name}"
  local cpu_int="${cpu%.*}"
  local max_body; if [ "$encoding" = "identity" ]; then max_body="4194304"; else max_body="1048576"; fi
  local load_url="http://infrastream_gateway:5005/api/v1/ingest"

  echo ""
  echo -e "${BLUE}==========================================${NC}"
  echo -e "${BLUE}  Test: $name${NC}"
  echo -e "${BLUE}  CPU: ${cpu}, MEM: ${mem}, Workers: ${workers}, GC: ${gc_limit}${NC}"
  echo -e "${BLUE}  Copies: ${copies}, Batch: ${batch}, Duration: ${duration}s, Encoding: ${encoding}${NC}"
  echo -e "${BLUE}  Mode: ${http1_mode}${NC}"
  echo -e "${BLUE}==========================================${NC}"

  docker compose down 2>/dev/null || true

  GATEWAY_CPU="$cpu" GATEWAY_CPU_INT="$cpu_int" GATEWAY_MEMORY="$mem" \
  WORKER_COUNT="$workers" GC_HEAP_HARD_LIMIT="$gc_limit" MAX_BODY_SIZE="$max_body" \
  HTTP1_ENABLED=true HTTP2_ENABLED=false GRPC_ENABLED=false HTTP1_MODE="$http1_mode" \
  LOAD_URL="$load_url" LOAD_HTTP2=false LOAD_PROTOCOL=http LOAD_COPIES="$copies" \
  LOAD_BATCH_SIZE="$batch" LOAD_DURATION="$duration" LOAD_ENCODINGS="$encoding" \
    docker compose up --build -d

  if ! wait_healthy "infrastream_gateway" 60; then
    echo -e "${RED}Gateway failed to start. Skipping $name.${NC}"
    docker compose logs infrastream_gateway > "reports/${name}_gateway_failed.log" 2>&1 || true
    docker compose down 2>/dev/null || true; return 1
  fi

  echo "Running for ${duration}s..."
  docker wait infrastream_loadtests > /dev/null 2>&1 || true
  sleep 3

  docker compose logs loadtests > "reports/${name}_loadtests.log" 2>&1 || true
  docker compose logs infrastream_gateway > "reports/${name}_gateway.log" 2>&1 || true

  local metrics_src; metrics_src=$(resolve_metrics_src "$name")

  local rps lines_per_s batches_ok batches_fail p50 p99
  rps=$(grep "Avg Network RPS" "$metrics_src" | tail -1 | awk '{print $NF}' || echo "N/A")
  lines_per_s=$(grep "Avg Ingress lines/s" "$metrics_src" | tail -1 | awk '{print $NF}' || echo "N/A")
  batches_ok=$(grep "Total Batches OK" "$metrics_src" | tail -1 | awk '{print $NF}' || echo "N/A")
  batches_fail=$(grep "Total Batches FAIL" "$metrics_src" | tail -1 | awk '{print $NF}' || echo "N/A")
  p50=$(grep "p50 latency" "$metrics_src" | tail -1 | awk '{print $NF}' || echo "N/A")
  p99=$(grep "p99 latency" "$metrics_src" | tail -1 | awk '{print $NF}' || echo "N/A")

  collect_worker_metrics "$name"
  print_results "$name" "$encoding" "$http1_mode" "$load_url" "$rps" "$lines_per_s" "$batches_ok" "$batches_fail" "$p50" "$p99"
  write_summary "$name" "$cpu" "$mem" "$workers" "$gc_limit" "$encoding" "$http1_mode" "$load_url" \
    "$copies" "$batch" "$max_body" "$rps" "$lines_per_s" "$batches_ok" "$batches_fail" "$p50" "$p99"

  docker compose down 2>/dev/null || true
  sleep 2
}

# ---------- Http2 (NBomber) ----------
run_h2_test() {
  local base_name="$1" cpu="$2" mem="$3" workers="$4" gc_limit="$5"
  local copies="$6" batch="$7" duration="${8:-60}" encoding="${9:-identity}"

  if ! matches_filter "$encoding" "$ENCODINGS_FILTER"; then
    echo "skip $base_name: encoding=$encoding" >> reports/skipped.txt; return 0; fi
  if ! matches_filter "${cpu%.*}" "$CPU_FILTER"; then
    echo "skip $base_name: cpu=$cpu" >> reports/skipped.txt; return 0; fi
  if ! matches_filter "$batch" "$BATCH_FILTER"; then
    echo "skip $base_name: batch=$batch" >> reports/skipped.txt; return 0; fi
  if [ "$HTTP2_ENABLED" != "true" ]; then
    echo "skip $base_name: HTTP2_ENABLED!=true" >> reports/skipped.txt; return 0; fi

  local name="H2_${base_name}"
  local cpu_int="${cpu%.*}"
  local load_url="http://infrastream_gateway:5006/api/v1/ingest"
  local max_body; if [ "$encoding" = "identity" ]; then max_body="4194304"; else max_body="1048576"; fi

  # Raise HTTP/2 streams for batch=1 (high RPS, more copies than default 100).
  local http2_max_streams
  if [ "$batch" = "1" ]; then http2_max_streams=500; else http2_max_streams=100; fi

  echo ""
  echo -e "${BLUE}==========================================${NC}"
  echo -e "${BLUE}  Test: $name (HTTP/2, NBomber)${NC}"
  echo -e "${BLUE}  CPU: ${cpu}, MEM: ${mem}, Workers: ${workers}, GC: ${gc_limit}${NC}"
  echo -e "${BLUE}  Copies: ${copies}, Batch: ${batch}, Duration: ${duration}s, Encoding: ${encoding}${NC}"
  echo -e "${BLUE}  MaxStreamsPerConnection: ${http2_max_streams}${NC}"
  echo -e "${BLUE}==========================================${NC}"

  docker compose down 2>/dev/null || true

  GATEWAY_CPU="$cpu" GATEWAY_CPU_INT="$cpu_int" GATEWAY_MEMORY="$mem" \
  WORKER_COUNT="$workers" GC_HEAP_HARD_LIMIT="$gc_limit" MAX_BODY_SIZE="$max_body" \
  HTTP1_ENABLED=false HTTP2_ENABLED=true GRPC_ENABLED=false \
  HTTP2_MAX_STREAMS="$http2_max_streams" \
    docker compose up --build -d --force-recreate infrastream_gateway

  if ! wait_healthy "infrastream_gateway" 60; then
    echo -e "${RED}Gateway failed to start. Skipping $name.${NC}"
    docker compose logs infrastream_gateway > "reports/${name}_gateway_failed.log" 2>&1 || true
    docker compose down 2>/dev/null || true; return 1
  fi

  if ! docker exec infrastream_gateway sh -c 'grep -q "138E" /proc/net/tcp'; then
    echo -e "${RED}Gateway not listening on IPv4:5006. Skipping $name.${NC}"
    docker compose down 2>/dev/null || true; return 1
  fi

  docker compose rm -f loadtests > /dev/null 2>&1 || true
  LOAD_URL="$load_url" LOAD_HTTP2=true LOAD_PROTOCOL=http LOAD_COPIES="$copies" \
  LOAD_BATCH_SIZE="$batch" LOAD_DURATION="$duration" LOAD_ENCODINGS="$encoding" \
    docker compose up --build -d --no-deps loadtests

  echo "Running for ${duration}s..."
  docker wait infrastream_loadtests > /dev/null 2>&1 || true
  sleep 3

  docker compose logs loadtests > "reports/${name}_loadtests.log" 2>&1 || true
  docker compose logs infrastream_gateway > "reports/${name}_gateway.log" 2>&1 || true

  local metrics_src; metrics_src=$(resolve_metrics_src "$name")

  local rps lines_per_s batches_ok batches_fail p50 p99
  rps=$(grep "Avg Network RPS" "$metrics_src" | tail -1 | awk '{print $NF}' || echo "N/A")
  lines_per_s=$(grep "Avg Ingress lines/s" "$metrics_src" | tail -1 | awk '{print $NF}' || echo "N/A")
  batches_ok=$(grep "Total Batches OK" "$metrics_src" | tail -1 | awk '{print $NF}' || echo "N/A")
  batches_fail=$(grep "Total Batches FAIL" "$metrics_src" | tail -1 | awk '{print $NF}' || echo "N/A")
  p50=$(grep "p50 latency" "$metrics_src" | tail -1 | awk '{print $NF}' || echo "N/A")
  p99=$(grep "p99 latency" "$metrics_src" | tail -1 | awk '{print $NF}' || echo "N/A")

  collect_worker_metrics "$name"
  print_results "$name" "$encoding" "HTTP/2 (NBomber)" "$load_url" "$rps" "$lines_per_s" "$batches_ok" "$batches_fail" "$p50" "$p99"
  write_summary "$name" "$cpu" "$mem" "$workers" "$gc_limit" "$encoding" "HTTP/2 (NBomber)" "$load_url" \
    "$copies" "$batch" "$max_body" "$rps" "$lines_per_s" "$batches_ok" "$batches_fail" "$p50" "$p99"

  docker compose down 2>/dev/null || true
  sleep 2
}

# ---------- gRPC (NBomber) ----------
run_grpc_test() {
  local base_name="$1" cpu="$2" mem="$3" workers="$4" gc_limit="$5"
  local copies="$6" batch="$7" duration="${8:-60}" encoding="${9:-identity}"

  if ! matches_filter "$encoding" "$ENCODINGS_FILTER"; then
    echo "skip $base_name: encoding=$encoding" >> reports/skipped.txt; return 0; fi
  if ! matches_filter "${cpu%.*}" "$CPU_FILTER"; then
    echo "skip $base_name: cpu=$cpu" >> reports/skipped.txt; return 0; fi
  if ! matches_filter "$batch" "$BATCH_FILTER"; then
    echo "skip $base_name: batch=$batch" >> reports/skipped.txt; return 0; fi
  if [ "$GRPC_ENABLED" != "true" ]; then
    echo "skip $base_name: GRPC_ENABLED!=true" >> reports/skipped.txt; return 0; fi
  if [ "$encoding" != "identity" ]; then
    echo "skip $base_name: gRPC measured with identity (protobuf)" >> reports/skipped.txt; return 0; fi

  local name="GRPC_${base_name}"
  local cpu_int="${cpu%.*}"
  local load_url="http://infrastream_gateway:5007"
  local max_body="4194304"

  # Raise HTTP/2 streams for batch=1 (high RPS, more copies than default 100).
  local grpc_max_streams
  if [ "$batch" = "1" ]; then grpc_max_streams=500; else grpc_max_streams=100; fi

  echo ""
  echo -e "${BLUE}==========================================${NC}"
  echo -e "${BLUE}  Test: $name (gRPC, NBomber, protobuf)${NC}"
  echo -e "${BLUE}  CPU: ${cpu}, MEM: ${mem}, Workers: ${workers}, GC: ${gc_limit}${NC}"
  echo -e "${BLUE}  Copies: ${copies}, Batch: ${batch}, Duration: ${duration}s${NC}"
  echo -e "${BLUE}  MaxStreamsPerConnection: ${grpc_max_streams}${NC}"
  echo -e "${BLUE}==========================================${NC}"

  docker compose down 2>/dev/null || true

  GATEWAY_CPU="$cpu" GATEWAY_CPU_INT="$cpu_int" GATEWAY_MEMORY="$mem" \
  WORKER_COUNT="$workers" GC_HEAP_HARD_LIMIT="$gc_limit" MAX_BODY_SIZE="$max_body" \
  HTTP1_ENABLED=false HTTP2_ENABLED=false GRPC_ENABLED=true GRPC_PORT=5007 \
  GRPC_MAX_STREAMS="$grpc_max_streams" GRPC_MAX_MESSAGE_SIZE=16777216 \
    docker compose up --build -d --force-recreate infrastream_gateway

  if ! wait_healthy "infrastream_gateway" 60; then
    echo -e "${RED}Gateway failed to start. Skipping $name.${NC}"
    docker compose logs infrastream_gateway > "reports/${name}_gateway_failed.log" 2>&1 || true
    docker compose down 2>/dev/null || true; return 1
  fi

  if ! docker exec infrastream_gateway sh -c 'grep -q "138F" /proc/net/tcp'; then
    echo -e "${RED}Gateway not listening on IPv4:5007. Skipping $name.${NC}"
    docker compose down 2>/dev/null || true; return 1
  fi

  docker compose rm -f loadtests > /dev/null 2>&1 || true
  LOAD_URL="$load_url" LOAD_PROTOCOL=grpc LOAD_COPIES="$copies" \
  LOAD_BATCH_SIZE="$batch" LOAD_DURATION="$duration" LOAD_ENCODINGS=identity \
    docker compose up --build -d --no-deps loadtests

  echo "Running for ${duration}s..."
  docker wait infrastream_loadtests > /dev/null 2>&1 || true
  sleep 3

  docker compose logs loadtests > "reports/${name}_loadtests.log" 2>&1 || true
  docker compose logs infrastream_gateway > "reports/${name}_gateway.log" 2>&1 || true

  local metrics_src; metrics_src=$(resolve_metrics_src "$name")

  local rps lines_per_s batches_ok batches_fail p50 p99
  rps=$(grep "Avg Network RPS" "$metrics_src" | tail -1 | awk '{print $NF}' || echo "N/A")
  lines_per_s=$(grep "Avg Ingress lines/s" "$metrics_src" | tail -1 | awk '{print $NF}' || echo "N/A")
  batches_ok=$(grep "Total Batches OK" "$metrics_src" | tail -1 | awk '{print $NF}' || echo "N/A")
  batches_fail=$(grep "Total Batches FAIL" "$metrics_src" | tail -1 | awk '{print $NF}' || echo "N/A")
  p50=$(grep "p50 latency" "$metrics_src" | tail -1 | awk '{print $NF}' || echo "N/A")
  p99=$(grep "p99 latency" "$metrics_src" | tail -1 | awk '{print $NF}' || echo "N/A")

  collect_worker_metrics "$name"
  print_results "$name" "$encoding" "gRPC (NBomber, protobuf)" "$load_url" "$rps" "$lines_per_s" "$batches_ok" "$batches_fail" "$p50" "$p99"
  write_summary "$name" "$cpu" "$mem" "$workers" "$gc_limit" "$encoding" "gRPC (NBomber, protobuf)" "$load_url" \
    "$copies" "$batch" "$max_body" "$rps" "$lines_per_s" "$batches_ok" "$batches_fail" "$p50" "$p99"

  docker compose down 2>/dev/null || true
  sleep 2
}

# ============================================================
#  TESTS
# ============================================================

# GROUP A: 1 CPU, 1 worker — batch=10k
run_test "A1_1cpu_1w_10k_br"        1.0 1G 1 0x30000000 60 10000 60 br       ConnectionHandler
run_test "A2_1cpu_1w_10k_gzip"      1.0 1G 1 0x30000000 60 10000 60 gzip     ConnectionHandler
run_test "A3_1cpu_1w_10k_identity"  1.0 1G 1 0x30000000 60 10000 60 identity ConnectionHandler

# GROUP B: 2 CPU, 2 workers — batch=10k
run_test "B1_2cpu_2w_10k_br"        2.0 1G 2 0x30000000 60 10000 60 br       ConnectionHandler
run_test "B2_2cpu_2w_10k_gzip"      2.0 1G 2 0x30000000 60 10000 60 gzip     ConnectionHandler
run_test "B3_2cpu_2w_10k_identity"  2.0 1G 2 0x30000000 60 10000 60 identity ConnectionHandler

# GROUP C: 4 CPU, 4 workers — batch=10k
run_test "C1_4cpu_4w_10k_br"        4.0 2G 4 0x60000000 120 10000 60 br       ConnectionHandler
run_test "C2_4cpu_4w_10k_gzip"      4.0 2G 4 0x60000000 120 10000 60 gzip     ConnectionHandler
run_test "C3_4cpu_4w_10k_identity"  4.0 2G 4 0x60000000 120 10000 60 identity ConnectionHandler

# GROUP D: batch=1, Brotli
run_test "D1_1cpu_1w_1_br"          1.0 1G 1 0x30000000 60 1 60 br ConnectionHandler
run_test "D2_2cpu_2w_1_br"          2.0 1G 2 0x30000000 60 1 60 br ConnectionHandler
run_test "D3_4cpu_4w_1_br"          4.0 2G 4 0x60000000 120 1 60 br ConnectionHandler

# GROUP E: batch=1, Gzip
run_test "E1_1cpu_1w_1_gzip"        1.0 1G 1 0x30000000 60 1 60 gzip ConnectionHandler
run_test "E2_2cpu_2w_1_gzip"        2.0 1G 2 0x30000000 60 1 60 gzip ConnectionHandler
run_test "E3_4cpu_4w_1_gzip"        4.0 2G 4 0x60000000 120 1 60 gzip ConnectionHandler

# GROUP F: batch=1, Identity
run_test "F1_1cpu_1w_1_identity"    1.0 1G 1 0x30000000 60 1 60 identity ConnectionHandler
run_test "F2_2cpu_2w_1_identity"    2.0 1G 2 0x30000000 60 1 60 identity ConnectionHandler
run_test "F3_4cpu_4w_1_identity"    4.0 2G 4 0x60000000 120 1 60 identity ConnectionHandler

# GROUP G: Http1.Kestrel — batch=10k
run_test "G1_1cpu_1w_10k_br"        1.0 1G 1 0x30000000 60 10000 60 br       Kestrel
run_test "G2_2cpu_2w_10k_br"        2.0 1G 2 0x30000000 60 10000 60 br       Kestrel
run_test "G3_4cpu_4w_10k_br"        4.0 2G 4 0x60000000 120 10000 60 br       Kestrel
run_test "G4_4cpu_4w_10k_gzip"      4.0 2G 4 0x60000000 120 10000 60 gzip     Kestrel
run_test "G5_4cpu_4w_10k_identity"  4.0 2G 4 0x60000000 120 10000 60 identity Kestrel

# GROUP H: HTTP/2 via NBomber — br/gzip/identity × 10k
run_h2_test "H1_1cpu_1w_10k_br"        1.0 1G 1 0x30000000 60 10000 60 br
run_h2_test "H2_1cpu_1w_10k_gzip"      1.0 1G 1 0x30000000 60 10000 60 gzip
run_h2_test "H3_1cpu_1w_10k_identity"  1.0 1G 1 0x30000000 60 10000 60 identity
run_h2_test "H4_2cpu_2w_10k_br"        2.0 1G 2 0x30000000 60 10000 60 br
run_h2_test "H5_2cpu_2w_10k_gzip"      2.0 1G 2 0x30000000 60 10000 60 gzip
run_h2_test "H6_2cpu_2w_10k_identity"  2.0 1G 2 0x30000000 60 10000 60 identity
run_h2_test "H7_4cpu_4w_10k_br"        4.0 2G 4 0x60000000 120 10000 60 br
run_h2_test "H8_4cpu_4w_10k_gzip"      4.0 2G 4 0x60000000 120 10000 60 gzip
run_h2_test "H9_4cpu_4w_10k_identity"  4.0 2G 4 0x60000000 120 10000 60 identity

# GROUP I: HTTP/2 via NBomber — br/gzip/identity × batch=1
run_h2_test "I1_1cpu_1w_1_br"          1.0 1G 1 0x30000000 60 1 60 br
run_h2_test "I2_1cpu_1w_1_gzip"        1.0 1G 1 0x30000000 60 1 60 gzip
run_h2_test "I3_1cpu_1w_1_identity"    1.0 1G 1 0x30000000 60 1 60 identity
run_h2_test "I4_2cpu_2w_1_br"          2.0 1G 2 0x30000000 60 1 60 br
run_h2_test "I5_2cpu_2w_1_gzip"        2.0 1G 2 0x30000000 60 1 60 gzip
run_h2_test "I6_2cpu_2w_1_identity"    2.0 1G 2 0x30000000 60 1 60 identity
run_h2_test "I7_4cpu_4w_1_br"          4.0 2G 4 0x60000000 120 1 60 br
run_h2_test "I8_4cpu_4w_1_gzip"        4.0 2G 4 0x60000000 120 1 60 gzip
run_h2_test "I9_4cpu_4w_1_identity"    4.0 2G 4 0x60000000 120 1 60 identity

# GROUP J: gRPC via NBomber — identity (protobuf) × 10k and batch=1
run_grpc_test "J1_1cpu_1w_10k_identity"  1.0 1G 1 0x30000000 60 10000 60 identity
run_grpc_test "J2_2cpu_2w_10k_identity"  2.0 1G 2 0x30000000 60 10000 60 identity
run_grpc_test "J3_4cpu_4w_10k_identity"  4.0 2G 4 0x60000000 120 10000 60 identity
run_grpc_test "J4_1cpu_1w_1_identity"    1.0 1G 1 0x30000000 60 1 60 identity
run_grpc_test "J5_2cpu_2w_1_identity"    2.0 1G 2 0x30000000 60 1 60 identity
run_grpc_test "J6_4cpu_4w_1_identity"    4.0 2G 4 0x60000000 120 1 60 identity

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
if [ -s reports/skipped.txt ]; then
  echo "Skipped tests: $(wc -l < reports/skipped.txt) (see reports/skipped.txt)"
fi
echo "Reports saved in: $SCRIPT_DIR/reports/"
ls -la reports/ | grep -v "^total"
