#!/usr/bin/env bash
set -Eeuo pipefail

inspect_pptx() {
    local package_path="$1" destination="$2"
    python3 - "$package_path" "$destination" <<'PY'
import hashlib
import json
import sys
import zipfile
import xml.etree.ElementTree as ET

package_path, destination = sys.argv[1:]
NS = {
    "p": "http://schemas.openxmlformats.org/presentationml/2006/main",
    "a": "http://schemas.openxmlformats.org/drawingml/2006/main",
}

def shape_text(shape):
    return "".join(node.text or "" for node in shape.iter("{" + NS["a"] + "}t"))

def shape_bounds(shape):
    xfrm = shape.find("p:spPr/a:xfrm", NS)
    if xfrm is None:
        return {"x": None, "y": None, "cx": None, "cy": None}
    off = xfrm.find("a:off", NS)
    ext = xfrm.find("a:ext", NS)
    return {
        "x": int(off.get("x")) if off is not None else None,
        "y": int(off.get("y")) if off is not None else None,
        "cx": int(ext.get("cx")) if ext is not None else None,
        "cy": int(ext.get("cy")) if ext is not None else None,
    }

with open(package_path, "rb") as handle:
    package_sha256 = hashlib.sha256(handle.read()).hexdigest()

with zipfile.ZipFile(package_path) as package:
    slide = ET.fromstring(package.read("ppt/slides/slide1.xml"))

editable_shapes = []
for shape in slide.findall(".//p:sp", NS):
    metadata = shape.find("p:nvSpPr/p:cNvPr", NS)
    editable_shapes.append({
        "id": int(metadata.get("id")) if metadata is not None else None,
        "name": metadata.get("name") if metadata is not None else None,
        "text": shape_text(shape),
        "bounds": shape_bounds(shape),
    })

result = {
    "packageSha256": package_sha256,
    "slide": 1,
    "editableShapes": editable_shapes,
    "pPicCount": len(slide.findall(".//p:pic", NS)),
    "pGraphicFrameCount": len(slide.findall(".//p:graphicFrame", NS)),
}
with open(destination, "w", encoding="utf-8", newline="\n") as handle:
    json.dump(result, handle, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    handle.write("\n")
PY
}

assert_shapes() {
    local inspection="$1" expected_text="$2" expected_ids="$3" expected_bounds="$4"
    python3 - "$inspection" "$expected_text" "$expected_ids" "$expected_bounds" <<'PY'
import json
import sys

inspection, expected_text, id_spec, bounds_spec = sys.argv[1:]
data = json.load(open(inspection, encoding="utf-8"))
shapes = data["editableShapes"]
expected_ids = [int(value) for value in id_spec.split(",") if value]
expected_bounds = [
    tuple(map(int, item.split(",")))
    for item in bounds_spec.split(";")
    if item
]
actual_bounds = [
    (shape["bounds"]["x"], shape["bounds"]["y"], shape["bounds"]["cx"], shape["bounds"]["cy"])
    for shape in shapes
]
valid = (
    [shape["id"] for shape in shapes] == expected_ids
    and [shape["name"] for shape in shapes] == ["Notes marker"] * len(expected_ids)
    and [shape["text"] for shape in shapes] == [expected_text] * len(expected_ids)
    and actual_bounds == expected_bounds
    and data["pPicCount"] == 0
    and data["pGraphicFrameCount"] == 0
)
if not valid:
    raise SystemExit(1)
PY
}

assert_baseline_inspection() {
    assert_shapes "$1" "$2" "2" "914400,914400,2743200,914400"
}

assert_duplicate_inspection() {
    assert_shapes "$1" "$2" "2,3" \
        "914400,914400,2743200,914400;1097280,1097280,2743200,914400"
}

assert_empty_user_shape_checkpoint() {
    assert_shapes "$1" "$2" "" ""
}

assert_restored_copies() {
    assert_duplicate_inspection "$@"
}

assert_cut_paste_inspection() {
    assert_shapes "$1" "$2" "1,2" \
        "1097280,1097280,2743200,914400;1280160,1280160,2743200,914400"
}

self_test_inspect_pptx() {
    local inspection="$1"
    python3 - "$inspection" <<'PY'
import json
import sys

data = json.load(open(sys.argv[1], encoding="utf-8"))
required = {"packageSha256", "slide", "editableShapes", "pPicCount", "pGraphicFrameCount"}
if set(data) != required or data["slide"] != 1:
    raise SystemExit("invalid inspect_pptx top-level schema")
if len(data["packageSha256"]) != 64 or any(c not in "0123456789abcdef" for c in data["packageSha256"]):
    raise SystemExit("invalid inspect_pptx package SHA256")
for shape in data["editableShapes"]:
    if set(shape) != {"id", "name", "text", "bounds"}:
        raise SystemExit("invalid editable shape record")
    if set(shape["bounds"]) != {"x", "y", "cx", "cy"}:
        raise SystemExit("invalid editable shape bounds")
PY
}

if [[ "${1:-}" == "--inspect-pptx" ]]; then
    [[ $# == 3 ]] || {
        printf 'usage: %s --inspect-pptx PACKAGE OUTPUT_JSON\n' "$0" >&2
        exit 2
    }
    inspect_pptx "$2" "$3"
    self_test_inspect_pptx "$3"
    exit 0
fi

export DISPLAY="${DISPLAY:-:99}"

output="${1:-/work/fps}"
document_path="${FREEP_DOCUMENT_PATH:-}"
expected_document_name="${FREEP_EXPECTED_DOCUMENT_NAME:-$(basename "${document_path:-presentation.pptx}")}"
window_pattern="${FREEP_EXPECTED_WINDOW_PATTERN:-FreeP}"
input_delay_ms="${FREEP_X11_INPUT_DELAY_MS:-160}"
settle_seconds="${FREEP_X11_SETTLE_SECONDS:-0.45}"
pointer_timeout_seconds="${FREEP_X11_POINTER_TIMEOUT_SECONDS:-3}"
clipboard_timeout_seconds="${FREEP_X11_CLIPBOARD_TIMEOUT_SECONDS:-3}"
save_attempts="${FREEP_SAVE_ATTEMPTS:-16}"
screen_width="${FREEP_SCREEN_WIDTH:-1280}"
screen_height="${FREEP_SCREEN_HEIGHT:-820}"
screen_dpi="${FREEP_SCREEN_DPI:-96}"
expected_text="Slide 1 has speaker notes"
native_clipboard_format="freex.freep.selection.v1"
records="$output/result-records.jsonl"
screenshots_file="$output/screenshot-names.txt"
manifest="$output/results.json"
required_ids=(
    "visible-window-discovery"
    "clipboard-copy-x11-preserves-source"
    "clipboard-paste-native-editable-shape"
    "select-all-multi-shape-mutation"
    "cut-all-x11-undoable"
    "undo-restores-editable-shapes"
    "redo-reapplies-cut"
    "paste-after-cut-restores-editable-shapes"
)

mkdir -p "$output"
: > "$records"
: > "$screenshots_file"
printf 'The probe ended before this contract row produced complete physical evidence.\n' \
    > "$output/probe-incomplete.txt"

record() {
    local id="$1" status="$2" note="$3"
    shift 3
    python3 - "$records" "$id" "$status" "$note" "$@" <<'PY'
import json
import sys

path, result_id, status, note, *evidence = sys.argv[1:]
row = {
    "id": result_id,
    "category": "physical-x11-clipboard-shortcut",
    "status": status,
    "evidenceLevel": "physical-x11-input",
    "evidence": evidence,
    "note": note,
}
with open(path, "a", encoding="utf-8") as handle:
    handle.write(json.dumps(row, ensure_ascii=False, sort_keys=True) + "\n")
PY
}

hash_file() {
    sha256sum "$1" | awk '{print tolower($1)}'
}

track_screenshot() {
    printf '%s\n' "$1" >> "$screenshots_file"
}

capture() {
    local name="$1"
    command -v scrot >/dev/null 2>&1 || return 1
    scrot -o "$output/$name" >/dev/null 2>&1 || return 1
    [[ -s "$output/$name" ]] || return 1
    track_screenshot "$name"
}

window_ids() {
    local hex_id
    while read -r hex_id _; do
        [[ "$hex_id" =~ ^0x[0-9A-Fa-f]+$ ]] || continue
        printf '%d\n' "$hex_id"
    done < <(wmctrl -l 2>/dev/null || true)
}

focus_owner() {
    timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
        xdotool windowactivate --sync "$owner_id" >/dev/null 2>&1 || true
    timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
        xdotool windowfocus "$owner_id" >/dev/null 2>&1 || true
    sleep 0.12
}

active_owner_now() {
    [[ "$(xdotool getactivewindow 2>/dev/null || true)" == "$owner_id" &&
       "$(xdotool getwindowfocus 2>/dev/null || true)" == "$owner_id" ]]
}

send_owner_key() {
    focus_owner
    timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
        xdotool key --clearmodifiers --delay "$input_delay_ms" "$@"
    sleep "$settle_seconds"
}

capture_window_state() {
    local name="$1"
    {
        printf 'owner-window-id=%s\n' "${owner_id:-}"
        printf 'owner-window-title=%s\n' "${owner_title:-}"
        printf 'active-window=%s\n' "$(xdotool getactivewindow 2>/dev/null || true)"
        printf 'focus-window=%s\n' "$(xdotool getwindowfocus 2>/dev/null || true)"
        printf 'top-level-window-ids='
        window_ids | tr '\n' ' '
        printf '\nwmctrl-list-begin\n'
        wmctrl -l 2>/dev/null || true
        printf 'wmctrl-list-end\n'
    } > "$output/$name"
}

capture_clipboard() {
    local prefix="$1" expected="$2" require_exact_text="$3"
    local target_file="$output/$prefix-targets.txt"
    local native_file="$output/$prefix-native.bin"
    local text_file="$output/$prefix-text.txt"
    local error_file="$output/$prefix-error.txt"
    local native_target="" clipboard_text=""

    for ((attempt = 1; attempt <= 20; attempt++)); do
        : > "$error_file"
        if timeout --foreground --kill-after=1s "$clipboard_timeout_seconds" \
            xclip -selection clipboard -t TARGETS -o > "$target_file" 2>> "$error_file"; then
            native_target="$(
                tr -d '\r' < "$target_file" |
                    grep -F "$native_clipboard_format" |
                    head -n 1 || true
            )"
            if [[ -n "$native_target" ]] &&
               timeout --foreground --kill-after=1s "$clipboard_timeout_seconds" \
                   xclip -selection clipboard -t "$native_target" -o \
                   > "$native_file" 2>> "$error_file" &&
               timeout --foreground --kill-after=1s "$clipboard_timeout_seconds" \
                   xclip -selection clipboard -o > "$text_file" 2>> "$error_file"; then
                clipboard_text="$(tr -d '\r' < "$text_file")"
                if [[ -s "$native_file" && -n "$clipboard_text" ]] &&
                   { [[ "$require_exact_text" != true ]] || [[ "$clipboard_text" == "$expected" ]]; }; then
                    hash_file "$native_file" > "$output/$prefix-native.sha256.txt"
                    return 0
                fi
            fi
        fi
        sleep 0.15
    done
    return 1
}

save_checkpoint() {
    local prefix="$1" predicate="$2"
    shift 2
    local temporary_pptx="$output/.$prefix.pptx.tmp"
    local temporary_json="$output/.$prefix.json.tmp"
    local error_file="$output/$prefix-inspection-error.txt"

    send_owner_key ctrl+s || return 1
    for ((attempt = 1; attempt <= save_attempts; attempt++)); do
        if cp "$document_path" "$temporary_pptx" 2> "$error_file" &&
           inspect_pptx "$temporary_pptx" "$temporary_json" 2>> "$error_file" &&
           "$predicate" "$temporary_json" "$@"; then
            mv "$temporary_pptx" "$output/$prefix.pptx"
            mv "$temporary_json" "$output/$prefix.json"
            hash_file "$output/$prefix.pptx" > "$output/$prefix.sha256.txt"
            return 0
        fi
        sleep 0.2
    done
    rm -f "$temporary_pptx" "$temporary_json"
    return 1
}

finalize() {
    local exit_code=$?
    set +e
    if [[ -n "$document_path" && -f "$document_path" ]]; then
        hash_file "$document_path" > "$output/fixture-mounted-after.sha256.txt"
    fi
    python3 - "$records" "$screenshots_file" "$manifest" \
        "${owner_id:-}" "${owner_title:-}" "$expected_document_name" "$exit_code" <<'PY'
import json
import os
import sys

records_path, screenshots_path, manifest_path, owner_id, owner_title, fixture, exit_code = sys.argv[1:]
ids = [
    "visible-window-discovery",
    "clipboard-copy-x11-preserves-source",
    "clipboard-paste-native-editable-shape",
    "select-all-multi-shape-mutation",
    "cut-all-x11-undoable",
    "undo-restores-editable-shapes",
    "redo-reapplies-cut",
    "paste-after-cut-restores-editable-shapes",
]
by_id = {}
try:
    with open(records_path, encoding="utf-8") as handle:
        for line in handle:
            if line.strip():
                row = json.loads(line)
                by_id[row["id"]] = row
except (FileNotFoundError, json.JSONDecodeError):
    pass
for result_id in ids:
    by_id.setdefault(result_id, {
        "id": result_id,
        "category": "physical-x11-clipboard-shortcut",
        "status": "failed",
        "evidenceLevel": "physical-x11-input",
        "evidence": ["probe-incomplete.txt"],
        "note": "The probe ended before this physical contract row produced complete evidence.",
    })
results = [by_id[result_id] for result_id in ids]
try:
    with open(screenshots_path, encoding="utf-8") as handle:
        screenshots = list(dict.fromkeys(line.strip() for line in handle if line.strip()))
except FileNotFoundError:
    screenshots = []
manifest = {
    "schemaVersion": 1,
    "suite": "freep-linux-clipboard-shortcut-physical",
    "platform": "linux",
    "shell": "avalonia",
    "app": "FreeP",
    "baseline": False,
    "appSurface": "document-editor-clipboard-shortcuts",
    "window": {
        "id": owner_id,
        "title": owner_title,
        "pattern": fixture,
        "visible": bool(owner_id),
    },
    "parameters": {
        "width": int(os.environ.get("FREEP_SCREEN_WIDTH", "1280")),
        "height": int(os.environ.get("FREEP_SCREEN_HEIGHT", "820")),
        "dpi": int(os.environ.get("FREEP_SCREEN_DPI", "96")),
        "fixture": fixture,
    },
    "coverage": {
        "scope": "physical FreeP clipboard shortcut evidence lane",
        "exhaustive": False,
    },
    "contractValidation": {
        "status": "pending",
        "validator": "tools/Run-FreePClipboardShortcutValidation.ps1",
        "contractReference": "tools/LinuxInteractiveDocker/freep-clipboard-shortcut-validation.schema.json",
    },
    "screenshots": [{"name": name, "kind": "screenshot"} for name in screenshots],
    "summary": {
        "passed": sum(row["status"] == "passed" for row in results),
        "failed": sum(row["status"] == "failed" for row in results),
        "total": len(results),
    },
    "results": results,
    "processExitCode": int(exit_code),
}
with open(manifest_path, "w", encoding="utf-8") as handle:
    json.dump(manifest, handle, ensure_ascii=False, indent=2)
    handle.write("\n")
PY
    return "$exit_code"
}
trap finalize EXIT

lane_failed=false

if [[ -z "$document_path" || ! -f "$document_path" ]]; then
    printf 'FREEP_DOCUMENT_PATH is absent or is not a file: %s\n' "$document_path" \
        > "$output/precondition-error.txt"
    exit 1
fi

initial_hash="$(hash_file "$document_path")"
printf '%s\n' "$initial_hash" > "$output/fixture-mounted-before.sha256.txt"
cp "$document_path" "$output/baseline.pptx"
inspect_pptx "$output/baseline.pptx" "$output/baseline-package-inspection.json"
self_test_inspect_pptx "$output/baseline-package-inspection.json"
baseline_valid=false
if assert_baseline_inspection "$output/baseline-package-inspection.json" "$expected_text"; then
    baseline_valid=true
fi

mapfile -t visible_owner_ids < <(
    xdotool search --onlyvisible --name "$window_pattern" 2>/dev/null || true
)
if (( ${#visible_owner_ids[@]} == 0 )); then
    printf 'No visible FreeP window matched %s.\n' "$window_pattern" \
        > "$output/window-discovery-error.txt"
    record "visible-window-discovery" "failed" \
        "No visible FreeP owner matched the X11 precondition." \
        window-discovery-error.txt
    exit 1
fi

owner_id="${visible_owner_ids[${#visible_owner_ids[@]}-1]}"
owner_title="$(xdotool getwindowname "$owner_id" 2>/dev/null || true)"
focus_owner
owner_focused=false
active_owner_now && owner_focused=true
baseline_capture=false
capture "baseline.png" && baseline_capture=true || true
capture_window_state "owner-discovery-state.txt"
{
    printf 'owner-window-id=%s\n' "$owner_id"
    printf 'owner-window-title=%s\n' "$owner_title"
    printf 'expected-window-pattern=%s\n' "$window_pattern"
    printf 'expected-fixture-filename=%s\n' "$expected_document_name"
    printf 'fixture-filename-in-title='
    [[ "$owner_title" == *"$expected_document_name"* ]] && printf 'true\n' || printf 'false\n'
    printf 'freep-in-title='
    [[ "$owner_title" == *FreeP* || "$owner_title" == *Freep* ]] &&
        printf 'true\n' || printf 'false\n'
    printf 'owner-focused=%s\n' "$owner_focused"
    printf 'baseline-package-valid=%s\n' "$baseline_valid"
    printf 'baseline-screenshot-captured=%s\n' "$baseline_capture"
    printf 'visible-owner-count=%s\n' "${#visible_owner_ids[@]}"
} > "$output/visible-window-discovery-proof.txt"

visible_pass=false
if $owner_focused && $baseline_valid && $baseline_capture &&
   [[ "$owner_title" == *"$expected_document_name"* ]] &&
   [[ "$owner_title" == *FreeP* || "$owner_title" == *Freep* ]]; then
    visible_pass=true
    record "visible-window-discovery" "passed" \
        "Visible focused FreeP owner, fixture title, baseline screenshot, and exact baseline package semantics were discovered." \
        visible-window-discovery-proof.txt owner-discovery-state.txt baseline.png \
        baseline.pptx baseline-package-inspection.json fixture-mounted-before.sha256.txt
else
    lane_failed=true
    record "visible-window-discovery" "failed" \
        "The visible owner did not prove focus, both required title tokens, a real screenshot, and exact baseline package semantics." \
        visible-window-discovery-proof.txt owner-discovery-state.txt
fi

geometry="$(xdotool getwindowgeometry --shell "$owner_id" 2>/dev/null || true)"
eval "$geometry"
shape_center_x=$((X + 180 + (WIDTH - 180) * 36 / 100))
shape_center_y=$((Y + HEIGHT * 38 / 100))
{
    printf 'calibration=known FreeP 1280x820 shell layout scaled from owner geometry\n'
    printf 'owner-geometry-begin\n%s\nowner-geometry-end\n' "$geometry"
    printf 'fixed-slide-pane-width=180\n'
    printf 'fixed-canvas-margin=40\n'
    printf 'fixed-notes-pane-height=60\n'
    printf 'fixture-slide-aspect=4:3\n'
    printf 'shape-bounds-emu=914400,914400,2743200,914400\n'
    printf 'shape-center-point=%s,%s\n' "$shape_center_x" "$shape_center_y"
    printf 'selection-credit=clipboard native payload and exact package transitions only\n'
} > "$output/shape-pointer-calibration.txt"

selection_before_capture=false
capture "shape-selection-before.png" && selection_before_capture=true || true
focus_owner
xdotool mousemove --sync "$shape_center_x" "$shape_center_y" >/dev/null 2>&1 || true
xdotool click --clearmodifiers 1 >/dev/null 2>&1 || true
sleep "$settle_seconds"
focus_owner
selection_after_capture=false
capture "shape-selection-after.png" && selection_after_capture=true || true
capture_window_state "shape-selection-state.txt"

copy_key_sent=true
send_owner_key ctrl+c || copy_key_sent=false
copy_clipboard_ready=false
if capture_clipboard "clipboard-copy" "$expected_text" true; then
    copy_clipboard_ready=true
fi
copy_capture=false
capture "clipboard-copy-after.png" && copy_capture=true || true
capture_window_state "clipboard-copy-state.txt"
copy_checkpoint=false
if save_checkpoint "after-copy" assert_baseline_inspection "$expected_text"; then
    copy_checkpoint=true
fi
after_copy_hash=""
[[ -s "$output/after-copy.sha256.txt" ]] &&
    after_copy_hash="$(tr -d '\r\n' < "$output/after-copy.sha256.txt")"
copy_source_unchanged=false
[[ "$after_copy_hash" == "$initial_hash" ]] && copy_source_unchanged=true
{
    printf 'shortcut=ctrl+c\n'
    printf 'copy-key-sent=%s\n' "$copy_key_sent"
    printf 'native-format=%s\n' "$native_clipboard_format"
    printf 'native-clipboard-ready=%s\n' "$copy_clipboard_ready"
    printf 'exact-text=%s\n' "$expected_text"
    printf 'ctrl-s-baseline-checkpoint=%s\n' "$copy_checkpoint"
    printf 'baseline-package-sha256=%s\n' "$initial_hash"
    printf 'after-copy-package-sha256=%s\n' "$after_copy_hash"
    printf 'source-package-unchanged=%s\n' "$copy_source_unchanged"
    printf 'selection-before-screenshot=%s\n' "$selection_before_capture"
    printf 'selection-after-screenshot=%s\n' "$selection_after_capture"
    printf 'copy-after-screenshot=%s\n' "$copy_capture"
} > "$output/clipboard-copy-proof.txt"
copy_pass=false
if $visible_pass && $copy_key_sent && $copy_clipboard_ready && $copy_checkpoint &&
   $copy_source_unchanged && $selection_before_capture && $selection_after_capture &&
   $copy_capture; then
    copy_pass=true
    record "clipboard-copy-x11-preserves-source" "passed" \
        "Pointer-selected shape Ctrl+C exposed the exact native X11 target and text, while Ctrl+S preserved the exact baseline package." \
        clipboard-copy-proof.txt shape-pointer-calibration.txt shape-selection-state.txt \
        clipboard-copy-state.txt clipboard-copy-targets.txt clipboard-copy-native.bin \
        clipboard-copy-native.sha256.txt clipboard-copy-text.txt after-copy.pptx \
        after-copy.json after-copy.sha256.txt
else
    lane_failed=true
    record "clipboard-copy-x11-preserves-source" "failed" \
        "Ctrl+C did not prove native X11 shape data, exact text, real captures, and unchanged baseline package semantics." \
        clipboard-copy-proof.txt shape-pointer-calibration.txt shape-selection-state.txt \
        clipboard-copy-state.txt
fi

paste_key_sent=true
send_owner_key ctrl+v || paste_key_sent=false
paste_capture=false
capture "clipboard-paste-after.png" && paste_capture=true || true
capture_window_state "clipboard-paste-state.txt"
paste_checkpoint=false
if save_checkpoint "after-copy-paste" assert_duplicate_inspection "$expected_text"; then
    paste_checkpoint=true
fi
{
    printf 'shortcut=ctrl+v\n'
    printf 'paste-key-sent=%s\n' "$paste_key_sent"
    printf 'native-copy-prerequisite=%s\n' "$copy_clipboard_ready"
    printf 'after-copy-paste-checkpoint=%s\n' "$paste_checkpoint"
    printf 'expected-ids=2,3\n'
    printf 'expected-bounds=914400,914400,2743200,914400;1097280,1097280,2743200,914400\n'
    printf 'paste-offset-emu=182880\n'
    printf 'paste-after-screenshot=%s\n' "$paste_capture"
} > "$output/clipboard-paste-proof.txt"
paste_pass=false
if $copy_pass && $paste_key_sent && $paste_checkpoint && $paste_capture; then
    paste_pass=true
    record "clipboard-paste-native-editable-shape" "passed" \
        "Ctrl+V consumed the native clipboard selection and saved two editable Notes marker shapes with exact IDs, text, bounds, and no pictures or frames." \
        clipboard-paste-proof.txt clipboard-paste-state.txt after-copy.pptx \
        after-copy.json after-copy-paste.pptx after-copy-paste.json \
        after-copy-paste.sha256.txt
else
    lane_failed=true
    record "clipboard-paste-native-editable-shape" "failed" \
        "Ctrl+V did not produce the exact two-shape editable package checkpoint from native clipboard data." \
        clipboard-paste-proof.txt clipboard-paste-state.txt
fi

select_all_key_sent=true
send_owner_key ctrl+a || select_all_key_sent=false
select_all_capture=false
capture "select-all-after.png" && select_all_capture=true || true
capture_window_state "select-all-state.txt"

cut_key_sent=true
send_owner_key ctrl+x || cut_key_sent=false
cut_clipboard_ready=false
if capture_clipboard "clipboard-cut" "$expected_text" false; then
    cut_clipboard_ready=true
fi
cut_capture=false
capture "cut-all-after.png" && cut_capture=true || true
capture_window_state "cut-all-state.txt"
cut_checkpoint=false
if save_checkpoint "after-cut" assert_empty_user_shape_checkpoint "$expected_text"; then
    cut_checkpoint=true
fi

undo_key_sent=true
send_owner_key ctrl+z || undo_key_sent=false
undo_capture=false
capture "undo-after.png" && undo_capture=true || true
capture_window_state "undo-state.txt"
undo_checkpoint=false
if save_checkpoint "after-undo" assert_restored_copies "$expected_text"; then
    undo_checkpoint=true
fi

{
    printf 'shortcut=ctrl+a\n'
    printf 'select-all-key-sent=%s\n' "$select_all_key_sent"
    printf 'precondition-two-editable-shapes=%s\n' "$paste_checkpoint"
    printf 'following-cut-removed-both=%s\n' "$cut_checkpoint"
    printf 'single-undo-restored-both=%s\n' "$undo_checkpoint"
    printf 'select-all-screenshot=%s\n' "$select_all_capture"
} > "$output/select-all-proof.txt"
select_all_pass=false
if $paste_pass && $select_all_key_sent && $cut_checkpoint && $undo_checkpoint &&
   $select_all_capture; then
    select_all_pass=true
    record "select-all-multi-shape-mutation" "passed" \
        "Ctrl+A selection was indirectly proven when the following single Ctrl+X removed both exact editable shapes and one Ctrl+Z restored both." \
        select-all-proof.txt select-all-state.txt after-copy-paste.pptx \
        after-copy-paste.json after-cut.pptx after-cut.json after-undo.pptx \
        after-undo.json
else
    lane_failed=true
    record "select-all-multi-shape-mutation" "failed" \
        "Ctrl+A was not proven by the required following two-shape removal and single-undo restoration." \
        select-all-proof.txt select-all-state.txt
fi

{
    printf 'shortcut=ctrl+x\n'
    printf 'cut-key-sent=%s\n' "$cut_key_sent"
    printf 'native-format=%s\n' "$native_clipboard_format"
    printf 'native-cut-clipboard-ready=%s\n' "$cut_clipboard_ready"
    printf 'before-checkpoint=after-copy-paste.pptx\n'
    printf 'after-checkpoint=after-cut.pptx\n'
    printf 'empty-package-checkpoint=%s\n' "$cut_checkpoint"
    printf 'one-undo-restored-two-shape-checkpoint=%s\n' "$undo_checkpoint"
    printf 'undoable-mutation-count=1\n'
    printf 'cut-after-screenshot=%s\n' "$cut_capture"
} > "$output/cut-all-proof.txt"
cut_pass=false
if $select_all_pass && $cut_key_sent && $cut_clipboard_ready && $cut_checkpoint &&
   $undo_checkpoint && $cut_capture; then
    cut_pass=true
    record "cut-all-x11-undoable" "passed" \
        "Ctrl+X exposed nonempty native X11 clipboard data, removed both shapes in one saved mutation, and one Ctrl+Z restored the exact pre-cut package semantics." \
        cut-all-proof.txt cut-all-state.txt clipboard-cut-targets.txt \
        clipboard-cut-native.bin clipboard-cut-native.sha256.txt \
        clipboard-cut-text.txt after-copy-paste.pptx after-copy-paste.json \
        after-copy-paste.sha256.txt after-cut.pptx after-cut.json \
        after-cut.sha256.txt after-undo.pptx after-undo.json
else
    lane_failed=true
    record "cut-all-x11-undoable" "failed" \
        "Ctrl+X did not prove a nonempty native clipboard, exact empty checkpoint, and one-step undo restoration." \
        cut-all-proof.txt cut-all-state.txt
fi

{
    printf 'shortcut=ctrl+z\n'
    printf 'undo-key-sent=%s\n' "$undo_key_sent"
    printf 'before-checkpoint=after-cut.pptx\n'
    printf 'restored-checkpoint=after-undo.pptx\n'
    printf 'expected-ids=2,3\n'
    printf 'restored-exact-editable-copies=%s\n' "$undo_checkpoint"
    printf 'undo-after-screenshot=%s\n' "$undo_capture"
} > "$output/undo-proof.txt"
undo_pass=false
if $cut_checkpoint && $undo_key_sent && $undo_checkpoint && $undo_capture; then
    undo_pass=true
    record "undo-restores-editable-shapes" "passed" \
        "Ctrl+Z restored both editable shapes with exact IDs, names, text, pre-cut bounds, and zero pictures or frames." \
        undo-proof.txt undo-state.txt after-cut.pptx after-cut.json \
        after-undo.pptx after-undo.json after-undo.sha256.txt
else
    lane_failed=true
    record "undo-restores-editable-shapes" "failed" \
        "Ctrl+Z did not restore the exact two editable pre-cut shapes." \
        undo-proof.txt undo-state.txt
fi

redo_key_sent=true
send_owner_key ctrl+shift+z || redo_key_sent=false
redo_capture=false
capture "redo-after.png" && redo_capture=true || true
capture_window_state "redo-state.txt"
redo_checkpoint=false
if save_checkpoint "after-redo" assert_empty_user_shape_checkpoint "$expected_text"; then
    redo_checkpoint=true
fi
{
    printf 'shortcut=ctrl+shift+z\n'
    printf 'redo-key-sent=%s\n' "$redo_key_sent"
    printf 'before-checkpoint=after-undo.pptx\n'
    printf 'empty-package-checkpoint=%s\n' "$redo_checkpoint"
    printf 'redo-after-screenshot=%s\n' "$redo_capture"
} > "$output/redo-proof.txt"
redo_pass=false
if $undo_pass && $redo_key_sent && $redo_checkpoint && $redo_capture; then
    redo_pass=true
    record "redo-reapplies-cut" "passed" \
        "Ctrl+Shift+Z reapplied the cut and saved an exact zero-user-shape package with no pictures or frames." \
        redo-proof.txt redo-state.txt after-undo.pptx after-undo.json \
        after-redo.pptx after-redo.json after-redo.sha256.txt
else
    lane_failed=true
    record "redo-reapplies-cut" "failed" \
        "Ctrl+Shift+Z did not reapply the exact empty-shape cut checkpoint." \
        redo-proof.txt redo-state.txt
fi

cut_paste_key_sent=true
send_owner_key ctrl+v || cut_paste_key_sent=false
cut_paste_capture=false
capture "paste-after-cut.png" && cut_paste_capture=true || true
capture_window_state "paste-after-cut-state.txt"
cut_paste_checkpoint=false
if save_checkpoint "after-cut-paste" assert_cut_paste_inspection "$expected_text"; then
    cut_paste_checkpoint=true
fi
{
    printf 'shortcut=ctrl+v\n'
    printf 'paste-key-sent=%s\n' "$cut_paste_key_sent"
    printf 'cut-native-clipboard-prerequisite=%s\n' "$cut_clipboard_ready"
    printf 'after-redo-empty-prerequisite=%s\n' "$redo_checkpoint"
    printf 'restored-cut-copies=%s\n' "$cut_paste_checkpoint"
    printf 'expected-ids=1,2\n'
    printf 'expected-bounds=1097280,1097280,2743200,914400;1280160,1280160,2743200,914400\n'
    printf 'paste-after-cut-screenshot=%s\n' "$cut_paste_capture"
} > "$output/paste-after-cut-proof.txt"
cut_paste_pass=false
if $redo_pass && $cut_clipboard_ready && $cut_paste_key_sent &&
   $cut_paste_checkpoint && $cut_paste_capture; then
    cut_paste_pass=true
    record "paste-after-cut-restores-editable-shapes" "passed" \
        "Ctrl+V after the redone cut restored two editable native shapes with fresh IDs 1/2, exact successive offsets, and zero pictures or frames." \
        paste-after-cut-proof.txt paste-after-cut-state.txt \
        clipboard-cut-targets.txt clipboard-cut-native.bin \
        clipboard-cut-native.sha256.txt after-redo.pptx after-redo.json \
        after-cut-paste.pptx after-cut-paste.json after-cut-paste.sha256.txt
else
    lane_failed=true
    record "paste-after-cut-restores-editable-shapes" "failed" \
        "Ctrl+V after cut did not restore the exact fresh-ID editable two-shape package." \
        paste-after-cut-proof.txt paste-after-cut-state.txt
fi

hash_file "$document_path" > "$output/fixture-mounted-after.sha256.txt"

if $lane_failed; then
    exit 1
fi
