#!/usr/bin/env bash
set -Eeuo pipefail
export DISPLAY="${DISPLAY:-:99}"

output="${1:-/work/freex-textbox-inline-edit-physical}"
document_path="${FREEX_TEXTBOX_DOCUMENT:-/documents/freex-wave93-textbox-fixture.xlsx}"
result_path="/work/freex-textbox-inline-physical.json"
runtime_path="/work/freex-textbox-inline-physical.json"
input_delay_ms="${FREEX_X11_INPUT_DELAY_MS:-120}"
type_delay_ms="${FREEX_X11_TYPE_DELAY_MS:-65}"
settle_seconds="${FREEX_X11_SETTLE_SECONDS:-0.45}"
window_id=""
window_x=0
window_y=0
window_width=0
window_height=0
a1_x=0
a1_y=0
cell_width=0
cell_height=0
declare -a results=()
declare -a screenshot_names=()
declare -a screenshot_phases=()
manifest_written=false

mkdir -p "$output"
: > "$output/result-rows.jsonl"

json_escape() {
    local value="$1"
    value="${value//\\/\\\\}"
    value="${value//\"/\\\"}"
    value="${value//$'\n'/\\n}"
    printf '%s' "$value"
}

record() {
    local id="$1" status="$2" note="$3" evidence="$4"
    local row="{\"id\":\"$(json_escape "$id")\",\"status\":\"$status\",\"evidenceLevel\":\"physical-x11-input\",\"evidence\":[\"$(json_escape "$evidence")\"],\"note\":\"$(json_escape "$note")\"}"
    results+=("$row")
    printf '%s\n' "$row" >> "$output/result-rows.jsonl"
}

capture() {
    local phase="$1" name="$2" track="${3:-true}"
    scrot -o "$output/$name" >/dev/null 2>&1
    [[ -s "$output/$name" ]] || return 1
    identify -format '%w %h' "$output/$name" > "$output/$name.dimensions"
    if [[ "$track" == "true" ]]; then
        screenshot_names+=("$name")
        screenshot_phases+=("$phase")
    fi
}

focus_owner() {
    xdotool windowactivate --sync "$window_id" >/dev/null 2>&1 || true
    xdotool windowfocus "$window_id" >/dev/null 2>&1 || true
    sleep 0.12
}

send_key() {
    focus_owner
    xdotool key --clearmodifiers --delay "$input_delay_ms" --window "$window_id" "$@"
    sleep "$settle_seconds"
}

type_text() {
    focus_owner
    xdotool type --clearmodifiers --delay "$type_delay_ms" --window "$window_id" "$1"
    sleep "$settle_seconds"
}

selection_box() {
    local screenshot="$1" components box
    components="$(convert "$screenshot" -alpha off -fill black +opaque '#217346' -fill white -opaque '#217346' -define connected-components:verbose=true -connected-components 8 null: 2>&1)"
    box="$(printf '%s\n' "$components" | awk '/srgb\(255,255,255\)/ && $4 + 0 > largest { largest = $4 + 0; box = $2 } END { print box }')"
    [[ "$box" =~ ^([0-9]+)x([0-9]+)\+([0-9]+)\+([0-9]+)$ ]] || return 1
    observed_width="${BASH_REMATCH[1]}"
    observed_height="${BASH_REMATCH[2]}"
    observed_x="${BASH_REMATCH[3]}"
    observed_y="${BASH_REMATCH[4]}"
    (( observed_width >= 20 && observed_width <= 500 && observed_height >= 12 && observed_height <= 120 ))
}

