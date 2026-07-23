#!/usr/bin/env bash
set -euo pipefail

export DISPLAY="${DISPLAY:-:99}"

output="${1:-/work/x11-validation}"
input_delay_ms="${FREEX_X11_INPUT_DELAY_MS:-160}"
type_delay_ms="${FREEX_X11_TYPE_DELAY_MS:-90}"
settle_seconds="${FREEX_X11_SETTLE_SECONDS:-0.35}"
dialog_settle_seconds="${FREEX_X11_DIALOG_SETTLE_SECONDS:-3.0}"
selection_color="${FREEX_X11_SELECTION_COLOR:-#217346}"
document_path="${FREEX_X11_DOCUMENT_PATH:-/documents/linux-interactive-demo.csv}"

mkdir -p "$output"

declare -a results=()
manifest_written=false
calibration_status="failed"
calibration_reason="Calibration did not run."
window_id=""
window_x=0
window_y=0
window_width=0
window_height=0
a1_x=0
a1_y=0
cell_width=0
cell_height=0

json_escape() {
    local value="$1"
    value="${value//\\/\\\\}"
    value="${value//\"/\\\"}"
    value="${value//$'\n'/\\n}"
    printf '%s' "$value"
}

artifact_json() {
    local value="${1:-}" token first=true
    local -a tokens=()
    printf '['
    IFS=';' read -ra tokens <<< "$value"
    for token in "${tokens[@]}"; do
        token="${token#${token%%[![:space:]]*}}"
        token="${token%${token##*[![:space:]]}}"
        [[ -z "$token" ]] && continue
        if $first; then first=false; else printf ','; fi
        printf '"%s"' "$(json_escape "$token")"
    done
    printf ']'
}

record() {
    local id="$1" status="$2" evidence="$3" note="${4:-}" artifacts="${5:-}"
    results+=("{\"id\":\"$(json_escape "$id")\",\"category\":\"x11-input\",\"status\":\"$status\",\"evidenceLevel\":\"physical-x11-input\",\"evidence\":\"$(json_escape "$evidence")\",\"artifacts\":$(artifact_json "$artifacts"),\"note\":\"$(json_escape "$note")\"}")
}

write_manifest() {
    local passed=0 failed=0 result first=true
    for result in "${results[@]}"; do
        if [[ "$result" == *'"status":"passed"'* ]]; then
            ((passed += 1))
        else
            ((failed += 1))
        fi
    done

    {
        printf '{"schemaVersion":2,"platform":"linux","shell":"avalonia"'
        printf ',"calibration":{"status":"%s","reason":"%s"' \
            "$calibration_status" "$(json_escape "$calibration_reason")"
        printf ',"selectionColor":"%s"' "$selection_color"
        printf ',"window":{"id":"%s","x":%d,"y":%d,"width":%d,"height":%d}' \
            "$window_id" "$window_x" "$window_y" "$window_width" "$window_height"
        printf ',"grid":{"a1":{"x":%d,"y":%d},"cellWidth":%d,"cellHeight":%d}' \
            "$a1_x" "$a1_y" "$cell_width" "$cell_height"
        printf ',"evidence":["calibration-a1.png","calibration-b1.png","calibration-a2.png"]}'
        printf ',"summary":{"passed":%d,"failed":%d,"total":%d}' "$passed" "$failed" "$((passed + failed))"
        printf ',"results":['
        for result in "${results[@]}"; do
            if $first; then first=false; else printf ','; fi
            printf '%s' "$result"
        done
        printf ']}\n'
    } > "$output/x11-input-results.json"
    manifest_written=true
}

on_error() {
    local exit_code=$?
    trap - ERR
    xdotool mouseup 1 >/dev/null 2>&1 || true
    if ! $manifest_written; then
        local runtime_reason="Probe aborted unexpectedly at line ${BASH_LINENO[0]} (exit $exit_code)."
        if [[ "$calibration_status" != "passed" ]]; then
            calibration_reason="$runtime_reason"
        fi
        record "x11-probe-runtime" "failed" "x11-input-results.json" "$runtime_reason"
        write_manifest
    fi
    exit "$exit_code"
}
trap on_error ERR

window_id="$(xdotool search --onlyvisible --name '^.+ - FreeX$' 2>/dev/null | tail -1 || true)"
if [[ -z "$window_id" ]]; then
    calibration_reason="No visible FreeX window."
    record "x11-window-discovery" "failed" "x11-input-results.json" "$calibration_reason"
    write_manifest
    exit 2
fi

focus_app() {
    xdotool windowactivate --sync "$window_id" 2>/dev/null || true
    xdotool windowfocus "$window_id" 2>/dev/null || true
    sleep 0.12
}

send_key() {
    focus_app
    xdotool key --clearmodifiers --delay "$input_delay_ms" --window "$window_id" "$@"
    sleep "$settle_seconds"
}

type_text() {
    local value="$1"
    focus_app
    xdotool type --clearmodifiers --delay "$type_delay_ms" --window "$window_id" "$value"
    sleep "$settle_seconds"
}

clipboard_text() {
    xclip -selection clipboard -out 2>/dev/null | tr -d '\r\n'
}

wait_for_clipboard() {
    local expected="$1" value
    for _ in $(seq 1 10); do
        value="$(clipboard_text)"
        if [[ "$value" == "$expected" ]]; then
            printf '%s' "$value"
            return 0
        fi
        sleep 0.12
    done
    printf '%s' "$(clipboard_text)"
    return 1
}

set_cell_text() {
    local column_offset="$1" row_offset="$2" address="$3" value="$4"
    select_cell "$column_offset" "$row_offset" "$address" || return 1
    send_key F2
    send_key ctrl+a
    # xdotool type with an empty string is a no-op; erase explicitly so an empty
    # destination is genuinely seeded as empty.
    send_key BackSpace
    if [[ -n "$value" ]]; then
        type_text "$value"
    fi
    send_key Return
    select_cell "$column_offset" "$row_offset" "$address"
}

csv_cell_value() {
    local column_offset="$1" row_offset="$2"
    [[ -f "$document_path" ]] || return 1
    # CSV rows map directly to worksheet rows; row offset 0 is the first row.
    awk -F',' -v row="$((row_offset + 1))" -v column="$((column_offset + 1))" \
        'NR == row { print $column; found = 1; exit } END { if (!found) print "" }' \
        "$document_path" | tr -d '\r'
}

wait_for_csv_cell() {
    local column_offset="$1" row_offset="$2" expected="$3" value
    for _ in $(seq 1 12); do
        value="$(csv_cell_value "$column_offset" "$row_offset")"
        if [[ "$value" == "$expected" ]]; then
            return 0
        fi
        sleep 0.2
    done
    return 1
}

