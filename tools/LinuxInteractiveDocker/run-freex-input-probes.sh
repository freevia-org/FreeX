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

record() {
    local id="$1" status="$2" evidence="$3" note="${4:-}"
    results+=("{\"id\":\"$(json_escape "$id")\",\"category\":\"x11-input\",\"status\":\"$status\",\"evidenceLevel\":\"physical-x11-input\",\"evidence\":\"$(json_escape "$evidence")\",\"note\":\"$(json_escape "$note")\"}")
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

window_id="$(xdotool search --onlyvisible --name '^FreeX' 2>/dev/null | tail -1 || true)"
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

probe_cancelable_window() {
    local id="$1" keys="$2" screenshot_name="$3"
    local before after dialog_id opened=false closed=false
    before="$(visible_window_count)"
    send_key "$keys"
    for _ in $(seq 1 8); do
        after="$(visible_window_count)"
        if (( after > before )); then
            opened=true
            break
        fi
        sleep 0.2
    done
    capture "$screenshot_name"
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
        if $closed; then
            # A native GTK picker and Avalonia ShowDialog can remove their X11 window before the
            # owner's modal loop has unwound. The owner already reports focused at that point, but
            # consumes its first key while restoring input. Send a harmless readiness sentinel,
            # then wait for the bounded settlement boundary before the next independent shortcut.
            focus_app
            xdotool key --clearmodifiers --delay "$input_delay_ms" --window "$window_id" Escape 2>/dev/null || true
            sleep "$dialog_settle_seconds"
            record "$id" "passed" "$screenshot_name"
        else
            record "$id" "failed" "$screenshot_name" "$keys opened a window, but targeted Escape did not close it."
            dismiss_overlays
        fi
    else
        record "$id" "failed" "$screenshot_name" "$keys did not open a cancelable window."
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
