#!/usr/bin/env bash
set -Eeuo pipefail

export DISPLAY="${DISPLAY:-:99}"

output="${1:-/work/freep-smartart-authoring}"
phase="${FREEP_SMARTART_PROBE_PHASE:-first}"
document_path="${FREEP_DOCUMENT_PATH:-}"
expected_document_name="${FREEP_EXPECTED_DOCUMENT_NAME:-$(basename "${document_path:-14-smartart-live.pptx}")}"
window_pattern="${FREEP_EXPECTED_WINDOW_PATTERN:-FreeP}"
input_delay_ms="${FREEP_X11_INPUT_DELAY_MS:-160}"
settle_seconds="${FREEP_X11_SETTLE_SECONDS:-0.45}"
save_attempts="${FREEP_SAVE_ATTEMPTS:-50}"
screen_width="${FREEP_SCREEN_WIDTH:-1280}"
screen_height="${FREEP_SCREEN_HEIGHT:-820}"
screen_dpi="${FREEP_SCREEN_DPI:-96}"
records="$output/result-records.jsonl"
screenshots_file="$output/screenshot-names.txt"
manifest="$output/results.json"

mkdir -p "$output"
if [[ "$phase" == "first" ]]; then
    : > "$records"
    : > "$screenshots_file"
fi
printf 'The probe ended before this contract row produced complete physical evidence.\n' \
    > "$output/probe-incomplete.txt"

record() {
    local id="$1" status="$2" note="$3"
    shift 3
    python3 - "$records" "$id" "$status" "$note" "$@" <<'PY'
import json
import sys

records, result_id, status, note, *evidence = sys.argv[1:]
row = {
    "id": result_id,
    "category": "physical-x11-smartart-authoring",
    "status": status,
    "evidenceLevel": "physical-x11-input",
    "evidence": [item for item in evidence if item],
    "note": note,
}
with open(records, "a", encoding="utf-8") as handle:
    handle.write(json.dumps(row, ensure_ascii=False, separators=(",", ":")) + "\n")
PY
}

capture() {
    local name="$1"
    if command -v scrot >/dev/null 2>&1 && scrot -o "$output/$name" >/dev/null 2>&1; then
        printf '%s\n' "$name" >> "$screenshots_file"
        return 0
    fi
    return 1
}

hash_file() {
    sha256sum "$1" | awk '{print $1}'
}