write_artifact() {
    local name="$1" contents="$2"
    printf '%b\n' "$contents" > "$output/$name"
}

copy_cell_display() {
    local column_offset="$1" row_offset="$2" address="$3"
    # xclip forks a clipboard owner. Redirect its descriptors so a caller using command
    # substitution does not wait forever for the inherited output pipe to close.
    printf 'clipboard-sentinel' | xclip -selection clipboard -in >/dev/null 2>&1
    select_cell "$column_offset" "$row_offset" "$address" || return 1
    send_key ctrl+c
    clipboard_text
}

copy_cell_formula() {
    local column_offset="$1" row_offset="$2" address="$3"
    printf 'clipboard-sentinel' | xclip -selection clipboard -in >/dev/null 2>&1
    select_cell "$column_offset" "$row_offset" "$address" || return 1
    send_key F2
    send_key ctrl+a
    send_key ctrl+c
    local value
    value="$(clipboard_text)"
    send_key Escape
    printf '%s' "$value"
}

capture() {
    scrot -o "$output/$1"
}

screen_changed() {
    local before="$1" after="$2" minimum="${3:-300}" changed
    changed="$(compare -metric AE "$before" "$after" null: 2>&1 || true)"
    [[ "$changed" =~ ^[0-9]+$ ]] && (( changed >= minimum ))
}

region_changed() {
    local before="$1" after="$2" minimum="${3:-8}" changed
    changed="$(compare -metric AE "$before" "$after" null: 2>&1 || true)"
    [[ "$changed" =~ ^[0-9]+$ ]] && (( changed >= minimum ))
}

regions_match() {
    local before="$1" after="$2" maximum="${3:-2}" changed
    changed="$(compare -metric AE "$before" "$after" null: 2>&1 || true)"
    [[ "$changed" =~ ^[0-9]+$ ]] && (( changed <= maximum ))
}

selection_box() {
    local screenshot="$1" components box
    components="$(convert "$screenshot" \
        -alpha off \
        -fill black +opaque "$selection_color" \
        -fill white -opaque "$selection_color" \
        -define connected-components:verbose=true \
        -connected-components 8 null: 2>&1)"
    box="$(printf '%s\n' "$components" | awk '
        /srgb\(255,255,255\)/ && $4 + 0 > largest { largest = $4 + 0; box = $2 }
        END { print box }
    ')"
    if [[ ! "$box" =~ ^([0-9]+)x([0-9]+)\+([0-9]+)\+([0-9]+)$ ]]; then
        return 1
    fi

    observed_width="${BASH_REMATCH[1]}"
    observed_height="${BASH_REMATCH[2]}"
    observed_x="${BASH_REMATCH[3]}"
    observed_y="${BASH_REMATCH[4]}"
    (( observed_width >= 20 && observed_width <= 500 && observed_height >= 12 && observed_height <= 120 ))
}

capture_selection() {
    local name="$1"
    capture "$name"
    selection_box "$output/$name"
}

box_near() {
    local expected_x="$1" expected_y="$2" tolerance="${3:-3}"
    (( observed_x >= expected_x - tolerance && observed_x <= expected_x + tolerance &&
       observed_y >= expected_y - tolerance && observed_y <= expected_y + tolerance ))
}

wait_for_selection() {
    local expected_x="$1" expected_y="$2" evidence="$3"
    for _ in $(seq 1 8); do
        if capture_selection "$evidence" && box_near "$expected_x" "$expected_y"; then
            return 0
        fi
        sleep 0.12
    done
    return 1
}

restore_a1() {
    for _ in $(seq 1 2); do
        send_key ctrl+Home
        if wait_for_selection "$a1_x" "$a1_y" "selection-a1-current.png"; then
            return 0
        fi
    done
    return 1
}

calibrate_geometry() {
    wmctrl -ir "$window_id" -b add,maximized_vert,maximized_horz 2>/dev/null || true
    focus_app
    for _ in $(seq 1 3); do
        xdotool key --clearmodifiers --delay "$input_delay_ms" --window "$window_id" Escape 2>/dev/null || true
        sleep 0.18
    done

    eval "$(xdotool getwindowgeometry --shell "$window_id")"
    window_x="$X"
    window_y="$Y"
    window_width="$WIDTH"
    window_height="$HEIGHT"

    send_key ctrl+Home
    if ! capture_selection "calibration-a1.png"; then
        calibration_reason="Could not isolate the active-cell selection outline after Ctrl+Home."
        return 1
    fi
    a1_x="$observed_x"
    a1_y="$observed_y"
    local a1_width="$observed_width" a1_height="$observed_height"

    local moved=false
    for _ in $(seq 1 2); do
        send_key Right
        for _ in $(seq 1 8); do
            if capture_selection "calibration-b1.png" &&
               (( observed_x > a1_x + 20 && observed_x < a1_x + 240 )) &&
               (( observed_y >= a1_y - 3 && observed_y <= a1_y + 3 )); then
                moved=true
                break 2
            fi
            sleep 0.12
        done
    done
    if ! $moved; then
        calibration_reason="The paced Right key did not produce a measurable A1-to-B1 selection transition."
        return 1
    fi
    cell_width=$((observed_x - a1_x))

    send_key ctrl+Home
    if ! wait_for_selection "$a1_x" "$a1_y" "calibration-a1-return.png"; then
        calibration_reason="Ctrl+Home did not restore the calibrated A1 selection."
        return 1
    fi

    moved=false
    for _ in $(seq 1 2); do
        send_key Down
        for _ in $(seq 1 8); do
            if capture_selection "calibration-a2.png" &&
               (( observed_y > a1_y + 10 && observed_y < a1_y + 120 )) &&
               (( observed_x >= a1_x - 3 && observed_x <= a1_x + 3 )); then
                moved=true
                break 2
            fi
            sleep 0.12
        done
    done
    if ! $moved; then
        calibration_reason="The paced Down key did not produce a measurable A1-to-A2 selection transition."
        return 1
    fi
    cell_height=$((observed_y - a1_y))

    if (( cell_width < 24 || cell_width > 240 || cell_height < 14 || cell_height > 120 )); then
        calibration_reason="Discovered implausible cell geometry ${cell_width}x${cell_height}."
        return 1
    fi
    if (( a1_width < cell_width || a1_width > cell_width + 16 ||
          a1_height < cell_height || a1_height > cell_height + 16 )); then
        calibration_reason="Selection outline ${a1_width}x${a1_height} is inconsistent with cell pitch ${cell_width}x${cell_height}."
        return 1
    fi
    if ! restore_a1; then
        calibration_reason="Could not restore A1 after geometry calibration."
        return 1
    fi

    calibration_status="passed"
    calibration_reason="Derived from paced Ctrl+Home, A1-to-B1, and A1-to-A2 physical selection transitions."
}

