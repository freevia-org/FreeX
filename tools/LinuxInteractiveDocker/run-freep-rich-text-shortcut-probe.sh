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
A = "{" + NS["a"] + "}"

def shape_bounds(shape):
    xfrm = shape.find("p:spPr/a:xfrm", NS)
    off = None if xfrm is None else xfrm.find("a:off", NS)
    ext = None if xfrm is None else xfrm.find("a:ext", NS)
    return {
        "x": int(off.get("x")) if off is not None else None,
        "y": int(off.get("y")) if off is not None else None,
        "cx": int(ext.get("cx")) if ext is not None else None,
        "cy": int(ext.get("cy")) if ext is not None else None,
    }

def paragraph_record(paragraph):
    sequence = []
    for child in paragraph:
        if child.tag in (A + "r", A + "fld"):
            text = child.find("a:t", NS)
            if text is not None:
                sequence.append({"node": "a:t", "value": text.text or ""})
        elif child.tag == A + "br":
            sequence.append({"node": "a:br"})
    return {
        "node": "a:p",
        "childSequence": sequence,
        "text": "".join(
            item.get("value", "") for item in sequence if item["node"] == "a:t"
        ),
        "breakCount": sum(item["node"] == "a:br" for item in sequence),
    }

with open(package_path, "rb") as handle:
    package_sha256 = hashlib.sha256(handle.read()).hexdigest()

with zipfile.ZipFile(package_path) as package:
    slide = ET.fromstring(package.read("ppt/slides/slide1.xml"))

shape_id2_matches = []
for shape in slide.findall(".//p:sp", NS):
    metadata = shape.find("p:nvSpPr/p:cNvPr", NS)
    if metadata is not None and metadata.get("id") == "2":
        shape_id2_matches.append((shape, metadata))

shape_record = None
if len(shape_id2_matches) == 1:
    shape, metadata = shape_id2_matches[0]
    text_body = shape.find("p:txBody", NS)
    paragraphs = [] if text_body is None else [
        paragraph_record(paragraph) for paragraph in text_body.findall("a:p", NS)
    ]
    shape_record = {
        "id": 2,
        "name": metadata.get("name"),
        "bounds": shape_bounds(shape),
        "paragraphCount": len(paragraphs),
        "paragraphs": paragraphs,
        "text": "".join(paragraph["text"] for paragraph in paragraphs),
    }

result = {
    "packageSha256": package_sha256,
    "slide": 1,
    "shapeId2Count": len(shape_id2_matches),
    "shapeId2": shape_record,
    "pPicCount": len(slide.findall(".//p:pic", NS)),
    "pGraphicFrameCount": len(slide.findall(".//p:graphicFrame", NS)),
}
with open(destination, "w", encoding="utf-8", newline="\n") as handle:
    json.dump(result, handle, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    handle.write("\n")
PY
}

assert_inspection_schema() {
    python3 - "$1" <<'PY'
import json
import sys

data = json.load(open(sys.argv[1], encoding="utf-8"))
required = {
    "packageSha256", "slide", "shapeId2Count", "shapeId2",
    "pPicCount", "pGraphicFrameCount",
}
if set(data) != required or data["slide"] != 1:
    raise SystemExit("invalid inspection schema")
if len(data["packageSha256"]) != 64 or any(
    character not in "0123456789abcdef" for character in data["packageSha256"]
):
    raise SystemExit("invalid package SHA256")
shape = data["shapeId2"]
if shape is not None:
    if set(shape) != {"id", "name", "bounds", "paragraphCount", "paragraphs", "text"}:
        raise SystemExit("invalid shape ID2 schema")
    if set(shape["bounds"]) != {"x", "y", "cx", "cy"}:
        raise SystemExit("invalid shape ID2 bounds")
PY
}

assert_baseline_inspection() {
    python3 - "$1" <<'PY'
import json
import sys

data = json.load(open(sys.argv[1], encoding="utf-8"))
shape = data["shapeId2"]
valid = (
    data["shapeId2Count"] == 1
    and shape is not None
    and shape["id"] == 2
    and shape["name"] == "Notes marker"
    and shape["bounds"] == {"x": 914400, "y": 914400, "cx": 2743200, "cy": 914400}
    and shape["paragraphCount"] == 1
    and shape["text"] == "Slide 1 has speaker notes"
    and shape["paragraphs"][0]["text"] == "Slide 1 has speaker notes"
    and shape["paragraphs"][0]["breakCount"] == 0
    and data["pPicCount"] == 0
    and data["pGraphicFrameCount"] == 0
)
raise SystemExit(0 if valid else 1)
PY
}

