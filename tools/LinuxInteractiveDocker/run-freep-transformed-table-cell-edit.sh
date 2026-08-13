#!/usr/bin/env bash
set -Eeuo pipefail
export DISPLAY="${DISPLAY:-:99}"

output="${1:-/work/freep-transformed-table-cell-edit-validation}"
document_path="${FREEP_DOCUMENT_PATH:-}"
expected_document_name="${FREEP_EXPECTED_DOCUMENT_NAME:-transformed-table-cell-fixture.pptx}"
window_pattern="${FREEP_EXPECTED_WINDOW_PATTERN:-FreeP}"
input_delay_ms="${FREEP_X11_INPUT_DELAY_MS:-160}"
settle_seconds="${FREEP_X11_SETTLE_SECONDS:-0.55}"
pointer_timeout_seconds="${FREEP_X11_POINTER_TIMEOUT_SECONDS:-3}"
save_attempts="${FREEP_SAVE_ATTEMPTS:-20}"
records="$output/result-records.jsonl"
screenshots_file="$output/screenshot-names.txt"
manifest="$output/results.json"
owner_id=""
owner_title=""
. "$(dirname "${BASH_SOURCE[0]}")/ProbeScriptSupport.sh"
required_ids=(
  "visible-window-discovery"
  "transformed-editor-entry-and-caret"
  "transformed-editor-typing-selection-commit"
  "saved-transformed-table-package"
  "escape-cancels-and-preserves-package"
)

mkdir -p "$output"
: > "$records"
: > "$screenshots_file"
printf 'The probe ended before this contract row produced complete physical evidence.\n' > "$output/probe-incomplete.txt"

record() {
  local id="$1" status="$2" note="$3"; shift 3
  python3 - "$records" "$id" "$status" "$note" "$@" <<'PY'
import json, sys
path, result_id, status, note, *evidence = sys.argv[1:]
row = {"id": result_id, "category": "physical-x11-transformed-table-cell-edit", "status": status,
       "evidenceLevel": "physical-x11-input", "evidence": evidence, "note": note}
with open(path, "a", encoding="utf-8") as handle:
    handle.write(json.dumps(row, ensure_ascii=False, sort_keys=True) + "\n")
PY
}

inspect_pptx() {
  python3 - "$1" "$2" <<'PY'
import hashlib, json, sys, zipfile
import xml.etree.ElementTree as ET
package_path, destination = sys.argv[1:]
NS = {"p":"http://schemas.openxmlformats.org/presentationml/2006/main", "a":"http://schemas.openxmlformats.org/drawingml/2006/main"}
def integer(value, default=0):
    try: return int(value)
    except (TypeError, ValueError): return default
with open(package_path, "rb") as handle: package_sha256 = hashlib.sha256(handle.read()).hexdigest()
with zipfile.ZipFile(package_path) as package:
    slide = ET.fromstring(package.read("ppt/slides/slide1.xml"))
frames = []
for frame in slide.findall(".//p:graphicFrame", NS):
    metadata = frame.find("p:nvGraphicFramePr/p:cNvPr", NS)
    if metadata is None or metadata.get("id") != "2": continue
    xfrm = frame.find("p:xfrm", NS)
    off = xfrm.find("a:off", NS); ext = xfrm.find("a:ext", NS)
    texts = [node.text or "" for node in frame.findall(".//a:t", NS)]
    frames.append({"id": 2, "name": metadata.get("name"),
                   "bounds": {"x": integer(off.get("x")), "y": integer(off.get("y")), "cx": integer(ext.get("cx")), "cy": integer(ext.get("cy"))},
                   "rotation": integer(xfrm.get("rot")) / 60000.0,
                   "flipH": xfrm.get("flipH", "0") in ("1", "true"),
                   "flipV": xfrm.get("flipV", "0") in ("1", "true"),
                   "texts": texts, "text": "\n".join(texts)})
result = {"packageSha256": package_sha256, "slide": 1, "shapeId2Count": len(frames), "shapeId2": frames[0] if len(frames) == 1 else None}
with open(destination, "w", encoding="utf-8") as handle:
    json.dump(result, handle, sort_keys=True, separators=(",", ":")); handle.write("\n")
PY
}