cell_x() { printf '%d' "$((a1_x + $1 * cell_width))"; }
cell_y() { printf '%d' "$((a1_y + $1 * cell_height))"; }
cell_center_x() { printf '%d' "$((a1_x + $1 * cell_width + cell_width / 2))"; }
cell_center_y() { printf '%d' "$((a1_y + $1 * cell_height + cell_height / 2))"; }

select_cell() {
    local column_offset="$1" row_offset="$2" address="$3"
    local expected_x expected_y center_x center_y
    expected_x="$(cell_x "$column_offset")"
    expected_y="$(cell_y "$row_offset")"
    center_x="$(cell_center_x "$column_offset")"
    center_y="$(cell_center_y "$row_offset")"

    for _ in $(seq 1 2); do
        focus_app
        xdotool mousemove --sync "$center_x" "$center_y" click 1
        sleep "$settle_seconds"
        if wait_for_selection "$expected_x" "$expected_y" "selection-${address}.png"; then
            return 0
        fi
    done
    return 1
}

crop_cell() {
    local screenshot="$1" output_file="$2" column_offset="$3" row_offset="$4"
    local x y width height
    x=$((a1_x + column_offset * cell_width + 2))
    y=$((a1_y + row_offset * cell_height + 2))
    width=$((cell_width - 4))
    height=$((cell_height - 4))
    convert "$screenshot" -crop "${width}x${height}+${x}+${y}" +repage "$output_file"
}

dismiss_overlays() {
    send_key Escape || true
    send_key Escape || true
}

visible_window_count() {
    wmctrl -l 2>/dev/null | wc -l
}

freex_window_ids() {
    # WindowTitlePlanner composes WPF-compatible workbook titles as "<document> - FreeX".
    # The exact shape excludes dialogs, pickers, and other transient windows.
    xdotool search --onlyvisible --name '^.+ - FreeX$' 2>/dev/null | sort -n || true
}

freex_window_count() {
    freex_window_ids | awk 'NF { count += 1 } END { print count + 0 }'
}

window_bounds_signature() {
    local id
    while read -r id; do
        [[ -z "$id" ]] && continue
        eval "$(xdotool getwindowgeometry --shell "$id" 2>/dev/null)" || continue
        printf '%s:%s,%s,%s,%s\n' "$id" "$X" "$Y" "$WIDTH" "$HEIGHT"
    done < <(freex_window_ids)
}

window_bounds_are_valid() {
    local signature="$1" line id bounds x y width height count=0
    while IFS=: read -r id bounds; do
        [[ -z "$id" ]] && continue
        IFS=',' read -r x y width height <<< "$bounds"
        [[ "$x" =~ ^-?[0-9]+$ && "$y" =~ ^-?[0-9]+$ &&
           "$width" =~ ^[0-9]+$ && "$height" =~ ^[0-9]+$ ]] || return 1
        (( width > 0 && height > 0 )) || return 1
        ((count += 1))
    done <<< "$signature"
    (( count >= 2 ))
}

enter_keytip_mode() {
    focus_app
    xdotool keydown --window "$window_id" Alt_L
    sleep 0.18
    xdotool keyup --window "$window_id" Alt_L
    sleep "$settle_seconds"
}

keytip_key() {
    xdotool key --clearmodifiers --delay "$input_delay_ms" --window "$window_id" "$1"
    sleep "$settle_seconds"
}

enter_view_keytip() {
    enter_keytip_mode
    keytip_key w
}

send_active_key() {
    xdotool key --clearmodifiers --delay "$input_delay_ms" "$@"
    sleep "$settle_seconds"
}

probe_worksheet_context_copy() {
    local value="X11ContextCopy" clipboard="" artifacts="worksheet-context-copy-before.png;worksheet-context-copy-open.png;worksheet-context-copy-after.png;worksheet-context-copy-postcondition.txt"
    if set_cell_text 6 10 G11 "$value"; then
        printf 'clipboard-sentinel' | xclip -selection clipboard -in >/dev/null 2>&1
        capture "worksheet-context-copy-before.png"
        send_key shift+F10
        capture "worksheet-context-copy-open.png"
        send_active_key Home Down Return
        clipboard="$(wait_for_clipboard "$value" || true)"
        capture "worksheet-context-copy-after.png"
        write_artifact "worksheet-context-copy-postcondition.txt" "expected=$value\nclipboard=$clipboard\ncell=G11"
        if [[ "$clipboard" == "$value" ]]; then
            record "worksheet-context-copy-physical" "passed" "worksheet-context-copy-before.png; worksheet-context-copy-open.png; worksheet-context-copy-after.png; clipboard=$clipboard" "The rendered worksheet popup was opened through X11 and its Copy item changed the X11 clipboard to the exact selected-cell value." "$artifacts"
        else
            record "worksheet-context-copy-physical" "failed" "worksheet-context-copy-before.png; worksheet-context-copy-open.png; worksheet-context-copy-after.png; clipboard=$clipboard" "The worksheet popup opened, but its physical Copy activation did not produce the expected clipboard value." "$artifacts"
        fi
    else
        write_artifact "worksheet-context-copy-postcondition.txt" "seeded=false\ncell=G11"
        record "worksheet-context-copy-physical" "failed" "worksheet-context-copy-postcondition.txt" "Could not seed calibrated G11 for the physical worksheet context Copy probe." "worksheet-context-copy-postcondition.txt"
    fi
    dismiss_overlays
}

