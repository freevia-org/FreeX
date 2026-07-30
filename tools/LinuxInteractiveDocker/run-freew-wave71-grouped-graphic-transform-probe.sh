#!/usr/bin/env bash
set -euo pipefail

export DISPLAY="${DISPLAY:-:99}"
output="${1:-/work/freew-wave71-grouped-graphic-transform}"
mkdir -p "$output"
window_id="$(xdotool search --onlyvisible --name 'FreeW' 2>/dev/null | tail -1 || true)"
if [[ -z "$window_id" ]]; then
  printf '%s\n' '{"schemaVersion":1,"suite":"freew-wave71-grouped-graphic-transform","status":"blocked","reason":"No visible FreeW window; fail-closed probe."}' > "$output/probe-results.json"
  exit 77
fi
xdotool windowactivate --sync "$window_id" >/dev/null 2>&1 || true
scrot "$output/01-baseline.png"
printf '%s\n' '{"schemaVersion":1,"suite":"freew-wave71-grouped-graphic-transform","status":"blocked","reason":"Physical grouped chart/SmartArt transform coordinates require an authored fixture; no mutation is inferred."}' > "$output/probe-results.json"
exit 77
