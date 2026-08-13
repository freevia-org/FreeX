#!/usr/bin/env bash
set -euo pipefail

executable=""
readiness_root=""
report_path=""
log_path=""
timeout_seconds=30
readiness_marker='"eventName":"app_ready"'
app_arguments=()

while (($#)); do
  case "$1" in
    --executable)
      executable="${2:-}"
      shift 2
      ;;
    --readiness-root)
      readiness_root="${2:-}"
      shift 2
      ;;
    --report)
      report_path="${2:-}"
      shift 2
      ;;
    --log)
      log_path="${2:-}"
      shift 2
      ;;
    --timeout-seconds)
      timeout_seconds="${2:-}"
      shift 2
      ;;
    --readiness-marker)
      readiness_marker="${2:-}"
      shift 2
      ;;
    --)
      shift
      app_arguments=("$@")
      break
      ;;
    *)
      echo "Unknown packaged-product launch probe argument: $1" >&2
      exit 2
      ;;
  esac
done

if [[ -z "$executable" || -z "$readiness_root" || -z "$report_path" ]]; then
  echo "Usage: $0 --executable <path> --readiness-root <directory> --report <path> [--log <path>] [--timeout-seconds <seconds>] [-- <app arguments...>]" >&2
  exit 2
fi
if [[ ! -x "$executable" ]]; then
  echo "Packaged product executable is missing or not executable: $executable" >&2
  exit 1
fi
if [[ ! "$timeout_seconds" =~ ^[1-9][0-9]*$ ]]; then
  echo "Packaged product launch timeout must be a positive integer: $timeout_seconds" >&2
  exit 2
fi

mkdir -p "$readiness_root" "$(dirname "$report_path")"
if [[ -z "$log_path" ]]; then
  log_path="${TMPDIR:-/tmp}/packaged-product-launch-$$.log"
fi
mkdir -p "$(dirname "$log_path")"
rm -f "$report_path" "$log_path"

probe_pid=""
process_is_active() {
  local pid="$1"
  local state
  state="$(ps -o stat= -p "$pid" 2>/dev/null | tr -d ' ')"
  [[ -n "$state" && "$state" != Z* ]]
}

stop_probe() {
  if [[ -z "$probe_pid" ]]; then
    return
  fi

  if process_is_active "$probe_pid"; then
    kill "$probe_pid" 2>/dev/null || true
    local elapsed=0
    while ((elapsed < 5)) && process_is_active "$probe_pid"; do
      sleep 1
      elapsed=$((elapsed + 1))
    done
    if process_is_active "$probe_pid"; then
      kill -9 "$probe_pid" 2>/dev/null || true
    fi
  fi
  wait "$probe_pid" 2>/dev/null || true
  probe_pid=""
}
trap stop_probe EXIT INT TERM

"$executable" "${app_arguments[@]}" >"$log_path" 2>&1 &
probe_pid=$!

elapsed=0
ready=false
while ((elapsed < timeout_seconds)); do
  if grep -R -F -q "$readiness_marker" "$readiness_root" 2>/dev/null; then
    ready=true
    break
  fi
  if ! process_is_active "$probe_pid"; then
    set +e
    wait "$probe_pid"
    process_status=$?
    set -e
    probe_pid=""
    cat "$log_path" >&2 || true
    echo "Packaged product exited before readiness (status $process_status): $executable" >&2
    exit 1
  fi
  sleep 1
  elapsed=$((elapsed + 1))
done

if [[ "$ready" != "true" ]]; then
  cat "$log_path" >&2 || true
  echo "Packaged product did not emit readiness within ${timeout_seconds}s: $executable" >&2
  exit 1
fi
if ! process_is_active "$probe_pid"; then
  cat "$log_path" >&2 || true
  echo "Packaged product was not active after emitting readiness: $executable" >&2
  exit 1
fi

{
  echo "packaged_product_launch_status=passed"
  echo "packaged_product_executable=$executable"
  echo "packaged_product_ready_marker=app_ready"
  echo "packaged_product_launch_timeout_seconds=$timeout_seconds"
} > "$report_path"

cat "$report_path"
stop_probe
trap - EXIT INT TERM