probe_worksheet_context_clear() {
    local value="X11ContextClear" before_hash="" after_hash="" observed="" artifacts="worksheet-context-clear-before.png;worksheet-context-clear-open.png;worksheet-context-clear-after.png;worksheet-context-clear-postcondition.txt"
    if set_cell_text 6 11 G12 "$value"; then
        send_key ctrl+s
        if ! wait_for_csv_cell 6 11 "$value"; then
            write_artifact "worksheet-context-clear-postcondition.txt" "seeded=false\ncell=G12\nexpected=$value\nobserved=$(json_escape "$(csv_cell_value 6 11)")"
            record "worksheet-context-clear-physical" "failed" "worksheet-context-clear-postcondition.txt" "Could not persist the seeded G12 value before the physical worksheet context Clear probe." "worksheet-context-clear-postcondition.txt"
            dismiss_overlays
            return
        fi
        before_hash="$(sha256sum "$document_path" 2>/dev/null | awk '{print $1}')"
        capture "worksheet-context-clear-before.png"
        send_key shift+F10
        capture "worksheet-context-clear-open.png"
        # Clear is the final top-level item; Right opens its submenu, then Home selects
        # Clear Contents. This is physical keyboard navigation of the rendered popup.
        send_active_key End Right Home Return
        capture "worksheet-context-clear-after.png"
        send_key ctrl+s
        for _ in $(seq 1 12); do
            after_hash="$(sha256sum "$document_path" 2>/dev/null | awk '{print $1}')"
            [[ -n "$before_hash" && "$after_hash" != "$before_hash" ]] && break
            sleep 0.25
        done
        observed="$(csv_cell_value 6 11)"
        write_artifact "worksheet-context-clear-postcondition.txt" "expected-empty=true\nobserved=$(json_escape "$observed")\nfile-hash-before=$before_hash\nfile-hash-after=$after_hash\ncell=G12"
        if [[ -n "$before_hash" && "$after_hash" != "$before_hash" ]] && wait_for_csv_cell 6 11 ""; then
            record "worksheet-context-clear-physical" "passed" "worksheet-context-clear-before.png; worksheet-context-clear-open.png; worksheet-context-clear-after.png; cell=G12; saved-value-empty=true; file-hash-changed=true" "The rendered Clear submenu was physically activated and the saved harness CSV proves G12 is empty." "$artifacts"
        else
            record "worksheet-context-clear-physical" "failed" "worksheet-context-clear-before.png; worksheet-context-clear-open.png; worksheet-context-clear-after.png; cell=G12; observed-value=$observed; file-hash-changed=$([[ -n "$before_hash" && "$after_hash" != "$before_hash" ]] && printf true || printf false)" "Clear Contents did not produce the required saved-cell and file-hash postconditions." "$artifacts"
        fi
    else
        write_artifact "worksheet-context-clear-postcondition.txt" "seeded=false\ncell=G12"
        record "worksheet-context-clear-physical" "failed" "worksheet-context-clear-postcondition.txt" "Could not seed calibrated G12 for the physical worksheet context Clear probe." "worksheet-context-clear-postcondition.txt"
    fi
    dismiss_overlays
}

probe_clipboard_roundtrips() {
    local copy_value="X11CopyPaste" cut_value="X11CutPaste"
    local clipboard="" before_hash="" after_hash="" copy_destination="" cut_destination="" cut_source=""
    local copy_artifacts="clipboard-copy-paste-before.png;clipboard-copy-paste-after.png;clipboard-copy-paste-postcondition.txt"
    local cut_artifacts="clipboard-cut-paste-before.png;clipboard-cut-paste-after.png;clipboard-cut-paste-postcondition.txt"

    if set_cell_text 6 12 G13 "$copy_value" &&
       select_cell 7 12 H13 &&
       [[ "$(csv_cell_value 7 12)" == "" ]]; then
        printf 'clipboard-sentinel' | xclip -selection clipboard -in >/dev/null 2>&1
        select_cell 6 12 G13
        capture "clipboard-copy-paste-before.png"
        send_key ctrl+c
        clipboard="$(wait_for_clipboard "$copy_value" || true)"
        select_cell 7 12 H13
        send_key ctrl+v
        capture "clipboard-copy-paste-after.png"
        send_key ctrl+s
        sleep "$dialog_settle_seconds"
        copy_destination="$(csv_cell_value 7 12)"
        write_artifact "clipboard-copy-paste-postcondition.txt" "expected=$copy_value\nclipboard=$clipboard\ndestination=H13\nsaved-destination=$copy_destination"
        if [[ "$clipboard" == "$copy_value" ]] && wait_for_csv_cell 7 12 "$copy_value"; then
            record "clipboard-copy-paste-roundtrip" "passed" "clipboard-copy-paste-before.png; clipboard-copy-paste-after.png; clipboard=$clipboard; saved-cell=H13:$copy_destination" "Physical Ctrl+C/Ctrl+V roundtrip produced the exact clipboard text and saved destination value." "$copy_artifacts"
        else
            record "clipboard-copy-paste-roundtrip" "failed" "clipboard-copy-paste-before.png; clipboard-copy-paste-after.png; clipboard=$clipboard; saved-cell=H13:$copy_destination" "Copy/paste did not satisfy the exact clipboard and saved-cell postconditions." "$copy_artifacts"
        fi
    else
        write_artifact "clipboard-copy-paste-postcondition.txt" "seeded=false\nsource=G13\ndestination=H13"
        record "clipboard-copy-paste-roundtrip" "failed" "clipboard-copy-paste-postcondition.txt" "Could not seed the copy/paste roundtrip cells." "clipboard-copy-paste-postcondition.txt"
    fi
    dismiss_overlays

    if set_cell_text 6 13 G14 "$cut_value" &&
       select_cell 7 13 H14 &&
       [[ "$(csv_cell_value 7 13)" == "" ]]; then
        printf 'clipboard-sentinel' | xclip -selection clipboard -in >/dev/null 2>&1
        before_hash="$(sha256sum "$document_path" 2>/dev/null | awk '{print $1}')"
        select_cell 6 13 G14
        capture "clipboard-cut-paste-before.png"
        send_key ctrl+x
        clipboard="$(wait_for_clipboard "$cut_value" || true)"
        select_cell 7 13 H14
        send_key ctrl+v
        capture "clipboard-cut-paste-after.png"
        send_key ctrl+s
        for _ in $(seq 1 12); do
            after_hash="$(sha256sum "$document_path" 2>/dev/null | awk '{print $1}')"
            [[ -n "$before_hash" && "$after_hash" != "$before_hash" ]] && break
            sleep 0.25
        done
        cut_destination="$(csv_cell_value 7 13)"
        cut_source="$(csv_cell_value 6 13)"
        write_artifact "clipboard-cut-paste-postcondition.txt" "expected=$cut_value\nclipboard=$clipboard\nsource=G14\ndestination=H14\nsaved-source=$(json_escape "$cut_source")\nsaved-destination=$cut_destination\nfile-hash-before=$before_hash\nfile-hash-after=$after_hash"
        if [[ "$clipboard" == "$cut_value" && "$cut_source" == "" && "$cut_destination" == "$cut_value" && -n "$before_hash" && "$after_hash" != "$before_hash" ]]; then
            record "clipboard-cut-paste-roundtrip" "passed" "clipboard-cut-paste-before.png; clipboard-cut-paste-after.png; clipboard=$clipboard; saved-source=G14:empty; saved-destination=H14:$cut_destination; file-hash-changed=true" "Physical Ctrl+X/Ctrl+V roundtrip proves the clipboard value, cleared source, destination value, and changed saved file." "$cut_artifacts"
        else
            record "clipboard-cut-paste-roundtrip" "failed" "clipboard-cut-paste-before.png; clipboard-cut-paste-after.png; clipboard=$clipboard; saved-source=G14:$cut_source; saved-destination=H14:$cut_destination; file-hash-changed=$([[ -n "$before_hash" && "$after_hash" != "$before_hash" ]] && printf true || printf false)" "Cut/paste did not satisfy the exact clipboard, source, destination, and file-hash postconditions." "$cut_artifacts"
        fi
    else
        write_artifact "clipboard-cut-paste-postcondition.txt" "seeded=false\nsource=G14\ndestination=H14"
        record "clipboard-cut-paste-roundtrip" "failed" "clipboard-cut-paste-postcondition.txt" "Could not seed the cut/paste roundtrip cells." "clipboard-cut-paste-postcondition.txt"
    fi
    dismiss_overlays
}