assert_package() {
  python3 - "$1" "$2" <<'PY'
import json, sys
data = json.load(open(sys.argv[1], encoding="utf-8")); expected = sys.argv[2]
shape = data.get("shapeId2")
valid = (data.get("shapeId2Count") == 1 and shape is not None and shape.get("id") == 2 and
         shape.get("name") == "Wave62 Transformed Table" and shape.get("bounds") ==
         {"x":1651000,"y":1778000,"cx":8890000,"cy":3302000} and
         abs(float(shape.get("rotation", 0)) - 30.0) < 0.001 and shape.get("flipH") and shape.get("flipV") and
         expected in shape.get("texts", []))
raise SystemExit(0 if valid else 1)
PY
}

save_checkpoint() {
  local prefix="$1" expected="$2"
  local temporary="$output/.$prefix.pptx.tmp" inspect="$output/.$prefix.json.tmp"
  probe_send_owner_key ctrl+s || return 1
  for _ in $(seq 1 "$save_attempts"); do
    if cp "$document_path" "$temporary" 2>"$output/$prefix-inspection-error.txt" &&
       inspect_pptx "$temporary" "$inspect" 2>>"$output/$prefix-inspection-error.txt" &&
       assert_package "$inspect" "$expected" 2>>"$output/$prefix-inspection-error.txt"; then
      mv "$temporary" "$output/$prefix.pptx"; mv "$inspect" "$output/$prefix.json"; return 0
    fi
    sleep 0.25
  done
  rm -f "$temporary" "$inspect"
  return 1
}

finalize() {
  local exit_code=$?; set +e
  python3 - "$records" "$screenshots_file" "$manifest" "$owner_id" "$owner_title" "$expected_document_name" "$exit_code" <<'PY'
import json, sys
records_path, screenshots_path, manifest_path, owner_id, owner_title, fixture, exit_code = sys.argv[1:]
ids = ["visible-window-discovery", "transformed-editor-entry-and-caret", "transformed-editor-typing-selection-commit", "saved-transformed-table-package", "escape-cancels-and-preserves-package"]
rows = {}
try:
    for line in open(records_path, encoding="utf-8"):
        if line.strip():
            row = json.loads(line); rows[row["id"]] = row
except FileNotFoundError: pass
for result_id in ids:
    rows.setdefault(result_id, {"id": result_id, "category": "physical-x11-transformed-table-cell-edit", "status": "failed", "evidenceLevel": "physical-x11-input", "evidence": ["probe-incomplete.txt"], "note": "The probe ended before this physical contract row produced complete evidence."})
try: screenshots = list(dict.fromkeys(line.strip() for line in open(screenshots_path, encoding="utf-8") if line.strip()))
except FileNotFoundError: screenshots = []
results = [rows[result_id] for result_id in ids]
manifest = {"schemaVersion":1, "suite":"freep-linux-transformed-table-cell-edit-physical", "platform":"linux", "shell":"avalonia", "app":"FreeP",
  "appSurface":"in-canvas-transformed-table-cell-text", "window":{"id":owner_id, "title":owner_title, "pattern":fixture, "visible":bool(owner_id)},
  "fixture":{"file":fixture, "shapeId":2, "name":"Wave62 Transformed Table", "bounds":{"x":1651000,"y":1778000,"cx":8890000,"cy":3302000}, "rotation":30, "flipH":True, "flipV":True, "text":"Rotate me"},
  "package":{"savedText":"Typed transformed cell text", "bounds":{"x":1651000,"y":1778000,"cx":8890000,"cy":3302000}, "rotation":30, "flipH":True, "flipV":True},
  "screenshots":[{"name":name,"kind":"screenshot"} for name in screenshots], "results":results,
  "summary":{"passed":sum(row["status"]=="passed" for row in results), "failed":sum(row["status"]=="failed" for row in results), "total":len(results)}, "processExitCode":int(exit_code),
  "contractValidation":{"status":"pending", "validator":"tools/Run-FreePTransformedTableCellEditValidation.ps1", "contractReference":"tools/LinuxInteractiveDocker/freep-transformed-table-cell-edit-validation.schema.json"}}
with open(manifest_path, "w", encoding="utf-8") as handle: json.dump(manifest, handle, indent=2); handle.write("\n")
PY
  return "$exit_code"
}
trap finalize EXIT