calibrate_grid() {
    wmctrl -ir "$window_id" -b add,maximized_vert,maximized_horz >/dev/null 2>&1 || true
    eval "$(xdotool getwindowgeometry --shell "$window_id")"
    window_x="$X"; window_y="$Y"; window_width="$WIDTH"; window_height="$HEIGHT"
    focus_owner
    send_key Escape
    xdotool mousemove --window "$window_id" "$((window_width - 160))" "$((window_height - 160))" click 1
    send_key ctrl+Home
    for _ in $(seq 1 20); do
        capture calibration calibration-a1.png false 2>/dev/null || true
        if selection_box "$output/calibration-a1.png"; then
            a1_x="$observed_x"; a1_y="$observed_y"; a1_width="$observed_width"; a1_height="$observed_height"; break
        fi
        sleep 0.12
    done
    (( a1_x > 0 && a1_y > 0 )) || return 1
    send_key Right
    for _ in $(seq 1 20); do
        capture calibration calibration-b1.png false 2>/dev/null || true
        if selection_box "$output/calibration-b1.png" && (( observed_x > a1_x + 20 && observed_x < a1_x + 240 )); then
            cell_width=$((observed_x - a1_x)); break
        fi
        sleep 0.12
    done
    send_key ctrl+Home
    send_key Down
    for _ in $(seq 1 20); do
        capture calibration calibration-a2.png false 2>/dev/null || true
        if selection_box "$output/calibration-a2.png" && (( observed_y > a1_y + 10 && observed_y < a1_y + 120 )); then
            cell_height=$((observed_y - a1_y)); break
        fi
        sleep 0.12
    done
    (( cell_width >= 24 && cell_height >= 14 )) || return 1
    send_key ctrl+Home
}

wait_for_runtime() {
    local expression="$1"
    for _ in $(seq 1 40); do
        if [[ -s "$runtime_path" ]] && python3 - "$runtime_path" "$expression" <<'PY'
import json, sys
path, expression = sys.argv[1:]
data = json.load(open(path, encoding="utf-8"))
events = data.get("events", [])
if expression == "entry":
    ok = any(e.get("phase") == "editing" and e.get("editorVisible") and e.get("editorFocused") and
             e.get("nonZeroBounds") and e.get("editorAutomationId") == "TextBoxInlineEditor" and
             float(e.get("editorWidth", 0)) > 0 and float(e.get("editorHeight", 0)) > 0 for e in events)
elif expression == "multiline":
    ok = any(e.get("phase") == "editing" and e.get("editorVisible") and e.get("editorFocused") and
             e.get("editorText") == "Wave93 committed\nsecond line" and
             e.get("modelText") == "Wave93 initial text" for e in events)
elif expression == "commit":
    ok = any(e.get("phase") == "committed" and not e.get("editorVisible") and
             e.get("editorText") == "Wave93 committed\nsecond line" and
             e.get("modelText") == "Wave93 committed\nsecond line" for e in events)
elif expression == "reopen":
    committed_indexes = [i for i, e in enumerate(events) if e.get("phase") == "committed"]
    last_commit = committed_indexes[-1] if committed_indexes else -1
    ok = any(i > last_commit and e.get("phase") == "editing" and e.get("editorVisible") and
             e.get("editorFocused") and e.get("editorText") == "Wave93 committed\nsecond line" and
             e.get("modelText") == "Wave93 committed\nsecond line"
             for i, e in enumerate(events))
elif expression == "cancel-input":
    ok = any(e.get("phase") == "editing" and e.get("editorVisible") and e.get("editorFocused") and
             e.get("editorText") == "Wave93 canceled" and
             e.get("modelText") == "Wave93 committed\nsecond line" for e in events)
elif expression == "cancel":
    ok = any(e.get("phase") == "canceled" and not e.get("editorVisible") and
             e.get("editorText") == "Wave93 committed\nsecond line" and
             e.get("modelText") == "Wave93 committed\nsecond line" for e in events)
else:
    raise SystemExit(2)
raise SystemExit(0 if ok else 1)
PY
        then return 0; fi
        sleep 0.2
    done
    return 1
}