probe_window_management() {
    local before_count after_count active_before active_after candidate existing known
    local pre_arrange_bounds after_bounds active_after_is_created=false
    local artifacts="window-new-before.png;window-new-after.png;window-arrange-after.png;window-switch-after.png;window-bounds-before.txt;window-bounds-after-arrange.txt;window-management-postcondition.txt"
    local -a baseline_window_ids=() current_window_ids=() created_window_ids=()
    mapfile -t baseline_window_ids < <(freex_window_ids)
    before_count="${#baseline_window_ids[@]}"
    capture "window-new-before.png"
    enter_view_keytip
    keytip_key n
    keytip_key w
    after_count=0
    for _ in $(seq 1 12); do
        after_count="$(freex_window_count)"
        if (( after_count > before_count )); then
            break
        fi
        sleep 0.25
    done
    capture "window-new-after.png"
    mapfile -t current_window_ids < <(freex_window_ids)
    for candidate in "${current_window_ids[@]}"; do
        known=false
        for existing in "${baseline_window_ids[@]}"; do
            if [[ "$candidate" == "$existing" ]]; then
                known=true
                break
            fi
        done
        if ! $known; then
            created_window_ids+=("$candidate")
        fi
    done
    if (( after_count != before_count + 1 )); then
        write_artifact "window-management-postcondition.txt" "before-count=$before_count\nafter-new-count=$after_count\nnew-window=false"
        record "window-new-arrange-switch-physical" "failed" "window-new-before.png; window-new-after.png; window-management-postcondition.txt; before-count=$before_count; after-new-count=$after_count" "The rendered View key-tip route did not create exactly one additional visible workbook window." "window-new-before.png;window-new-after.png;window-management-postcondition.txt"
        for candidate in "${created_window_ids[@]}"; do
            [[ -z "$candidate" || "$candidate" == "$window_id" ]] && continue
            xdotool windowclose "$candidate" 2>/dev/null || true
        done
        dismiss_overlays
        return
    fi

    pre_arrange_bounds="$(window_bounds_signature)"
    write_artifact "window-bounds-before.txt" "$pre_arrange_bounds"
    focus_app
    enter_view_keytip
    keytip_key a
    # The Arrange All popup owns X11 focus. Navigate its rendered menu instead of
    # injecting a key-tip token back into the workbook window.
    send_active_key Home Return
    sleep "$dialog_settle_seconds"
    after_bounds="$(window_bounds_signature)"
    write_artifact "window-bounds-after-arrange.txt" "$after_bounds"
    capture "window-arrange-after.png"
    active_before="$(xdotool getactivewindow 2>/dev/null || true)"
    send_active_key ctrl+F6
    active_after="$(xdotool getactivewindow 2>/dev/null || true)"
    capture "window-switch-after.png"
    for candidate in "${created_window_ids[@]}"; do
        if [[ "$candidate" == "$active_after" ]]; then
            active_after_is_created=true
            break
        fi
    done
    write_artifact "window-management-postcondition.txt" "before-count=$before_count\nafter-new-count=$after_count\ncreated-ids=${created_window_ids[*]}\nactive-before-switch=$active_before\nactive-after-switch=$active_after\nactive-after-is-created=$active_after_is_created\nbounds-before-arrange=$pre_arrange_bounds\nbounds-after-arrange=$after_bounds"

    if window_bounds_are_valid "$after_bounds" && [[ "$after_bounds" != "$pre_arrange_bounds" ]] &&
       [[ -n "$active_after" && "$active_after" != "$active_before" && "$active_after_is_created" == true ]]; then
        record "window-new-arrange-switch-physical" "passed" "window-new-before.png; window-new-after.png; window-arrange-after.png; window-switch-after.png; window-management-postcondition.txt; visible-count=$after_count; active-window-switched=true; shared-workbook-parity=managed-behavior-tested" "Physical View key-tip New Window created one additional top-level workbook window, Arrange All changed valid bounds, and Ctrl+F6 switched to the created window. Shared-workbook model, view-state, detach, title, and close lifecycle semantics are covered by AvaloniaSharedWorkbookWindowTests." "$artifacts"
    else
        record "window-new-arrange-switch-physical" "failed" "window-new-before.png; window-new-after.png; window-arrange-after.png; window-switch-after.png; window-management-postcondition.txt; visible-count=$after_count; active-before=$active_before; active-after=$active_after" "Window management did not satisfy exact count, bounds, and active-window postconditions." "$artifacts"
    fi

    # Close only windows observed after New Window that were absent from the
    # baseline; unrelated workbook windows are never touched by this probe.
    for candidate in "${created_window_ids[@]}"; do
        [[ -z "$candidate" || "$candidate" == "$window_id" ]] && continue
        xdotool windowclose "$candidate" 2>/dev/null || true
    done
    for _ in $(seq 1 10); do
        (( $(freex_window_count) <= before_count )) && break
        sleep 0.25
    done
    focus_app
    dismiss_overlays
}

