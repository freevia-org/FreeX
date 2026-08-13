#!/usr/bin/env bash
set -Eeuo pipefail
export DISPLAY="${DISPLAY:-:99}"

output="${1:-/work/freep-rotated-shape-text-edit-validation}"
document_path="${FREEP_DOCUMENT_PATH:-}"
expected_document_name="${FREEP_EXPECTED_DOCUMENT_NAME:-rotated-shape-text-fixture.pptx}"
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
    "rotated-editor-entry-and-caret"
    "rotated-editor-typing-selection-commit"
    "saved-rotated-shape-package"
    "escape-cancels-and-preserves-package"
)

mkdir -p "$output"
: > "$records"
: > "$screenshots_file"
printf 'The probe ended before this contract row produced complete physical evidence.\n' > "$output/probe-incomplete.txt"

record() {
    local id="$1" status="$2" note="$3"
    shift 3
    python3 - "$records" "$id" "$status" "$note" "$@" <<'PY'
import json
import sys
path, result_id, status, note, *evidence = sys.argv[1:]
row = {"id": result_id, "category": "physical-x11-rotated-shape-text", "status": status,
       "evidenceLevel": "physical-x11-input", "evidence": evidence, "note": note}
with open(path, "a", encoding="utf-8") as handle:
    handle.write(json.dumps(row, ensure_ascii=False, sort_keys=True) + "\n")
PY
}