read_textbox_text() {
    python3 - "$document_path" <<'PY'
import sys, zipfile, xml.etree.ElementTree as ET
path = sys.argv[1]
ns = {'xdr':'http://schemas.openxmlformats.org/spreadsheetml/2006/main', 'a':'http://schemas.openxmlformats.org/drawingml/2006/main'}
ns = {'xdr':'http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing', 'a':'http://schemas.openxmlformats.org/drawingml/2006/main'}
with zipfile.ZipFile(path) as package:
    root = ET.fromstring(package.read('xl/drawings/drawing1.xml'))
for shape in root.findall('.//xdr:sp', ns):
    c_nv = shape.find('./xdr:nvSpPr/xdr:cNvSpPr', ns)
    if c_nv is not None and c_nv.get('txBox') == '1':
        value = []
        for node in shape.find('./xdr:txBody', ns).iter():
            if node.tag == '{http://schemas.openxmlformats.org/drawingml/2006/main}t':
                value.append(node.text or '')
            elif node.tag == '{http://schemas.openxmlformats.org/drawingml/2006/main}br':
                value.append('\n')
        print(''.join(value))
        break
else:
    raise SystemExit('No txBox=1 drawing object found')
PY
}

write_manifest() {
    local passed=0 failed=0
    for row in "${results[@]}"; do
        if [[ "$row" == *'"status":"passed"'* ]]; then passed=$((passed + 1)); else failed=$((failed + 1)); fi
    done
    printf '%s\n' "${screenshot_names[*]}" > "$output/screenshot-names.txt"
    printf '%s\n' "${screenshot_phases[*]}" > "$output/screenshot-phases.txt"
    python3 - "$output" "$result_path" "$window_id" "$window_width" "$window_height" "$passed" "$failed" <<'PY'
import json, os, sys
output, runtime_path, window_id, window_width, window_height, passed, failed = sys.argv[1:]
names = open(os.path.join(output, 'screenshot-names.txt'), encoding='utf-8').read().split()
phases = open(os.path.join(output, 'screenshot-phases.txt'), encoding='utf-8').read().split()
with open(os.path.join(output, 'result-rows.jsonl'), encoding='utf-8') as handle:
    normalized = [json.loads(line) for line in handle if line.strip()]
screenshots = []
for name, phase in zip(names, phases):
    with open(os.path.join(output, name + '.dimensions'), encoding='utf-8') as handle:
        width, height = (int(part) for part in handle.read().split())
    screenshots.append({'name': name, 'phase': phase, 'width': width, 'height': height})
runtime = json.load(open(runtime_path, encoding='utf-8')) if os.path.exists(runtime_path) else {'schemaVersion':1,'suite':'freex-linux-textbox-inline-edit-physical','events':[]}
def read_optional(name):
    path = os.path.join(output, name)
    if not os.path.exists(path):
        return ''
    with open(path, encoding='utf-8') as handle:
        return handle.read().rstrip('\n')
fixture = {
    'packageText': read_optional('package-fixture.txt'),
    'provenance': 'xlsx-package-readback-before-interaction'
}
manifest = {
    'schemaVersion': 1, 'suite': 'freex-linux-textbox-inline-edit-physical',
    'platform': 'linux', 'shell': 'avalonia', 'app': 'FreeX',
    'window': {'pattern':'FreeX', 'visible': True, 'id':window_id, 'width':int(window_width), 'height':int(window_height)},
    'screenshots': screenshots, 'fixture': fixture, 'runtime': runtime,
    'results': normalized, 'summary': {'passed':int(passed), 'failed':int(failed), 'total':int(passed)+int(failed)}
}
with open(os.path.join(output, 'results.json'), 'w', encoding='utf-8') as handle:
    json.dump(manifest, handle, ensure_ascii=False, separators=(',', ':'))
    handle.write('\n')
PY
    manifest_written=true
}

on_error() {
    local exit_code=$?
    trap - ERR
    xdotool mouseup 1 >/dev/null 2>&1 || true
    if ! $manifest_written; then
        record "probe-runtime" "failed" "The physical probe aborted before all required rows completed." "probe-incomplete.txt"
        printf '%s\n' "The physical probe aborted with exit $exit_code." > "$output/probe-incomplete.txt"
        write_manifest || true
    fi
    exit "$exit_code"
}
trap on_error ERR

while read -r candidate; do window_id="$candidate"; done < <(xdotool search --onlyvisible --name '^.+ - FreeX$' 2>/dev/null || true)
if [[ -z "$window_id" ]]; then
    record "visible-window-discovery" "failed" "No visible FreeX window matched the production title pattern." "probe-incomplete.txt"
    printf '%s\n' 'No visible FreeX window.' > "$output/probe-incomplete.txt"
    write_manifest
    exit 2