probe_cancelable_window() {
    local id="$1" keys="$2" screenshot_name="$3"
    local before="" after="" dialog_id="" opened=false closed=false
    local before_screenshot="${id}-before.png" after_screenshot="${id}-after-open.png" cancel_screenshot="${id}-after-cancel.png"
    local artifacts="${before_screenshot};${after_screenshot};${cancel_screenshot};${id}-postcondition.txt"
    before="$(visible_window_count)"
    capture "$before_screenshot"
    send_key "$keys"
    for _ in $(seq 1 8); do
        after="$(visible_window_count)"
        if (( after > before )); then
            opened=true
            break
        fi
        sleep 0.2
    done
    capture "$after_screenshot"
    if $opened; then
        dialog_id="$(xdotool getactivewindow 2>/dev/null || true)"
        if [[ -n "$dialog_id" && "$dialog_id" != "$window_id" ]]; then
            # Gtk-backed native pickers are already active, but do not process the synthetic
            # KeyPress client message xdotool emits with --window. Send Escape through the
            # active X11 focus path, which is also how the interactive user and dialog probe
            # deliver it. dialog_id still proves this is not the workbook window.
            xdotool key --clearmodifiers --delay "$input_delay_ms" Escape 2>/dev/null || true
            for _ in $(seq 1 8); do
                after="$(visible_window_count)"
                if (( after <= before )); then
                    closed=true
                    break
                fi
                sleep 0.2
            done
        fi
        capture "$cancel_screenshot"
        write_artifact "${id}-postcondition.txt" "shortcut=$keys\nwindow-count-before=$before\nwindow-count-after-open=$after\nwindow-count-after-cancel=$(visible_window_count)\ndialog-window-id=$dialog_id\nopened=$opened\nclosed=$closed"
        if $closed; then
            # A native GTK picker and Avalonia ShowDialog can remove their X11 window before the
            # owner's modal loop has unwound. The owner already reports focused at that point, but
            # consumes its first key while restoring input. Send a harmless readiness sentinel,
            # then wait for the bounded settlement boundary before the next independent shortcut.
            focus_app
            xdotool key --clearmodifiers --delay "$input_delay_ms" --window "$window_id" Escape 2>/dev/null || true
            sleep "$dialog_settle_seconds"
            record "$id" "passed" "$before_screenshot; $after_screenshot; $cancel_screenshot; dialog-window-id=$dialog_id; window-count-before=$before; window-count-after-open=$after; window-count-after-cancel=$(visible_window_count)" "The cancel-only native flow opened a distinct top-level window and Escape returned to the workbook with the original window count." "$artifacts"
        else
            record "$id" "failed" "$before_screenshot; $after_screenshot; $cancel_screenshot; dialog-window-id=$dialog_id; window-count-before=$before; window-count-after-open=$after" "$keys opened a window, but targeted Escape did not close it." "$artifacts"
            dismiss_overlays
        fi
    else
        capture "$cancel_screenshot"
        write_artifact "${id}-postcondition.txt" "shortcut=$keys\nwindow-count-before=$before\nwindow-count-after-open=$after\ndialog-window-id=$dialog_id\nopened=$opened\nclosed=$closed"
        record "$id" "failed" "$before_screenshot; $after_screenshot; $cancel_screenshot; window-count-before=$before; window-count-after-open=$after" "$keys did not open a cancelable window." "$artifacts"
        dismiss_overlays
    fi
}

if ! calibrate_geometry; then
    record "x11-geometry-calibration" "failed" "calibration-a1.png; calibration-b1.png; calibration-a2.png" "$calibration_reason"
    write_manifest
    exit 2
fi

if ! command -v xclip >/dev/null 2>&1; then
    calibration_reason="The probe image does not provide xclip for exact X11 clipboard assertions."
    record "x11-clipboard-precondition" "failed" "x11-input-results.json" "$calibration_reason"
    write_manifest
    exit 2
fi

initial_document_hash=""
if [[ -f "$document_path" ]]; then
    initial_document_hash="$(sha256sum "$document_path" | awk '{print $1}')"
fi

# F2 on G7 must create a visible inline editor, accept paced text, and restore the blank
# cell exactly when Escape cancels. Target selection is verified against calibrated geometry.
select_cell 0 0 A1
capture "inline-edit-cancel-before.png"
crop_cell "$output/inline-edit-cancel-before.png" "$output/inline-edit-cancel-before-cell.png" 6 6
if select_cell 6 6 G7; then
    send_key F2
    type_text "X11Cancel"
    capture "inline-edit-open.png"
    crop_cell "$output/inline-edit-open.png" "$output/inline-edit-open-cell.png" 6 6
    send_key Escape
    select_cell 0 0 A1 || true
    capture "inline-edit-cancelled.png"
    crop_cell "$output/inline-edit-cancelled.png" "$output/inline-edit-cancelled-cell.png" 6 6
    if region_changed "$output/inline-edit-cancel-before-cell.png" "$output/inline-edit-open-cell.png" 8 &&
       regions_match "$output/inline-edit-cancel-before-cell.png" "$output/inline-edit-cancelled-cell.png" 2; then
        record "inline-edit-f2-escape" "passed" "selection-G7.png; inline-edit-open.png; calibrated G7 cell interior restored exactly"
    else
        record "inline-edit-f2-escape" "failed" "selection-G7.png; inline-edit-open.png; inline-edit-cancelled-cell.png" "F2/Escape did not visibly enter and restore calibrated G7 exactly."
    fi
else
    record "inline-edit-f2-escape" "failed" "selection-G7.png" "Could not physically select calibrated cell G7."
fi

# G8: commit a unique inline value, move selection away, and compare the calibrated cell interior.
select_cell 0 0 A1
capture "inline-edit-commit-before.png"
crop_cell "$output/inline-edit-commit-before.png" "$output/inline-edit-commit-before-cell.png" 6 7
if select_cell 6 7 G8; then
    send_key F2
    type_text "X11InlineCommit"
    capture "inline-edit-commit-editing.png"
    send_key Return
    committed_value="$(copy_cell_formula 6 7 G8 || printf 'selection-failed')"
    send_key Escape
    select_cell 0 0 A1 || true
    capture "inline-edit-commit-after.png"
    crop_cell "$output/inline-edit-commit-after.png" "$output/inline-edit-commit-after-cell.png" 6 7
    if region_changed "$output/inline-edit-commit-before-cell.png" "$output/inline-edit-commit-after-cell.png" 8 &&
       [[ "$committed_value" == "X11InlineCommit" ]]; then
        record "inline-edit-f2-enter-commit" "passed" "selection-G8.png; inline-edit-commit-editing.png; X11 clipboard='X11InlineCommit'"
    else
        record "inline-edit-f2-enter-commit" "failed" "selection-G8.png; inline-edit-commit-after-cell.png" "F2/Enter did not commit the complete value in calibrated G8 (clipboard='${committed_value}')."
    fi
else
    record "inline-edit-f2-enter-commit" "failed" "selection-G8.png" "Could not physically select calibrated cell G8."
fi

# Ctrl+S saves the committed G8 mutation to the harness-owned CSV.
saved=false
if [[ -n "$initial_document_hash" ]]; then
    send_key ctrl+s
    for _ in $(seq 1 20); do
        current_document_hash="$(sha256sum "$document_path" | awk '{print $1}')"
        if [[ "$current_document_hash" != "$initial_document_hash" ]]; then
            saved=true
            break
        fi
        sleep 0.25
    done