hash_file() { sha256sum "$1" | awk '{print tolower($1)}'; }
active_owner_now() {
    [[ "$(xdotool getactivewindow 2>/dev/null || true)" == "$owner_id" &&
       "$(xdotool getwindowfocus 2>/dev/null || true)" == "$owner_id" ]]
}
inspect_pptx() {
    python3 - "$1" "$2" <<'PY'
import hashlib, json, sys, zipfile
import xml.etree.ElementTree as ET
package_path, destination = sys.argv[1:]
NS = {"p":"http://schemas.openxmlformats.org/presentationml/2006/main", "a":"http://schemas.openxmlformats.org/drawingml/2006/main"}
def bounds(shape):
    xfrm = shape.find("p:spPr/a:xfrm", NS)
    off = xfrm.find("a:off", NS) if xfrm is not None else None
    ext = xfrm.find("a:ext", NS) if xfrm is not None else None
    return {"x": int(off.get("x")), "y": int(off.get("y")), "cx": int(ext.get("cx")), "cy": int(ext.get("cy"))}
with open(package_path, "rb") as handle: package_sha256 = hashlib.sha256(handle.read()).hexdigest()
with zipfile.ZipFile(package_path) as package: slide = ET.fromstring(package.read("ppt/slides/slide1.xml"))
matches = []
for shape in slide.findall(".//p:sp", NS):
    metadata = shape.find("p:nvSpPr/p:cNvPr", NS)
    if metadata is not None and metadata.get("id") == "2":
        xfrm = shape.find("p:spPr/a:xfrm", NS)
        text = "".join((node.text or "") for node in shape.findall(".//a:t", NS))
        matches.append({"id": 2, "name": metadata.get("name"), "bounds": bounds(shape),
                        "rotation": (int(xfrm.get("rot", "0")) / 60000.0) if xfrm is not None else 0.0,
                        "text": text})
result = {"packageSha256": package_sha256, "slide": 1, "shapeId2Count": len(matches), "shapeId2": matches[0] if len(matches) == 1 else None}
with open(destination, "w", encoding="utf-8") as handle: json.dump(result, handle, sort_keys=True, separators=(",", ":")); handle.write("\n")
PY
}
assert_package() {
    python3 - "$1" "$2" <<'PY'
import json, sys
data = json.load(open(sys.argv[1], encoding="utf-8")); expected = sys.argv[2]
shape = data.get("shapeId2")
valid = (data.get("shapeId2Count") == 1 and shape is not None and shape.get("id") == 2 and
         shape.get("name") == "Wave61 Rotated Text" and shape.get("bounds") ==
         {"x":2857500,"y":1428750,"cx":2286000,"cy":1524000} and
         abs(float(shape.get("rotation", 0)) - 30.0) < 0.001 and shape.get("text") == expected)
raise SystemExit(0 if valid else 1)
PY
}
save_checkpoint() {
    local prefix="$1" expected="$2"
    local temporary="$output/.$prefix.pptx.tmp" inspect="$output/.$prefix.json.tmp"
    probe_send_owner_key ctrl+s || return 1
    for _ in $(seq 1 "$save_attempts"); do
        if cp "$document_path" "$temporary" 2>"$output/$prefix-inspection-error.txt" && inspect_pptx "$temporary" "$inspect" 2>>"$output/$prefix-inspection-error.txt" && assert_package "$inspect" "$expected" 2>>"$output/$prefix-inspection-error.txt"; then
            mv "$temporary" "$output/$prefix.pptx"; mv "$inspect" "$output/$prefix.json"; hash_file "$output/$prefix.pptx" > "$output/$prefix.sha256.txt"; return 0
        fi
        sleep 0.25
    done
    rm -f "$temporary" "$inspect"
    return 1
}
finalize() {
    local exit_code=$?
    set +e
    python3 - "$records" "$screenshots_file" "$manifest" "$owner_id" "$owner_title" "$expected_document_name" "$exit_code" <<'PY'
import json, sys
records_path, screenshots_path, manifest_path, owner_id, owner_title, fixture, exit_code = sys.argv[1:]
ids = ["visible-window-discovery", "rotated-editor-entry-and-caret", "rotated-editor-typing-selection-commit", "saved-rotated-shape-package", "escape-cancels-and-preserves-package"]
rows = {}
try:
    for line in open(records_path, encoding="utf-8"):
        if line.strip(): rows[json.loads(line)["id"]] = json.loads(line)
except FileNotFoundError: pass
for result_id in ids:
    rows.setdefault(result_id, {"id": result_id, "category":"physical-x11-rotated-shape-text", "status":"failed", "evidenceLevel":"physical-x11-input", "evidence":["probe-incomplete.txt"], "note":"The probe ended before this physical contract row produced complete evidence."})
try: screenshots = list(dict.fromkeys(line.strip() for line in open(screenshots_path, encoding="utf-8") if line.strip()))
except FileNotFoundError: screenshots = []
results = [rows[result_id] for result_id in ids]
manifest = {"schemaVersion":1, "suite":"freep-linux-rotated-shape-text-edit-physical", "platform":"linux", "shell":"avalonia", "app":"FreeP", "baseline":False,
  "appSurface":"in-canvas-rotated-shape-text", "window":{"id":owner_id, "title":owner_title, "pattern":fixture, "visible":bool(owner_id)},
  "fixture":{"file":fixture, "shapeId":2, "name":"Wave61 Rotated Text", "bounds":{"x":2857500,"y":1428750,"cx":2286000,"cy":1524000}, "rotation":30, "text":"Rotate me"},
  "package":{"savedText":"Typed rotated text", "bounds":{"x":2857500,"y":1428750,"cx":2286000,"cy":1524000}, "rotation":30},
  "screenshots":[{"name":name,"kind":"screenshot"} for name in screenshots], "results":results,
  "summary":{"passed":sum(row["status"]=="passed" for row in results), "failed":sum(row["status"]=="failed" for row in results), "total":len(results)}, "processExitCode":int(exit_code),
  "contractValidation":{"status":"pending", "validator":"tools/Run-FreePRotatedShapeTextEditValidation.ps1", "contractReference":"tools/LinuxInteractiveDocker/freep-rotated-shape-text-edit-validation.schema.json"}}
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
    if (( ${#visible_owner_ids[@]} == 0 )); then
        mapfile -t visible_owner_ids < <(xdotool search --onlyvisible --name "$expected_document_name" 2>/dev/null || true)
    fi
    (( ${#visible_owner_ids[@]} > 0 )) && break
    sleep 0.25
done
if (( ${#visible_owner_ids[@]} == 0 )); then
    printf 'No visible FreeP window matched %s.\n' "$window_pattern" > "$output/window-discovery-error.txt"
    record "visible-window-discovery" "failed" "No visible FreeP owner matched the X11 precondition." window-discovery-error.txt
    exit 1
fi
owner_id="${visible_owner_ids[${#visible_owner_ids[@]}-1]}"; owner_title="$(xdotool getwindowname "$owner_id" 2>/dev/null || true)"; probe_focus_owner
owner_focused=false; active_owner_now && owner_focused=true
probe_capture baseline.png && baseline_capture=true || baseline_capture=false
probe_capture_window_state owner-discovery-state.txt
printf 'owner-window-id=%s\nowner-window-title=%s\nexpected-fixture-filename=%s\nowner-focused=%s\nbaseline-package-valid=%s\nbaseline-screenshot-captured=%s\n' "$owner_id" "$owner_title" "$expected_document_name" "$owner_focused" "$baseline_ok" "$baseline_capture" > "$output/visible-window-discovery-proof.txt"
if $owner_focused && $baseline_ok && $baseline_capture && [[ "$owner_title" == *"$expected_document_name"* ]]; then
    record "visible-window-discovery" "passed" "Focused visible FreeP window, deterministic fixture title, screenshot, and exact rotated package baseline." visible-window-discovery-proof.txt owner-discovery-state.txt baseline.png baseline-package-inspection.json
else
    record "visible-window-discovery" "failed" "Visible owner did not prove focus, title, screenshot, and exact rotated package baseline." visible-window-discovery-proof.txt owner-discovery-state.txt
fi

geometry="$(xdotool getwindowgeometry --shell "$owner_id" 2>/dev/null || true)"; eval "$geometry"
pane_width=180; stage_body_top=$((Y + 137)); stage_body_height=$((HEIGHT - 241)); fit_box_x=$((X + pane_width + 40)); fit_box_y=$((stage_body_top + 40)); fit_box_width=$((WIDTH - pane_width - 80)); fit_box_height=$((stage_body_height - 80))
slide_width_emu=12192000; slide_height_emu=6858000; shape_entry_x_dip=290; shape_entry_y_dip=236
if (( fit_box_width * 9 <= fit_box_height * 16 )); then slide_width_px=$fit_box_width; slide_height_px=$(((fit_box_width * 9 + 8) / 16)); slide_x=$fit_box_x; slide_y=$((fit_box_y + (fit_box_height - slide_height_px + 1) / 2)); fit_constraint=width; else slide_height_px=$fit_box_height; slide_width_px=$(((fit_box_height * 16 + 4) / 9)); slide_x=$((fit_box_x + (fit_box_width - slide_width_px + 1) / 2)); slide_y=$fit_box_y; fit_constraint=height; fi
entry_x=$((slide_x + (slide_width_px * shape_entry_x_dip * 9525 + slide_width_emu / 2) / slide_width_emu))
entry_y=$((slide_y + (slide_height_px * shape_entry_y_dip * 9525 + slide_height_emu / 2) / slide_height_emu))
# The fixture point is inside rotated shape ID 2 after inverse rotation, outside its
# unrotated left edge, and outside the overlapping orange shape ID 3.
commit_x=$((slide_x + slide_width_px - 24)); commit_y=$((slide_y + slide_height_px - 24))
printf 'owner-geometry-begin\n%s\nowner-geometry-end\nfit-constraint=%s\nderived-slide-rect=%s,%s,%s,%s\nrotated-entry-point=%s,%s\nentry-outside-unrotated-aabb=true\n' "$geometry" "$fit_constraint" "$slide_x" "$slide_y" "$slide_width_px" "$slide_height_px" "$entry_x" "$entry_y" > "$output/shape-pointer-calibration.txt"

probe_focus_owner; xdotool mousemove --sync "$entry_x" "$entry_y"; xdotool click --clearmodifiers --repeat 2 --delay 120 1; sleep "$settle_seconds"; probe_capture rotated-editor-entry.png; probe_capture_window_state rotated-editor-entry-state.txt
probe_send_owner_key ctrl+a; xdotool type --clearmodifiers --delay "$input_delay_ms" 'Typed rotated text'; sleep "$settle_seconds"; probe_capture rotated-editor-input.png; probe_capture_window_state rotated-editor-input-state.txt
if [[ -s "$output/rotated-editor-entry.png" && -s "$output/rotated-editor-input.png" ]]; then record "rotated-editor-entry-and-caret" "passed" "Double-clicked a point inside the rotated shape but outside its unrotated AABB; focused editor state and input screenshot captured." shape-pointer-calibration.txt rotated-editor-entry.png rotated-editor-entry-state.txt rotated-editor-input.png rotated-editor-input-state.txt; else record "rotated-editor-entry-and-caret" "failed" "Rotated editor entry or caret/input capture was missing." shape-pointer-calibration.txt rotated-editor-entry-state.txt; fi

xdotool mousemove --sync "$commit_x" "$commit_y"; xdotool click --clearmodifiers 1; sleep "$settle_seconds"; probe_capture rotated-editor-committed.png; probe_capture_window_state rotated-editor-committed-state.txt
if save_checkpoint after-commit 'Typed rotated text'; then record "rotated-editor-typing-selection-commit" "passed" "Real X11 selection replacement, typing, outside-pointer commit, and editor-state capture completed." rotated-editor-input.png rotated-editor-committed.png rotated-editor-committed-state.txt; record "saved-rotated-shape-package" "passed" "Saved PPTX contains exact edited text, original geometry, and 30 degree rotation." after-commit.json after-commit.pptx after-commit.sha256.txt; else record "rotated-editor-typing-selection-commit" "failed" "Typing or outside-pointer commit did not produce the exact saved text." rotated-editor-input.png rotated-editor-committed.png rotated-editor-committed-state.txt; record "saved-rotated-shape-package" "failed" "Saved PPTX did not satisfy exact text, geometry, and rotation assertions." after-commit-inspection-error.txt; fi

probe_focus_owner; xdotool mousemove --sync "$entry_x" "$entry_y"; xdotool click --clearmodifiers --repeat 2 --delay 120 1; sleep "$settle_seconds"; probe_send_owner_key ctrl+a; xdotool type --clearmodifiers --delay "$input_delay_ms" 'Discarded'; sleep "$settle_seconds"; probe_capture rotated-editor-canceled.png
# Do not refocus the top-level window here: changing focus can commit the live editor before
# Escape reaches its native input control.
timeout --foreground --kill-after=1s "$pointer_timeout_seconds" xdotool key --clearmodifiers --delay "$input_delay_ms" --window "$owner_id" Escape; sleep "$settle_seconds"; probe_capture rotated-editor-after-escape.png; probe_capture_window_state rotated-editor-cancel-state.txt
if save_checkpoint after-cancel 'Typed rotated text'; then record "escape-cancels-and-preserves-package" "passed" "Second real pointer/keyboard edit was canceled with Escape; exact committed package text, geometry, and rotation remained unchanged." rotated-editor-canceled.png rotated-editor-after-escape.png rotated-editor-cancel-state.txt after-cancel.json; else record "escape-cancels-and-preserves-package" "failed" "Escape did not preserve the exact committed package." rotated-editor-canceled.png rotated-editor-after-escape.png rotated-editor-cancel-state.txt after-cancel-inspection-error.txt; fi
exit 0