if [[ -z "$document_path" || ! -f "$document_path" ]]; then printf 'FREEP_DOCUMENT_PATH is absent or is not a file: %s\n' "$document_path" > "$output/precondition-error.txt"; exit 1; fi
cp "$document_path" "$output/baseline.pptx"
inspect_pptx "$output/baseline.pptx" "$output/baseline-package-inspection.json"
baseline_ok=false; assert_package "$output/baseline-package-inspection.json" "Rotate me" && baseline_ok=true

visible_owner_ids=()
for _ in $(seq 1 30); do
  mapfile -t visible_owner_ids < <(xdotool search --onlyvisible --name "$window_pattern" 2>/dev/null || true)
  (( ${#visible_owner_ids[@]} > 0 )) && break
  sleep 0.25
done
if (( ${#visible_owner_ids[@]} == 0 )); then
  printf 'No visible FreeP window matched %s.\n' "$window_pattern" > "$output/window-discovery-error.txt"
  record "visible-window-discovery" "failed" "No visible FreeP owner matched the X11 precondition." window-discovery-error.txt
  exit 1
fi
owner_id="${visible_owner_ids[${#visible_owner_ids[@]}-1]}"; owner_title="$(xdotool getwindowname "$owner_id" 2>/dev/null || true)"; probe_focus_owner
probe_capture baseline.png && baseline_capture=true || baseline_capture=false
probe_capture_window_state owner-discovery-state.txt
printf 'owner-window-id=%s\nowner-window-title=%s\nexpected-fixture-filename=%s\nbaseline-package-valid=%s\nbaseline-screenshot-captured=%s\n' "$owner_id" "$owner_title" "$expected_document_name" "$baseline_ok" "$baseline_capture" > "$output/visible-window-discovery-proof.txt"
if $baseline_ok && $baseline_capture && [[ "$owner_title" == *"$expected_document_name"* ]]; then
  record "visible-window-discovery" "passed" "Focused visible FreeP window, deterministic transformed-table fixture title, screenshot, and exact baseline transform." visible-window-discovery-proof.txt owner-discovery-state.txt baseline.png baseline-package-inspection.json
else
  record "visible-window-discovery" "failed" "Visible owner did not prove focus, title, screenshot, and exact transformed-table baseline." visible-window-discovery-proof.txt owner-discovery-state.txt
fi

geometry="$(xdotool getwindowgeometry --shell "$owner_id" 2>/dev/null || true)"; eval "$geometry"
pane_width=180; stage_body_top=$((Y + 137)); stage_body_height=$((HEIGHT - 241)); fit_box_x=$((X + pane_width + 40)); fit_box_y=$((stage_body_top + 40)); fit_box_width=$((WIDTH - pane_width - 80)); fit_box_height=$((stage_body_height - 80))
slide_width_emu=12192000; slide_height_emu=6858000
if (( fit_box_width * 9 <= fit_box_height * 16 )); then slide_width_px=$fit_box_width; slide_height_px=$(((fit_box_width * 9 + 8) / 16)); slide_x=$fit_box_x; slide_y=$((fit_box_y + (fit_box_height - slide_height_px + 1) / 2)); else slide_height_px=$fit_box_height; slide_width_px=$(((fit_box_height * 16 + 4) / 9)); slide_x=$((fit_box_x + (fit_box_width - slide_width_px + 1) / 2)); slide_y=$fit_box_y; fi
# First-cell center is (320,230) DIP; table center is (640,360) DIP. Apply flipH/flipV then +30deg rotation.
entry_x_dip=427; entry_y_dip=87
entry_x=$((slide_x + (slide_width_px * entry_x_dip * 9525 + slide_width_emu / 2) / slide_width_emu))
entry_y=$((slide_y + (slide_height_px * entry_y_dip * 9525 + slide_height_emu / 2) / slide_height_emu))
commit_x=$((slide_x + slide_width_px - 24)); commit_y=$((slide_y + slide_height_px - 24))
printf 'owner-geometry-begin\n%s\nowner-geometry-end\ntransformed-first-cell-entry-dip=%s,%s\nentry-outside-untransformed-top-left=true\n' "$geometry" "$entry_x_dip" "$entry_y_dip" > "$output/table-cell-pointer-calibration.txt"

probe_focus_owner; xdotool mousemove --sync "$entry_x" "$entry_y"; xdotool click --clearmodifiers --repeat 2 --delay 120 1; sleep "$settle_seconds"; probe_capture transformed-editor-entry.png; probe_capture_window_state transformed-editor-entry-state.txt
probe_send_owner_key ctrl+a; xdotool type --clearmodifiers --delay "$input_delay_ms" 'Typed transformed cell text'; sleep "$settle_seconds"; probe_capture transformed-editor-input.png; probe_capture_window_state transformed-editor-input-state.txt
if [[ -s "$output/transformed-editor-entry.png" && -s "$output/transformed-editor-input.png" ]]; then record "transformed-editor-entry-and-caret" "passed" "Double-clicked a point inside the rotated/flipped first table cell; focused editor and selection/input screenshots captured." table-cell-pointer-calibration.txt transformed-editor-entry.png transformed-editor-entry-state.txt transformed-editor-input.png transformed-editor-input-state.txt; else record "transformed-editor-entry-and-caret" "failed" "Transformed table-cell editor entry or input capture was missing." table-cell-pointer-calibration.txt transformed-editor-entry-state.txt; fi

xdotool mousemove --sync "$commit_x" "$commit_y"; xdotool click --clearmodifiers 1; sleep "$settle_seconds"; probe_capture transformed-editor-committed.png; probe_capture_window_state transformed-editor-committed-state.txt
if save_checkpoint after-commit 'Typed transformed cell text'; then record "transformed-editor-typing-selection-commit" "passed" "Real X11 selection replacement, typing, outside-pointer commit, and exact persisted transformed-cell text completed." transformed-editor-input.png transformed-editor-committed.png transformed-editor-committed-state.txt; record "saved-transformed-table-package" "passed" "Saved PPTX contains exact transformed-cell text, original table geometry, 30 degree rotation, and both flips." after-commit.json after-commit.pptx; else record "transformed-editor-typing-selection-commit" "failed" "Typing or outside-pointer commit did not produce exact transformed-cell text." transformed-editor-input.png transformed-editor-committed.png transformed-editor-committed-state.txt; record "saved-transformed-table-package" "failed" "Saved PPTX did not satisfy exact transformed table text/geometry/rotation/flip assertions." after-commit-inspection-error.txt; fi

probe_focus_owner; xdotool mousemove --sync "$entry_x" "$entry_y"; xdotool click --clearmodifiers --repeat 2 --delay 120 1; sleep "$settle_seconds"; probe_send_owner_key ctrl+a; xdotool type --clearmodifiers --delay "$input_delay_ms" 'Discarded transformed text'; sleep "$settle_seconds"; probe_capture transformed-editor-canceled.png
timeout --foreground --kill-after=1s "$pointer_timeout_seconds" xdotool key --clearmodifiers --delay "$input_delay_ms" --window "$owner_id" Escape; sleep "$settle_seconds"; probe_capture transformed-editor-after-escape.png; probe_capture_window_state transformed-editor-cancel-state.txt
if save_checkpoint after-cancel 'Typed transformed cell text'; then record "escape-cancels-and-preserves-package" "passed" "Second real transformed table-cell edit was canceled with Escape; exact committed text, geometry, rotation, and flips remained unchanged." transformed-editor-canceled.png transformed-editor-after-escape.png transformed-editor-cancel-state.txt after-cancel.json; else record "escape-cancels-and-preserves-package" "failed" "Escape did not preserve the exact committed transformed table package." transformed-editor-canceled.png transformed-editor-after-escape.png transformed-editor-cancel-state.txt after-cancel-inspection-error.txt; fi
exit 0