fi
capture "save-ctrl-s-after.png"
if $saved; then
    record "save-ctrl-s-persist" "passed" "save-ctrl-s-after.png; $(basename "$document_path") content hash changed"
else
    record "save-ctrl-s-persist" "failed" "save-ctrl-s-after.png" "Ctrl+S did not persist the calibrated G8 edit to the harness-owned document."
fi
dismiss_overlays

# Shift+F12 is Save, not Save As. Make a second mutation so its delivery is independently
# observable as another content-hash transition on the same harness-owned document.
shift_f12_before_hash=""
shift_f12_committed=false
if select_cell 6 10 G11; then
    send_key F2
    type_text "Z"
    send_key Return
    shift_f12_value="$(copy_cell_formula 6 10 G11 || printf 'selection-failed')"
    send_key Escape
    if [[ "$shift_f12_value" == "Z" && -f "$document_path" ]]; then
        shift_f12_committed=true
        shift_f12_before_hash="$(sha256sum "$document_path" | awk '{print $1}')"
    fi
fi

shift_f12_saved=false
if $shift_f12_committed && [[ -n "$shift_f12_before_hash" ]]; then
    send_key shift+F12
    for _ in $(seq 1 20); do
        current_document_hash="$(sha256sum "$document_path" | awk '{print $1}')"
        if [[ "$current_document_hash" != "$shift_f12_before_hash" ]]; then
            shift_f12_saved=true
            break
        fi
        sleep 0.25
    done
fi
capture "save-shift-f12-after.png"
if $shift_f12_saved; then
    record "save-shift-f12-persist" "passed" "selection-G11.png; save-shift-f12-after.png; $(basename "$document_path") content hash changed"
else
    record "save-shift-f12-persist" "failed" "selection-G11.png; save-shift-f12-after.png" "Shift+F12 did not persist an independently verified G11 edit to the harness-owned document."
fi
dismiss_overlays

# G9: enter a formula inline, click calibrated B2 while point mode is active, and commit.
select_cell 0 0 A1
capture "inline-point-before.png"
crop_cell "$output/inline-point-before.png" "$output/inline-point-before-cell.png" 6 8
if select_cell 6 8 G9; then
    send_key F2
    type_text "="
    capture "inline-point-equals.png"
    crop_cell "$output/inline-point-equals.png" "$output/inline-point-b2-before.png" 1 1
    focus_app
    xdotool mousemove --sync "$(cell_center_x 1)" "$(cell_center_y 1)" click 1
    sleep "$settle_seconds"
    capture "inline-point-address.png"
    crop_cell "$output/inline-point-address.png" "$output/inline-point-b2-address.png" 1 1
    send_key Return
    inline_formula="$(copy_cell_formula 6 8 G9 || printf 'selection-failed')"
    select_cell 0 0 A1 || true
    capture "inline-point-committed.png"
    crop_cell "$output/inline-point-committed.png" "$output/inline-point-committed-cell.png" 6 8
    if region_changed "$output/inline-point-b2-before.png" "$output/inline-point-b2-address.png" 12 &&
       region_changed "$output/inline-point-before-cell.png" "$output/inline-point-committed-cell.png" 8 &&
       [[ "$inline_formula" == "=B2" ]]; then
        record "inline-point-mode-click" "passed" "selection-G9.png; inline-point-address.png; X11 clipboard formula='=B2'"
    else
        record "inline-point-mode-click" "failed" "selection-G9.png; inline-point-address.png; inline-point-committed-cell.png" "Calibrated B2 did not gain a visible point reference or commit '=B2' in G9 (clipboard='${inline_formula}')."
        dismiss_overlays
    fi
else
    record "inline-point-mode-click" "failed" "selection-G9.png" "Could not physically select calibrated cell G9."
fi

# G12: keep the inline formula editor active while a physical pointer drag extends B2 to D4.
select_cell 0 0 A1
capture "inline-point-drag-before.png"
crop_cell "$output/inline-point-drag-before.png" "$output/inline-point-drag-before-cell.png" 6 11
if select_cell 6 11 G12; then
    send_key F2
    type_text "="
    capture "inline-point-drag-equals.png"
    focus_app
    xdotool mousemove --sync "$(cell_center_x 1)" "$(cell_center_y 1)"
    xdotool mousedown 1
    xdotool mousemove --sync "$(cell_center_x 3)" "$(cell_center_y 3)"
    xdotool mouseup 1
    sleep "$settle_seconds"
    capture "inline-point-drag-address.png"
    printf 'clipboard-sentinel' | xclip -selection clipboard -in >/dev/null 2>&1
    send_key ctrl+a
    send_key ctrl+c
    inline_drag_editor_text="$(clipboard_text)"
    send_key Return
    inline_drag_formula="$(copy_cell_formula 6 11 G12 || printf 'selection-failed')"
    select_cell 0 0 A1 || true
    capture "inline-point-drag-committed.png"
    crop_cell "$output/inline-point-drag-committed.png" "$output/inline-point-drag-committed-cell.png" 6 11
    if [[ "$inline_drag_editor_text" == "=B2:D4" ]] &&
       region_changed "$output/inline-point-drag-before-cell.png" "$output/inline-point-drag-committed-cell.png" 8 &&
       [[ "$inline_drag_formula" == "=B2:D4" ]]; then
        record "inline-point-mode-drag-range" "passed" "selection-G12.png; inline-point-drag-address.png; X11 editor clipboard='=B2:D4'; committed formula='=B2:D4'"
    else
        record "inline-point-mode-drag-range" "failed" "selection-G12.png; inline-point-drag-address.png; inline-point-drag-committed-cell.png" "Physical B2-to-D4 point drag did not restore the editor or commit '=B2:D4' in G12 (editor clipboard='${inline_drag_editor_text}', committed formula='${inline_drag_formula}')."
        dismiss_overlays
    fi
else
    record "inline-point-mode-drag-range" "failed" "selection-G12.png" "Could not physically select calibrated cell G12."
fi