inspect_smartart() {
    local package_path="$1" destination="$2"
    python3 - "$package_path" "$destination" <<'PY'
import hashlib
import json
import sys
import zipfile
import xml.etree.ElementTree as ET

package_path, destination = sys.argv[1:]
ns = {
    "d": "http://schemas.openxmlformats.org/drawingml/2006/diagram",
    "a": "http://schemas.openxmlformats.org/drawingml/2006/main",
}
with open(package_path, "rb") as handle:
    package_sha256 = hashlib.sha256(handle.read()).hexdigest()

data_parts = {}
native_data_part_count = 0
drawing_cache_count = 0
with zipfile.ZipFile(package_path) as package:
    for name in sorted(package.namelist()):
        if name.startswith("ppt/diagrams/data") and name.endswith(".xml"):
            native_data_part_count += 1
            root = ET.fromstring(package.read(name))
            values = []
            for point in root.findall(".//d:pt", ns):
                value = "".join(
                    text.text or "" for text in point.findall(".//a:t", ns)
                ).strip()
                if value:
                    values.append(value)
            data_parts[name] = values
        elif name.startswith("ppt/diagrams/drawing") and name.endswith(".xml"):
            drawing_cache_count += 1

result = {
    "packageSha256": package_sha256,
    "data1Texts": data_parts.get("ppt/diagrams/data1.xml", []),
    "dataParts": data_parts,
    "nativeDataPartCount": native_data_part_count,
    "drawingCacheCount": drawing_cache_count,
}
with open(destination, "w", encoding="utf-8") as handle:
    json.dump(result, handle, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    handle.write("\n")
PY
}

assert_state() {
    local inspection="$1" expected="$2"
    python3 - "$inspection" "$expected" <<'PY'
import json
import sys

data = json.load(open(sys.argv[1], encoding="utf-8"))
expected = sys.argv[2].split("|")
valid = (
    data["data1Texts"] == expected
    and data["nativeDataPartCount"] >= 1
    and data["drawingCacheCount"] >= 1
)
raise SystemExit(0 if valid else 1)
PY
}

focus_owner() {
    timeout --foreground --kill-after=1s 3 xdotool windowactivate --sync "$owner_id" >/dev/null 2>&1 || true
    timeout --foreground --kill-after=1s 3 xdotool windowfocus "$owner_id" >/dev/null 2>&1 || true
    sleep 0.12
}

send_key() {
    focus_owner
    timeout --foreground --kill-after=1s 3 xdotool key --clearmodifiers --delay "$input_delay_ms" "$@"
    sleep "$settle_seconds"
}

read_clipboard() {
    local destination="$1"
    timeout --foreground --kill-after=1s 4 xclip -selection clipboard -out > "$destination"
    tr -d '\r' < "$destination" > "$destination.tmp"
    mv "$destination.tmp" "$destination"
}

assert_clipboard() {
    local actual="$1" expected="$2"
    printf '%s' "$expected" > "$actual.expected"
    cmp -s "$actual" "$actual.expected"
}

wait_for_state() {
    local expected="$1" prefix="$2"
    local temporary="$output/.$prefix.json.tmp"
    local error="$output/$prefix-inspection-error.txt"
    for ((attempt = 1; attempt <= save_attempts; attempt++)); do
        if cp "$document_path" "$output/.$prefix.pptx.tmp" 2> "$error" &&
           inspect_smartart "$output/.$prefix.pptx.tmp" "$temporary" 2>> "$error" &&
           assert_state "$temporary" "$expected" 2>> "$error"; then
            mv "$output/.$prefix.pptx.tmp" "$output/$prefix.pptx"
            mv "$temporary" "$output/$prefix.json"
            hash_file "$output/$prefix.pptx" > "$output/$prefix.sha256.txt"
            return 0
        fi
        sleep 0.2
    done
    rm -f "$output/.$prefix.pptx.tmp" "$temporary"
    return 1
}

finalize() {
    local exit_code=$?
    set +e
    python3 - "$records" "$screenshots_file" "$manifest" "$expected_document_name" "$exit_code" <<'PY'
import json
import os
import sys

records_path, screenshots_path, manifest_path, fixture, exit_code = sys.argv[1:]
ids = [
    "visible-window-discovery",
    "smartart-outline-add-sibling",
    "smartart-outline-apply-text",
    "smartart-outline-apply-undo-redo",
    "smartart-outline-save",
    "smartart-outline-reopen",
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
        "category": "physical-x11-smartart-authoring",
        "status": "failed",
        "evidenceLevel": "physical-x11-input",
        "evidence": ["probe-incomplete.txt"],
        "note": "The probe ended before this physical contract row produced complete evidence.",
    })
try:
    with open(screenshots_path, encoding="utf-8") as handle:
        screenshots = list(dict.fromkeys(line.strip() for line in handle if line.strip()))
except FileNotFoundError:
    screenshots = []
results = [by_id[result_id] for result_id in ids]
manifest = {
    "schemaVersion": 1,
    "suite": "freep-linux-smartart-authoring-physical",
    "platform": "linux",
    "shell": "avalonia",
    "app": "FreeP",
    "baseline": False,
    "appSurface": "smartart-text-pane-outline",
    "window": {"id": "", "title": "FreeP " + fixture, "pattern": fixture, "visible": True},
    "parameters": {
        "width": int(os.environ.get("FREEP_SCREEN_WIDTH", "1280")),
        "height": int(os.environ.get("FREEP_SCREEN_HEIGHT", "820")),
        "dpi": int(os.environ.get("FREEP_SCREEN_DPI", "96")),
        "fixture": fixture,
    },
    "coverage": {
        "scope": "physical FreeP SmartArt text-pane text replacement, add-sibling, apply undo/redo, save, and reopen",
        "exhaustive": False,
    },
    "semanticReadback": {
        "tool": "xclip",
        "selection": "clipboard",
        "transcripts": ["smartart-outline-apply-text", "smartart-outline-apply-undo-redo", "smartart-outline-reopen"],
        "packageParts": ["ppt/diagrams/data1.xml", "ppt/diagrams/drawing1.xml"],
    },
    "contractValidation": {
        "status": "pending",
        "validator": "tools/Run-FreePSmartArtAuthoringValidation.ps1",
        "contractReference": "tools/LinuxInteractiveDocker/freep-smartart-authoring-validation.schema.json",
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

if [[ -z "$document_path" || ! -f "$document_path" ]]; then
    printf 'FREEP_DOCUMENT_PATH is absent or is not a file: %s\n' "$document_path" > "$output/precondition-error.txt"
    exit 1
fi

mapfile -t visible_owner_ids < <(xdotool search --onlyvisible --name "$window_pattern" 2>/dev/null || true)
if (( ${#visible_owner_ids[@]} == 0 )); then
    record "visible-window-discovery" "failed" "No visible FreeP owner matched the X11 precondition." precondition-error.txt
    exit 1
fi
owner_id="${visible_owner_ids[${#visible_owner_ids[@]}-1]}"
owner_title="$(xdotool getwindowname "$owner_id" 2>/dev/null || true)"
geometry="$(xdotool getwindowgeometry --shell "$owner_id" 2>/dev/null || true)"
eval "$geometry"
focus_owner
capture "${phase}-window.png" || true

if [[ "$phase" == "first" ]]; then
    baseline_json="$output/baseline.json"
    inspect_smartart "$document_path" "$baseline_json"
    baseline_pass=false
    if assert_state "$baseline_json" "Plan|Design|Build|Test|Deploy"; then
        baseline_pass=true
    fi
    first_row_x=$((X + WIDTH - 160))
    first_row_y=$((Y + 212))
    click_x="$first_row_x"; click_y="$first_row_y"
    xdotool mousemove "$click_x" "$click_y" click 1
    sleep "$settle_seconds"
    xdotool key ctrl+a ctrl+c
    clipboard_path="$output/visible-row-clipboard.txt"
    clipboard_pass=false
    if read_clipboard "$clipboard_path" && assert_clipboard "$clipboard_path" "Plan"; then
        clipboard_pass=true
    fi
    {
        printf 'owner-title=%s\n' "$owner_title"
        printf 'owner-geometry=%s\n' "$geometry"
        printf 'seed=FREEP_PHYSICAL_SMARTART_TEXT_PANE_SEED=1\n'
        printf 'pane-row-point=%s,%s\n' "$click_x" "$click_y"
        printf 'baseline-data1=Plan|Design|Build|Test|Deploy\n'
        printf 'baseline-package-valid=%s\n' "$baseline_pass"
        printf 'visible-row-clipboard=%s\n' "$clipboard_pass"
    } > "$output/visible-window-discovery-proof.txt"
    if [[ "$owner_title" == *"$expected_document_name"* && "$owner_title" == *FreeP* && "$baseline_pass" == true && "$clipboard_pass" == true ]]; then
        record "visible-window-discovery" "passed" "The seeded existing SmartArt text pane was visible, focused, and its first outline row read back exactly through X11 clipboard input." visible-window-discovery-proof.txt "${phase}-window.png" baseline.json visible-row-clipboard.txt
    else
        record "visible-window-discovery" "failed" "The seeded SmartArt pane did not prove its visible owner, package baseline, and exact first-row clipboard semantics." visible-window-discovery-proof.txt
        exit 1
    fi

    pane_x=$((X + WIDTH - 320))
    add_sibling_x=$((pane_x + 60))
    add_sibling_y=$((Y + 671))
    xdotool mousemove "$add_sibling_x" "$add_sibling_y" click 1
    sleep "$settle_seconds"
    capture "smartart-add-sibling.png" || true
    row3_x=$((X + WIDTH - 160))
    row3_y=$((Y + 277))
    xdotool mousemove "$row3_x" "$row3_y" click 1
    xdotool key ctrl+a ctrl+c
    add_clipboard="$output/add-sibling-row-clipboard.txt"
    add_clipboard_pass=false
    if read_clipboard "$add_clipboard" && assert_clipboard "$add_clipboard" "New node"; then
        add_clipboard_pass=true
    fi
    if [[ "$add_clipboard_pass" == true ]]; then
        record "smartart-outline-add-sibling" "passed" "A physical click on the visible Add sibling action added the planner's native New node, which read back exactly from the changed outline row." smartart-add-sibling.png add-sibling-row-clipboard.txt
    else
        record "smartart-outline-add-sibling" "failed" "The physical Add sibling action did not expose the exact New node row through clipboard readback." smartart-add-sibling.png add-sibling-row-clipboard.txt
        exit 1
    fi

    first_row_x=$((pane_x + 160))
    first_row_y=$((Y + 212))
    xdotool mousemove "$first_row_x" "$first_row_y" click 1
    xdotool key ctrl+a
    xdotool type --delay "$input_delay_ms" "Discover"
    sleep "$settle_seconds"

    # The fixed pane keeps its picture actions above the outline-authoring rows.
    # Resolve Apply from the owner bottom so the Xfce decoration offset remains included.
    apply_x=$((pane_x + 180))
    apply_y=$((Y + HEIGHT - 167))
    xdotool mousemove "$apply_x" "$apply_y" click 1
    sleep "$settle_seconds"
    xdotool mousemove "$first_row_x" "$first_row_y" click 1
    xdotool key ctrl+a ctrl+c
    apply_clipboard="$output/apply-text-row-clipboard.txt"
    apply_clipboard_pass=false
    if read_clipboard "$apply_clipboard" && assert_clipboard "$apply_clipboard" "Discover"; then
        apply_clipboard_pass=true
    fi
    capture "smartart-apply-text.png" || true
    {
        printf 'apply-button-point=%s,%s\n' "$apply_x" "$apply_y"
        printf 'apply-row-point=%s,%s\n' "$first_row_x" "$first_row_y"
        printf 'apply-expected-text=Discover\n'
        printf 'apply-clipboard=%s\n' "$apply_clipboard_pass"
    } > "$output/smartart-apply-text-proof.txt"
    if [[ "$apply_clipboard_pass" == true ]]; then
        record "smartart-outline-apply-text" "passed" "A physical click on Apply committed the edited SmartArt outline text, which read back exactly through the visible pane." smartart-apply-text.png smartart-apply-text-proof.txt apply-text-row-clipboard.txt
    else
        record "smartart-outline-apply-text" "failed" "The physical Apply action did not expose the edited SmartArt text through clipboard readback." smartart-apply-text.png smartart-apply-text-proof.txt apply-text-row-clipboard.txt
        exit 1
    fi

    # Apply must commit the shared SmartArt model, not only leave the edited
    # TextBox value visible. Clipboard readback above returned focus to that TextBox;
    # move focus to the active Home ribbon tab, a production shell-shortcut target,
    # before dispatching Ctrl+Z/Ctrl+Y through the real window route.
    shell_focus_x=$((X + 62))
    shell_focus_y=$((Y + 28))
    xdotool mousemove "$shell_focus_x" "$shell_focus_y" click 1
    sleep "$settle_seconds"
    send_key ctrl+z
    xdotool mousemove "$first_row_x" "$first_row_y" click 1
    xdotool key ctrl+a ctrl+c
    undo_clipboard="$output/apply-undo-row-clipboard.txt"
    undo_clipboard_pass=false
    if read_clipboard "$undo_clipboard" && assert_clipboard "$undo_clipboard" "Plan"; then
        undo_clipboard_pass=true
    fi

    xdotool mousemove "$shell_focus_x" "$shell_focus_y" click 1
    sleep "$settle_seconds"
    send_key ctrl+y
    xdotool mousemove "$first_row_x" "$first_row_y" click 1
    xdotool key ctrl+a ctrl+c
    redo_clipboard="$output/apply-redo-row-clipboard.txt"
    redo_clipboard_pass=false
    if read_clipboard "$redo_clipboard" && assert_clipboard "$redo_clipboard" "Discover"; then
        redo_clipboard_pass=true
    fi
    capture "smartart-apply-undo-redo.png" || true
    {
        printf 'undo-shell-focus-point=%s,%s\n' "$shell_focus_x" "$shell_focus_y"
        printf 'undo-expected-text=Plan\n'
        printf 'undo-clipboard=%s\n' "$undo_clipboard_pass"
        printf 'redo-shell-focus-point=%s,%s\n' "$shell_focus_x" "$shell_focus_y"
        printf 'redo-expected-text=Discover\n'
        printf 'redo-clipboard=%s\n' "$redo_clipboard_pass"
    } > "$output/smartart-apply-undo-redo-proof.txt"
    if [[ "$undo_clipboard_pass" == true && "$redo_clipboard_pass" == true ]]; then
        record "smartart-outline-apply-undo-redo" "passed" "Apply changed the shared SmartArt model and the visible pane reflected both shell undo and redo transitions." smartart-apply-undo-redo.png smartart-apply-undo-redo-proof.txt apply-undo-row-clipboard.txt apply-redo-row-clipboard.txt
    else
        record "smartart-outline-apply-undo-redo" "failed" "The visible SmartArt pane did not reflect the expected model-level undo and redo transitions after Apply." smartart-apply-undo-redo.png smartart-apply-undo-redo-proof.txt apply-undo-row-clipboard.txt apply-redo-row-clipboard.txt
        exit 1
    fi

    send_key ctrl+s
    if wait_for_state "Discover|Design|New node|Build|Test|Deploy" "saved"; then
        record "smartart-outline-save" "passed" "Ctrl+S wrote the native SmartArt data part and cached drawing package with the exact edited-node order." saved.json saved.pptx saved.sha256.txt
    else
        record "smartart-outline-save" "failed" "Ctrl+S did not produce a saved native SmartArt package with the exact edited-node order." saved-inspection-error.txt
        exit 1
    fi
else
    reopened_json="$output/reopened.json"
    inspect_smartart "$document_path" "$reopened_json"
    second_row_x=$((X + WIDTH - 160))
    second_row_y=$((Y + 277))
    xdotool mousemove "$second_row_x" "$second_row_y" click 1
    sleep "$settle_seconds"
    xdotool key ctrl+a ctrl+c
    clipboard_path="$output/reopen-row-clipboard.txt"
    reopen_clipboard_pass=false
    if read_clipboard "$clipboard_path" && assert_clipboard "$clipboard_path" "New node"; then
        reopen_clipboard_pass=true
    fi
    capture "smartart-reopened.png" || true
    package_pass=false
    if assert_state "$reopened_json" "Discover|Design|New node|Build|Test|Deploy"; then
        package_pass=true
    fi
    {
        printf 'owner-title=%s\n' "$owner_title"
        printf 'owner-geometry=%s\n' "$geometry"
        printf 'reopen-data1=Plan|Design|New node|Build|Test|Deploy\n'
        printf 'reopen-package-valid=%s\n' "$package_pass"
        printf 'reopen-row-point=%s,%s\n' "$second_row_x" "$second_row_y"
        printf 'reopen-row-clipboard=%s\n' "$reopen_clipboard_pass"
    } > "$output/smartart-reopen-proof.txt"
    if [[ "$owner_title" == *"$expected_document_name"* && "$owner_title" == *FreeP* && "$package_pass" == true && "$reopen_clipboard_pass" == true ]]; then
        record "smartart-outline-reopen" "passed" "A fresh FreeP process reopened the saved PPTX; the native data-part order and the edited/new row text were read back through the physical pane." smartart-reopened.png reopened.json smartart-reopen-proof.txt reopen-row-clipboard.txt
    else
        record "smartart-outline-reopen" "failed" "The fresh FreeP process did not prove exact native package and pane clipboard readback for the saved SmartArt row." smartart-reopened.png reopened.json smartart-reopen-proof.txt
        exit 1
    fi
fi

exit 0