fi
eval "$(xdotool getwindowgeometry --shell "$window_id")"
window_x="$X"; window_y="$Y"; window_width="$WIDTH"; window_height="$HEIGHT"
record "visible-window-discovery" "passed" "The foreground FreeX Avalonia window was discovered through X11." "before.png"
capture before before.png
calibrate_grid

fixture_text="$(read_textbox_text 2>/dev/null || true)"
printf '%s\n' "$fixture_text" > "$output/package-fixture.txt"
if [[ "$fixture_text" == 'Wave93 initial text' ]]; then
    record "textbox-fixture-readback" "passed" "The opened production workbook contains the deterministic txBox=1 fixture text." "package-fixture.txt"
else
    record "textbox-fixture-readback" "failed" "The opened workbook did not expose the deterministic txBox=1 fixture text." "package-fixture.txt"
fi

object_x=$((a1_x + cell_width))
object_y=$((a1_y + cell_height * 4))
focus_owner
xdotool mousemove --sync "$object_x" "$object_y"
xdotool click --clearmodifiers --repeat 2 --delay 180 1
sleep "$settle_seconds"
capture editing editing.png
if wait_for_runtime entry; then
    record "textbox-editor-entry" "passed" "A physical double-click opened and focused the production TextBoxInlineEditor with nonzero bounds." "editing.png"
else
    record "textbox-editor-entry" "failed" "The physical double-click did not produce a focused, nonzero-bounds TextBoxInlineEditor observation." "editing.png;freex-textbox-inline-physical.json"
fi

send_key ctrl+a
type_text 'Wave93 committed'
send_key ctrl+Return
type_text 'second line'
capture editing editing-multiline.png
if wait_for_runtime multiline; then
    record "textbox-editor-multiline" "passed" "Modified Enter inserted a newline while the real inline editor remained visible and focused." "editing.png;editing-multiline.png"
else
    record "textbox-editor-multiline" "failed" "Modified Enter did not produce a multiline live editor observation." "editing.png;editing-multiline.png;freex-textbox-inline-physical.json"
fi

send_key Tab
if wait_for_runtime commit; then
    capture committed committed.png
    record "textbox-editor-commit" "passed" "Tab committed the real editor; the opt-in observer reported the editor hidden and the live drawing TextBox model contains the exact authored multiline text." "committed.png;freex-textbox-inline-physical.json"
else
    capture committed committed.png || true
    record "textbox-editor-commit" "failed" "Tab did not prove editor disappearance plus the exact committed live model text." "committed.png;freex-textbox-inline-physical.json"
fi

focus_owner
xdotool mousemove --sync "$object_x" "$object_y"
xdotool click --clearmodifiers --repeat 2 --delay 180 1
sleep "$settle_seconds"
reopen_observed=false
if wait_for_runtime reopen; then reopen_observed=true; fi
send_key ctrl+a
type_text 'Wave93 canceled'
cancel_input_observed=false
if wait_for_runtime cancel-input; then cancel_input_observed=true; fi
send_key Escape
capture canceled canceled.png
if $reopen_observed && $cancel_input_observed && wait_for_runtime cancel; then
    record "textbox-editor-cancel" "passed" "A second physical double-click reopened the editor, the cancellation value was observed live, and Escape hid the editor while restoring the exact committed model text." "canceled.png;freex-textbox-inline-physical.json"
else
    record "textbox-editor-cancel" "failed" "The second physical edit did not prove reopen, cancellation input, and exact live-model rollback after Escape." "canceled.png;freex-textbox-inline-physical.json"
fi

write_manifest
passed_count=0
failed_count=0
for row in "${results[@]}"; do
    if [[ "$row" == *'"status":"passed"'* ]]; then passed_count=$((passed_count + 1)); else failed_count=$((failed_count + 1)); fi
done
if [[ "$passed_count" -ne 6 || "$failed_count" -ne 0 ]]; then exit 3; fi