# G10: use the physical Ctrl+F2 formula-bar focus route, exercise F2 Edit/Point toggling,
# click calibrated B2, and commit the resulting formula.
select_cell 0 0 A1
capture "formula-point-before.png"
crop_cell "$output/formula-point-before.png" "$output/formula-point-before-cell.png" 6 9
if select_cell 6 9 G10; then
    send_key ctrl+F2
    send_key ctrl+a
    type_text "="
    send_key F2
    send_key F2
    capture "formula-point-equals.png"
    crop_cell "$output/formula-point-equals.png" "$output/formula-point-b2-before.png" 1 1
    focus_app
    xdotool mousemove --sync "$(cell_center_x 1)" "$(cell_center_y 1)" click 1
    sleep "$settle_seconds"
    capture "formula-point-address.png"
    crop_cell "$output/formula-point-address.png" "$output/formula-point-b2-address.png" 1 1
    send_key Return
    formula_bar_formula="$(copy_cell_formula 6 9 G10 || printf 'selection-failed')"
    select_cell 0 0 A1 || true
    capture "formula-point-committed.png"
    crop_cell "$output/formula-point-committed.png" "$output/formula-point-committed-cell.png" 6 9
    if region_changed "$output/formula-point-b2-before.png" "$output/formula-point-b2-address.png" 12 &&
       region_changed "$output/formula-point-before-cell.png" "$output/formula-point-committed-cell.png" 8 &&
       [[ "$formula_bar_formula" == "=B2" ]]; then
        record "formula-bar-point-mode-click" "passed" "selection-G10.png; formula-point-address.png; X11 clipboard formula='=B2'"
    else
        record "formula-bar-point-mode-click" "failed" "selection-G10.png; formula-point-address.png; formula-point-committed-cell.png" "Ctrl+F2, F2 Edit/Point toggling, and calibrated B2 click did not commit '=B2' in G10 (clipboard='${formula_bar_formula}')."
        dismiss_overlays
    fi
else
    record "formula-bar-point-mode-click" "failed" "selection-G10.png" "Could not physically select calibrated cell G10."
fi

# Standalone Alt uses an explicit held interval so the X11 key-up path cannot collapse into the
# key-down event. A broad visual assertion is sufficient because exact routing is covered in-process.
select_cell 0 0 A1 || true
capture "alt-before.png"
focus_app
xdotool keydown --clearmodifiers --window "$window_id" Alt_L
sleep 0.18
xdotool keyup --window "$window_id" Alt_L
sleep "$settle_seconds"
capture "alt-after.png"
if screen_changed "$output/alt-before.png" "$output/alt-after.png" 500; then
    record "keytips-alt" "passed" "alt-before.png -> alt-after.png"
else
    record "keytips-alt" "failed" "alt-before.png -> alt-after.png" "Paced Alt press/release produced no visible keytip change."
fi
dismiss_overlays

capture "f10-before.png"
send_key F10
capture "f10-after.png"
if screen_changed "$output/f10-before.png" "$output/f10-after.png" 500; then
    record "keytips-f10" "passed" "f10-before.png -> f10-after.png"
else
    record "keytips-f10" "failed" "f10-before.png -> f10-after.png" "Paced F10 produced no visible keytip change."
fi
dismiss_overlays

# Keyboard and pointer context entry target the same calibrated B2 cell.
if select_cell 1 1 B2; then
    capture "context-keyboard-before.png"
    send_key shift+F10
    capture "context-keyboard-after.png"
    if screen_changed "$output/context-keyboard-before.png" "$output/context-keyboard-after.png" 1000; then
        record "worksheet-context-shift-f10" "passed" "selection-B2.png; context-keyboard-after.png"
    else
        record "worksheet-context-shift-f10" "failed" "selection-B2.png; context-keyboard-after.png" "Shift+F10 produced no visible context menu for calibrated B2."
    fi
    dismiss_overlays

    select_cell 1 1 B2 || true
    capture "context-pointer-before.png"
    focus_app
    xdotool mousemove --sync "$(cell_center_x 1)" "$(cell_center_y 1)" click 3
    sleep "$settle_seconds"
    capture "context-pointer-after.png"
    if screen_changed "$output/context-pointer-before.png" "$output/context-pointer-after.png" 1000; then
        record "worksheet-context-right-click" "passed" "selection-B2.png; context-pointer-after.png"
    else
        record "worksheet-context-right-click" "failed" "selection-B2.png; context-pointer-after.png" "Right-click produced no visible context menu for calibrated B2."
    fi
    dismiss_overlays
else
    record "worksheet-context-shift-f10" "failed" "selection-B2.png" "Could not physically select calibrated B2."
    record "worksheet-context-right-click" "failed" "selection-B2.png" "Could not physically select calibrated B2."
fi

# The worksheet context menu is an Avalonia-rendered popup, not an application NativeMenu. These
# probes credit only physical X11 navigation of that popup and assert the command's real clipboard
# or saved-cell effect; application-level NativeMenuItem activation remains an explicit managed-lane
# boundary in the exhaustive report.
probe_worksheet_context_copy
probe_worksheet_context_clear
probe_clipboard_roundtrips
probe_window_management

# Real shortcut-to-dialog path, followed by paced focus traversal and Escape cancellation.
send_key ctrl+1
dialog_id=""
for _ in $(seq 1 8); do
    dialog_id="$(xdotool search --onlyvisible --name 'Format Cells' 2>/dev/null | tail -1 || true)"
    [[ -n "$dialog_id" ]] && break
    sleep 0.2
done
if [[ -n "$dialog_id" ]]; then
    capture "dialog-format-cells-open.png"
    xdotool key --clearmodifiers --delay "$input_delay_ms" --window "$dialog_id" Tab Tab shift+Tab
    sleep "$settle_seconds"
    capture "dialog-format-cells-tabbed.png"
    xdotool key --clearmodifiers --delay "$input_delay_ms" Escape
    sleep "$settle_seconds"
    if [[ -z "$(xdotool search --onlyvisible --name 'Format Cells' 2>/dev/null | tail -1 || true)" ]]; then
        record "dialog-format-cells-keyboard" "passed" "dialog-format-cells-open.png; dialog-format-cells-tabbed.png"
    else
        record "dialog-format-cells-keyboard" "failed" "dialog-format-cells-tabbed.png" "Escape did not close Format Cells."
        dismiss_overlays
    fi
else
    record "dialog-format-cells-keyboard" "failed" "dialog-format-cells-open.png" "Ctrl+1 did not open Format Cells."
fi

# Native file and print boundaries are cancel-only. Probe Print Preview first so each shortcut is
# independent of GTK's picker modal loop rather than treating one picker's unwind as another
# command's precondition.
probe_cancelable_window "print-preview-ctrl-shift-f12-cancel" "ctrl+shift+F12" "print-preview.png"
probe_cancelable_window "native-save-as-f12-cancel" "F12" "native-save-as.png"
probe_cancelable_window "native-open-ctrl-f12-cancel" "ctrl+F12" "native-open.png"

write_manifest
if (( $(printf '%s\n' "${results[@]}" | grep -c '"status":"failed"' || true) > 0 )); then
    exit 1
fi