assert_soft_break_inspection() {
    python3 - "$1" <<'PY'
import json
import sys

data = json.load(open(sys.argv[1], encoding="utf-8"))
shape = data["shapeId2"]
expected_sequence = [
    {"node": "a:t", "value": "SoftBefore"},
    {"node": "a:br"},
    {"node": "a:t", "value": "SoftAfter"},
]
valid = (
    data["shapeId2Count"] == 1
    and shape is not None
    and shape["id"] == 2
    and shape["name"] == "Notes marker"
    and shape["bounds"] == {"x": 914400, "y": 914400, "cx": 2743200, "cy": 914400}
    and shape["paragraphCount"] == 1
    and shape["text"] == "SoftBeforeSoftAfter"
    and shape["paragraphs"] == [{
        "node": "a:p",
        "childSequence": expected_sequence,
        "text": "SoftBeforeSoftAfter",
        "breakCount": 1,
    }]
    and data["pPicCount"] == 0
    and data["pGraphicFrameCount"] == 0
)
raise SystemExit(0 if valid else 1)
PY
}

if [[ "${1:-}" == "--inspect-pptx" ]]; then
    [[ $# == 3 ]] || {
        printf 'usage: %s --inspect-pptx PACKAGE OUTPUT_JSON\n' "$0" >&2
        exit 2
    }
    inspect_pptx "$2" "$3"
    assert_inspection_schema "$3"
    exit 0
fi

export DISPLAY="${DISPLAY:-:99}"

output="${1:-/work/freep-rich-text-shortcut-validation}"
document_path="${FREEP_DOCUMENT_PATH:-}"
expected_document_name="${FREEP_EXPECTED_DOCUMENT_NAME:-$(basename "${document_path:-presentation.pptx}")}"
window_pattern="${FREEP_EXPECTED_WINDOW_PATTERN:-FreeP}"
input_delay_ms="${FREEP_X11_INPUT_DELAY_MS:-160}"
settle_seconds="${FREEP_X11_SETTLE_SECONDS:-0.45}"
pointer_timeout_seconds="${FREEP_X11_POINTER_TIMEOUT_SECONDS:-3}"
save_attempts="${FREEP_SAVE_ATTEMPTS:-16}"
screen_width="${FREEP_SCREEN_WIDTH:-1280}"
screen_height="${FREEP_SCREEN_HEIGHT:-820}"
screen_dpi="${FREEP_SCREEN_DPI:-96}"
records="$output/result-records.jsonl"
screenshots_file="$output/screenshot-names.txt"
manifest="$output/results.json"
required_ids=(
    "visible-window-discovery"
    "rich-editor-physical-soft-break-input"
    "saved-soft-break-native-package"
    "undo-restores-original-text"
    "redo-restores-soft-break"
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
    "category": "physical-x11-rich-text-shortcut",
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

save_checkpoint() {
    local prefix="$1" predicate="$2"
    local temporary_pptx="$output/.$prefix.pptx.tmp"
    local temporary_json="$output/.$prefix.json.tmp"
    local error_file="$output/$prefix-inspection-error.txt"

    send_owner_key ctrl+s || return 1
    for ((attempt = 1; attempt <= save_attempts; attempt++)); do
        if cp "$document_path" "$temporary_pptx" 2> "$error_file" &&
           inspect_pptx "$temporary_pptx" "$temporary_json" 2>> "$error_file" &&
           assert_inspection_schema "$temporary_json" 2>> "$error_file" &&
           "$predicate" "$temporary_json" 2>> "$error_file"; then
            mv "$temporary_pptx" "$output/$prefix.pptx"
            mv "$temporary_json" "$output/$prefix.json"
            hash_file "$output/$prefix.pptx" > "$output/$prefix.sha256.txt"
            return 0
        fi
        sleep 0.2
    done
    rm -f "$temporary_pptx" "$temporary_json"
    if [[ ! -s "$error_file" ]]; then
        printf 'Checkpoint %s never satisfied predicate %s after %s attempts.\n' \
            "$prefix" "$predicate" "$save_attempts" > "$error_file"
    fi
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
    "rich-editor-physical-soft-break-input",
    "saved-soft-break-native-package",
    "undo-restores-original-text",
    "redo-restores-soft-break",
]
by_id = {}
try:
    with open(records_path, encoding="utf-8") as handle:
        for line in handle:
            if line.strip():
                row = json.loads(line)
                if row.get("id") in ids:
                    by_id[row["id"]] = row
except (FileNotFoundError, json.JSONDecodeError):
    pass
for result_id in ids:
    by_id.setdefault(result_id, {
        "id": result_id,
        "category": "physical-x11-rich-text-shortcut",
        "status": "failed",
        "evidenceLevel": "physical-x11-input",
        "evidence": ["probe-incomplete.txt"],
        "note": "The probe ended before this physical contract row produced complete evidence.",
    })
results = [by_id[result_id] for result_id in ids]
try:
    with open(screenshots_path, encoding="utf-8") as handle:
        screenshots = list(dict.fromkeys(
            line.strip() for line in handle if line.strip()
        ))
except FileNotFoundError:
    screenshots = []
manifest = {
    "schemaVersion": 1,
    "suite": "freep-linux-rich-text-shortcut-physical",
    "platform": "linux",
    "shell": "avalonia",
    "app": "FreeP",
    "baseline": False,
    "appSurface": "in-canvas-rich-text-soft-break",
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
        "scope": "physical FreeP rich-editor soft-break evidence lane",
        "exhaustive": False,
    },
    "contractValidation": {
        "status": "pending",
        "validator": "tools/Run-FreePRichTextShortcutValidation.ps1",
        "contractReference": "tools/LinuxInteractiveDocker/freep-rich-text-shortcut-validation.schema.json",
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
assert_inspection_schema "$output/baseline-package-inspection.json"
baseline_valid=false
if assert_baseline_inspection "$output/baseline-package-inspection.json"; then
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
if active_owner_now; then
    owner_focused=true
fi
baseline_capture=false
if capture "baseline.png"; then
    baseline_capture=true
fi
capture_window_state "owner-discovery-state.txt"
{
    printf 'owner-window-id=%s\n' "$owner_id"
    printf 'owner-window-title=%s\n' "$owner_title"
    printf 'expected-fixture-filename=%s\n' "$expected_document_name"
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
        "Visible focused FreeP owner, fixture title, screenshot, and exact original shape ID2 package semantics were discovered." \
        visible-window-discovery-proof.txt owner-discovery-state.txt baseline.png \
        baseline.pptx baseline-package-inspection.json fixture-mounted-before.sha256.txt
else
    lane_failed=true
    record "visible-window-discovery" "failed" \
        "The visible owner did not prove focus, title, screenshot, and exact original shape ID2 package semantics." \
        visible-window-discovery-proof.txt owner-discovery-state.txt
fi

geometry="$(xdotool getwindowgeometry --shell "$owner_id" 2>/dev/null || true)"
eval "$geometry"
if [[ ! "${X:-}" =~ ^-?[0-9]+$ || ! "${Y:-}" =~ ^-?[0-9]+$ ||
      ! "${WIDTH:-}" =~ ^[0-9]+$ || ! "${HEIGHT:-}" =~ ^[0-9]+$ ]]; then
    printf 'Owner geometry was incomplete or nonnumeric.\n%s\n' "$geometry" \
        > "$output/shape-pointer-calibration-error.txt"
    exit 1
fi

pane_width=180
stage_body_top=$((Y + 137))
stage_body_height=$((HEIGHT - 241))
fit_box_x=$((X + pane_width + 40))
fit_box_y=$((stage_body_top + 40))
fit_box_width=$((WIDTH - pane_width - 80))
fit_box_height=$((stage_body_height - 80))
slide_width_emu=12192000
slide_height_emu=6858000
shape_center_x_emu=2286000
shape_center_y_emu=1371600

if (( fit_box_width * 9 <= fit_box_height * 16 )); then
    slide_width_px=$fit_box_width
    slide_height_px=$(((fit_box_width * 9 + 8) / 16))
    slide_x=$fit_box_x
    slide_y=$((fit_box_y + (fit_box_height - slide_height_px + 1) / 2))
    fit_constraint=width
else
    slide_height_px=$fit_box_height
    slide_width_px=$(((fit_box_height * 16 + 4) / 9))
    slide_x=$((fit_box_x + (fit_box_width - slide_width_px + 1) / 2))
    slide_y=$fit_box_y
    fit_constraint=height
fi
shape_center_x=$((slide_x +
    (slide_width_px * shape_center_x_emu + slide_width_emu / 2) /
        slide_width_emu))
shape_center_y=$((slide_y +
    (slide_height_px * shape_center_y_emu + slide_height_emu / 2) /
        slide_height_emu))
commit_point_x=$((slide_x + slide_width_px - 24))
commit_point_y=$((slide_y + slide_height_px - 24))

if (( fit_box_width <= 0 || fit_box_height <= 0 ||
      slide_width_px <= 0 || slide_height_px <= 0 ||
      shape_center_x < slide_x || shape_center_x >= slide_x + slide_width_px ||
      shape_center_y < slide_y || shape_center_y >= slide_y + slide_height_px ||
      commit_point_x < slide_x || commit_point_x >= slide_x + slide_width_px ||
      commit_point_y < slide_y || commit_point_y >= slide_y + slide_height_px )); then
    printf 'Derived calibration geometry was nonsensical.\n%s\n' "$geometry" \
        > "$output/shape-pointer-calibration-error.txt"
    exit 1
fi
{
    printf 'calibration=derived 16:9 slide fit and fixture EMU center\n'
    printf 'owner-geometry-begin\n%s\nowner-geometry-end\n' "$geometry"
    printf 'fit-constraint=%s\n' "$fit_constraint"
    printf 'fixture-slide-size-emu=%s,%s\n' "$slide_width_emu" "$slide_height_emu"
    printf 'derived-slide-rect=%s,%s,%s,%s\n' \
        "$slide_x" "$slide_y" "$slide_width_px" "$slide_height_px"
    printf 'shape-bounds-emu=914400,914400,2743200,914400\n'
    printf 'shape-center-emu=%s,%s\n' "$shape_center_x_emu" "$shape_center_y_emu"
    printf 'shape-center-point=%s,%s\n' "$shape_center_x" "$shape_center_y"
    printf 'natural-commit-point=%s,%s\n' "$commit_point_x" "$commit_point_y"
} > "$output/shape-pointer-calibration.txt"

input_commands_ok=true
focus_owner
xdotool mousemove --sync "$shape_center_x" "$shape_center_y" >/dev/null 2>&1 ||
    input_commands_ok=false
xdotool click --clearmodifiers --repeat 2 --delay 120 1 >/dev/null 2>&1 ||
    input_commands_ok=false
sleep "$settle_seconds"
send_owner_key ctrl+a || input_commands_ok=false
timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
    xdotool type --clearmodifiers --delay "$input_delay_ms" "SoftBefore" ||
    input_commands_ok=false
timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
    xdotool key --clearmodifiers --delay "$input_delay_ms" shift+Return ||
    input_commands_ok=false
timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
    xdotool type --clearmodifiers --delay "$input_delay_ms" "SoftAfter" ||
    input_commands_ok=false
sleep "$settle_seconds"
input_capture=false
if capture "soft-break-input.png"; then
    input_capture=true
fi
capture_window_state "soft-break-input-state.txt"
xdotool mousemove --sync "$commit_point_x" "$commit_point_y" >/dev/null 2>&1 ||
    input_commands_ok=false
xdotool click --clearmodifiers 1 >/dev/null 2>&1 || input_commands_ok=false
sleep "$settle_seconds"
commit_capture=false
if capture "soft-break-committed.png"; then
    commit_capture=true
fi
capture_window_state "soft-break-committed-state.txt"
{
    printf 'editor-entry=physical double click at shape ID2 center\n'
    printf 'replacement=Ctrl+A then ASCII SoftBefore\n'
    printf 'soft-break=physical Shift+Enter\n'
    printf 'suffix=ASCII SoftAfter\n'
    printf 'commit=physical click at calibrated slide point outside shape ID2\n'
    printf 'input-commands-ok=%s\n' "$input_commands_ok"
    printf 'input-screenshot-captured=%s\n' "$input_capture"
    printf 'commit-screenshot-captured=%s\n' "$commit_capture"
} > "$output/rich-editor-physical-input-proof.txt"

input_pass=false
if $visible_pass && $input_commands_ok && $input_capture && $commit_capture; then
    input_pass=true
    record "rich-editor-physical-soft-break-input" "passed" \
        "Physical X11 input entered shape ID2, replaced its text, sent Shift+Enter between the ASCII tokens, and committed naturally outside the shape." \
        rich-editor-physical-input-proof.txt shape-pointer-calibration.txt \
        soft-break-input.png soft-break-input-state.txt \
        soft-break-committed.png soft-break-committed-state.txt
else
    lane_failed=true
    record "rich-editor-physical-soft-break-input" "failed" \
        "The physical editor entry, replacement, Shift+Enter, suffix, commit, and screenshots were not all completed." \
        rich-editor-physical-input-proof.txt shape-pointer-calibration.txt \
        soft-break-input-state.txt soft-break-committed-state.txt
fi

saved_checkpoint=false
if save_checkpoint "after-soft-break" assert_soft_break_inspection; then
    saved_checkpoint=true
fi
{
    printf 'checkpoint=after-soft-break.pptx\n'
    printf 'shape-id=2\n'
    printf 'paragraph-count=1\n'
    printf 'ordered-children=a:t SoftBefore; a:br; a:t SoftAfter\n'
    printf 'fallback-counts=p:pic 0; p:graphicFrame 0\n'
    printf 'checkpoint-valid=%s\n' "$saved_checkpoint"
} > "$output/saved-soft-break-native-package-proof.txt"

saved_pass=false
if $input_pass && $saved_checkpoint; then
    saved_pass=true
    record "saved-soft-break-native-package" "passed" \
        "Ctrl+S saved shape ID2 as exactly one native paragraph with ordered SoftBefore text, break, SoftAfter text and zero fallback objects." \
        saved-soft-break-native-package-proof.txt after-soft-break.pptx \
        after-soft-break.json after-soft-break.sha256.txt
else
    lane_failed=true
    record "saved-soft-break-native-package" "failed" \
        "The saved package did not prove the exact native shape ID2 soft-break structure." \
        saved-soft-break-native-package-proof.txt after-soft-break-inspection-error.txt
fi

undo_key_sent=true
send_owner_key ctrl+z || undo_key_sent=false
undo_capture=false
if capture "undo-original.png"; then
    undo_capture=true
fi
capture_window_state "undo-original-state.txt"
undo_checkpoint=false
if save_checkpoint "after-undo" assert_baseline_inspection; then
    undo_checkpoint=true
fi
{
    printf 'shortcut=Ctrl+Z\n'
    printf 'key-sent=%s\n' "$undo_key_sent"
    printf 'expected-original-text=Slide 1 has speaker notes\n'
    printf 'exact-original-checkpoint=%s\n' "$undo_checkpoint"
    printf 'screenshot-captured=%s\n' "$undo_capture"
} > "$output/undo-restores-original-text-proof.txt"

undo_pass=false
if $saved_pass && $undo_key_sent && $undo_checkpoint && $undo_capture; then
    undo_pass=true
    record "undo-restores-original-text" "passed" \
        "Ctrl+Z and Ctrl+S restored the exact original shape ID2 text, bounds, paragraph semantics, and zero fallback objects." \
        undo-restores-original-text-proof.txt undo-original.png \
        undo-original-state.txt after-soft-break.pptx after-soft-break.json \
        after-undo.pptx after-undo.json after-undo.sha256.txt
else
    lane_failed=true
    record "undo-restores-original-text" "failed" \
        "Ctrl+Z did not restore the exact original shape ID2 package semantics." \
        undo-restores-original-text-proof.txt undo-original-state.txt \
        after-undo-inspection-error.txt
fi

redo_key_sent=true
send_owner_key ctrl+shift+z || redo_key_sent=false
redo_capture=false
if capture "redo-soft-break.png"; then
    redo_capture=true
fi
capture_window_state "redo-soft-break-state.txt"
redo_checkpoint=false
if save_checkpoint "after-redo" assert_soft_break_inspection; then
    redo_checkpoint=true
fi
{
    printf 'shortcut=Ctrl+Shift+Z\n'
    printf 'key-sent=%s\n' "$redo_key_sent"
    printf 'expected-ordered-children=a:t SoftBefore; a:br; a:t SoftAfter\n'
    printf 'exact-soft-break-checkpoint=%s\n' "$redo_checkpoint"
    printf 'screenshot-captured=%s\n' "$redo_capture"
} > "$output/redo-restores-soft-break-proof.txt"

redo_pass=false
if $undo_pass && $redo_key_sent && $redo_checkpoint && $redo_capture; then
    redo_pass=true
    record "redo-restores-soft-break" "passed" \
        "Ctrl+Shift+Z and Ctrl+S restored the exact native shape ID2 soft-break structure and zero fallback objects." \
        redo-restores-soft-break-proof.txt redo-soft-break.png \
        redo-soft-break-state.txt after-undo.pptx after-undo.json \
        after-redo.pptx after-redo.json after-redo.sha256.txt
else
    lane_failed=true
    record "redo-restores-soft-break" "failed" \
        "Ctrl+Shift+Z did not restore the exact native shape ID2 soft-break package semantics." \
        redo-restores-soft-break-proof.txt redo-soft-break-state.txt \
        after-redo-inspection-error.txt
fi

hash_file "$document_path" > "$output/fixture-mounted-after.sha256.txt"

if $lane_failed; then
    exit 1
fi
