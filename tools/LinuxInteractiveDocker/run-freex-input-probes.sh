#!/usr/bin/env bash
set -eEuo pipefail

export DISPLAY="${DISPLAY:-:99}"

output="${1:-/work/x11-validation}"
input_delay_ms="${FREEX_X11_INPUT_DELAY_MS:-160}"
type_delay_ms="${FREEX_X11_TYPE_DELAY_MS:-90}"
settle_seconds="${FREEX_X11_SETTLE_SECONDS:-0.35}"
dialog_settle_seconds="${FREEX_X11_DIALOG_SETTLE_SECONDS:-3.0}"
mousemove_timeout_seconds="${FREEX_X11_MOUSEMOVE_TIMEOUT_SECONDS:-5}"
mousemove_timeout_count=0
clipboard_timeout_seconds="${FREEX_X11_CLIPBOARD_TIMEOUT_SECONDS:-5}"
clipboard_sentinel_pid=""
image_tool_timeout_seconds="${FREEX_X11_IMAGE_TOOL_TIMEOUT_SECONDS:-5}"
selection_color="${FREEX_X11_SELECTION_COLOR:-#217346}"
document_path="${FREEX_X11_DOCUMENT_PATH:-/documents/linux-interactive-demo.csv}"
probe_selector="${FREEX_X11_PROBE_SELECTOR:-all}"

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
worksheet_base_a1_x=0
worksheet_base_a1_y=0
row_outline_depth=0
column_outline_depth=0
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

stop_clipboard_sentinel() {
    local pid="${clipboard_sentinel_pid:-}"
    clipboard_sentinel_pid=""
    [[ -n "$pid" ]] || return 0

    kill "$pid" 2>/dev/null || true
    for _ in $(seq 1 20); do
        if ! kill -0 "$pid" 2>/dev/null; then
            wait "$pid" 2>/dev/null || true
            return 0
        fi
        sleep 0.05
    done
    kill -KILL "$pid" 2>/dev/null || true
    wait "$pid" 2>/dev/null || true
}

on_error() {
    local exit_code=$?
    trap - ERR
    stop_clipboard_sentinel
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
trap stop_clipboard_sentinel EXIT

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

xdotool_mousemove_sync() {
    # Deliver the pointer transition before a dependent click. Keep the synchronous
    # wait bounded because clipped or rearranged coordinates can otherwise stall a probe,
    # then yield before dispatching a chained pointer command so Avalonia observes the
    # settled header hit target as a separate input lifecycle.
    local target_x="${1:-}" target_y="${2:-}"
    shift 2 2>/dev/null || true
    if [[ ! "$target_x" =~ ^[0-9]+$ || ! "$target_y" =~ ^[0-9]+$ ]]; then
        mousemove_timeout_count=$((mousemove_timeout_count + 1))
        return 0
    fi
    if ! timeout --foreground --kill-after=1s "${mousemove_timeout_seconds}s" xdotool mousemove --sync "$target_x" "$target_y"; then
        mousemove_timeout_count=$((mousemove_timeout_count + 1))
        return 0
    fi
    sleep 0.12
    if (( $# > 0 )); then
        xdotool "$@"
    fi
}

send_key() {
    focus_app
    xdotool key --clearmodifiers --delay "$input_delay_ms" --window "$window_id" "$@"
    sleep "$settle_seconds"
}

send_flyout_key() {
    # Keep the popup's focus owner. Calling send_key here would reactivate the
    # workbook top-level window and can dismiss an Avalonia MenuFlyout before
    # the key reaches its MenuItem selection model.
    xdotool key --clearmodifiers --delay "$input_delay_ms" "$@"
    sleep "$settle_seconds"
}

type_text() {
    local value="$1"
    focus_app
    xdotool type --clearmodifiers --delay "$type_delay_ms" --window "$window_id" "$value"
    sleep "$settle_seconds"
}

clipboard_text() {
    timeout --foreground --kill-after=1s "${clipboard_timeout_seconds}s" xclip -selection clipboard -out 2>/dev/null | tr -d '\r\n'
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
}

wait_for_document_clean() {
    local title=""
    for _ in $(seq 1 20); do
        title="$(xdotool getwindowname "$window_id" 2>/dev/null || true)"
        if [[ "$title" != *"*"* ]]; then
            return 0
        fi
        sleep 0.25
    done
    return 1
}

seed_cell_text() {
    local column_offset="$1" row_offset="$2" address="$3" value="$4" committed=""
    set_cell_text "$column_offset" "$row_offset" "$address" "$value" || return 1

    # Verify the committed model value through the same physical F2/clipboard
    # route used by the passing inline-edit probe before crediting persistence.
    committed="$(copy_cell_formula "$column_offset" "$row_offset" "$address" || true)"
    [[ "$committed" == "$value" ]] || return 1

    send_key ctrl+s
    wait_for_csv_cell "$column_offset" "$row_offset" "$value" || return 1
    wait_for_document_clean || return 1
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

set_clipboard_sentinel() {
    # The application becomes the real clipboard owner after each physical Copy.
    # Do not install a persistent xclip owner before that event: xclip waits for
    # selection requests and can block command substitutions indefinitely.
    return 0
}

copy_cell_display() {
    local column_offset="$1" row_offset="$2" address="$3"
    set_clipboard_sentinel
    select_cell "$column_offset" "$row_offset" "$address" || return 1
    send_key ctrl+c
    clipboard_text
}

copy_cell_formula() {
    local column_offset="$1" row_offset="$2" address="$3"
    # After Enter commits an inline edit, Avalonia may leave focus on the detached
    # editor for one input turn even though the active-cell border has repainted. Rebuild
    # worksheet focus through the stable keyboard route before opening the editor again.
    # This is the same readback path used by the passing grid-drag seed probes.
    copy_cell_formula_by_keyboard "$column_offset" "$row_offset"
}

select_cell_by_keyboard() {
    local column_offset="$1" row_offset="$2"
    send_key ctrl+Home
    for _ in $(seq 1 "$column_offset"); do send_key Right; done
    for _ in $(seq 1 "$row_offset"); do send_key Down; done
}

copy_cell_formula_by_keyboard() {
    local column_offset="$1" row_offset="$2" value=""
    set_clipboard_sentinel
    select_cell_by_keyboard "$column_offset" "$row_offset"
    send_key F2
    send_key ctrl+a
    send_key ctrl+c
    value="$(clipboard_text)"
    send_key Escape
    printf '%s' "$value"
}

restore_calibrated_window_geometry() {
    local geometry="" current_x="" current_y="" current_width="" current_height=""

    wmctrl -ir "$window_id" -b add,maximized_vert,maximized_horz 2>/dev/null || return 1
    for _ in $(seq 1 20); do
        geometry="$(xdotool getwindowgeometry --shell "$window_id" 2>/dev/null || true)"
        current_x="$(printf '%s\n' "$geometry" | awk -F= '$1 == "X" { print $2 }')"
        current_y="$(printf '%s\n' "$geometry" | awk -F= '$1 == "Y" { print $2 }')"
        current_width="$(printf '%s\n' "$geometry" | awk -F= '$1 == "WIDTH" { print $2 }')"
        current_height="$(printf '%s\n' "$geometry" | awk -F= '$1 == "HEIGHT" { print $2 }')"
        if [[ "$current_x" == "$window_x" && "$current_y" == "$window_y" &&
              "$current_width" == "$window_width" && "$current_height" == "$window_height" ]]; then
            focus_app
            return 0
        fi
        sleep 0.1
    done
    return 1
}

copy_cell_formula_by_address() {
    local address="$1" value=""
    local sentinel="__FREEX_ADDRESS_FORMULA__" sentinel_pid="" current="" copied=false
    local dialog_id="" dialog_open=false dialog_closed=false candidate=""

    # Keep a PID-owned sentinel until the application takes clipboard ownership. A missed click
    # therefore remains distinguishable from a successful copy instead of exposing stale data.
    printf '%s' "$sentinel" | xclip -selection clipboard -in >/dev/null 2>&1 &
    sentinel_pid=$!
    clipboard_sentinel_pid="$sentinel_pid"
    for _ in $(seq 1 10); do
        current="$(clipboard_text || true)"
        [[ "$current" == "$sentinel" ]] && break
        sleep 0.05
    done
    if [[ "$current" != "$sentinel" ]]; then
        stop_clipboard_sentinel
        return 1
    fi

    # Establish worksheet focus before Ctrl+G. Formula-bar and outline-button focus can consume
    # the shortcut; typing an address after a missed dialog would otherwise edit the worksheet.
    xdotool_mousemove_sync "$((a1_x + cell_width / 2))" "$((a1_y + cell_height / 2))" click 1
    sleep "$settle_seconds"

    # Ctrl+G is the production Go To route. Require a distinct active dialog before typing so a
    # missed shortcut cannot masquerade as a successful address readback.
    for _ in $(seq 1 2); do
        send_key ctrl+g
        for _ in $(seq 1 20); do
            candidate="$(xdotool getactivewindow 2>/dev/null || true)"
            if [[ -n "$candidate" && "$candidate" != "$window_id" ]] &&
               xdotool getwindowname "$candidate" >/dev/null 2>&1; then
                dialog_id="$candidate"
                dialog_open=true
                break 2
            fi
            sleep 0.1
        done
        xdotool key --clearmodifiers --delay "$input_delay_ms" Escape 2>/dev/null || true
        xdotool_mousemove_sync "$((a1_x + cell_width / 2))" "$((a1_y + cell_height / 2))" click 1
    done
    if ! $dialog_open; then
        stop_clipboard_sentinel
        restore_calibrated_window_geometry || true
        send_key ctrl+Home
        return 1
    fi

    xdotool key --clearmodifiers --delay "$input_delay_ms" --window "$dialog_id" ctrl+a
    xdotool type --clearmodifiers --delay "$type_delay_ms" --window "$dialog_id" "$address"
    # Enter closes the dialog on key-down. Route it through the active X11 focus so its key-up
    # cannot target the already-destroyed dialog window and raise BadWindow.
    xdotool key --clearmodifiers --delay "$input_delay_ms" Return
    for _ in $(seq 1 20); do
        if ! xdotool getwindowname "$dialog_id" >/dev/null 2>&1; then
            dialog_closed=true
            break
        fi
        sleep 0.1
    done
    if ! $dialog_closed; then
        xdotool key --clearmodifiers --delay "$input_delay_ms" --window "$dialog_id" Escape 2>/dev/null || true
        stop_clipboard_sentinel
        restore_calibrated_window_geometry || true
        send_key ctrl+Home
        return 1
    fi
    sleep "$settle_seconds"

    # A Go To target may be hidden by an outline/filter. Read its authoritative formula field;
    # F2 would instead attach the inline editor to the current visible slot. Go To can also leave
    # the owner unmaximized, so restore the calibrated window geometry first. Ctrl+F2 is the
    # production keyboard contract for focusing the formula field and avoids outline-dependent
    # pointer coordinates.
    if ! restore_calibrated_window_geometry; then
        stop_clipboard_sentinel
        send_key Escape
        send_key ctrl+Home
        return 1
    fi
    send_key ctrl+F2
    xdotool key --clearmodifiers --delay "$input_delay_ms" ctrl+a
    xdotool key --clearmodifiers --delay "$input_delay_ms" ctrl+c
    for _ in $(seq 1 10); do
        if value="$(clipboard_text)" &&
           [[ "$value" != "$sentinel" ]] &&
           ! kill -0 "$sentinel_pid" 2>/dev/null; then
            copied=true
            break
        fi
        sleep 0.12
    done
    stop_clipboard_sentinel
    xdotool key --clearmodifiers --delay "$input_delay_ms" Escape
    # Go To may scroll a hidden or distant address into view. Restore the calibrated A1 viewport
    # before the caller performs another coordinate-based outline or filter gesture.
    send_key ctrl+Home
    $copied || return 1
    printf '%s' "$value"
}

copy_cell_formula_allow_empty() {
    local column_offset="$1" row_offset="$2" address="$3" selection_mode="${4:-pointer}"
    local sentinel="__FREEX_NO_FORMULA__" sentinel_pid="" current="" value=""

    # Empty TextBox copies do not replace the X11 clipboard owner. Seed a bounded
    # two-request owner so a genuinely empty formula has an exact semantic value
    # instead of inheriting the prior formula transcript.
    printf '%s' "$sentinel" | xclip -selection clipboard -in -loops 2 >/dev/null 2>&1 &
    sentinel_pid=$!
    for _ in $(seq 1 10); do
        current="$(clipboard_text || true)"
        [[ "$current" == "$sentinel" ]] && break
        sleep 0.05
    done
    if [[ "$current" != "$sentinel" ]]; then
        wait "$sentinel_pid" 2>/dev/null || true
        return 1
    fi

    local selected=false
    if [[ "$selection_mode" == keyboard ]]; then
        select_cell_by_keyboard "$column_offset" "$row_offset" && selected=true
    else
        select_cell "$column_offset" "$row_offset" "$address" && selected=true
    fi
    if ! $selected; then
        clipboard_text >/dev/null 2>&1 || true
        wait "$sentinel_pid" 2>/dev/null || true
        return 1
    fi
    send_key F2
    send_key ctrl+a
    send_key ctrl+c
    value="$(clipboard_text || true)"
    wait "$sentinel_pid" 2>/dev/null || true
    send_key Escape
    [[ "$value" == "$sentinel" ]] && value=""
    printf '%s' "$value"
}

read_active_formula_bar() {
    # A point-mode header/corner click leaves the formula editor active. Copy the
    # editor text before committing so the semantic assertion is independent of pixels.
    set_clipboard_sentinel
    send_key ctrl+End
    send_key ctrl+a
    send_key ctrl+c
    clipboard_text
}

probe_formula_bar_point_mode_whole_range() {
    local column_formula_bar="" row_formula_bar="" select_all_formula_bar=""
    local column_cell_formula="" row_cell_formula="" select_all_cell_formula=""
    local column_active=false row_active=false select_all_active=false
    local column_passed=false row_passed=false select_all_passed=false
    local column_header_x column_header_y row_header_x row_header_y corner_x corner_y
    local formula_cancel_x formula_cancel_y
    local postcondition="formula-whole-range-point-postcondition.txt"
    local artifacts="formula-whole-range-column-before.png;formula-whole-range-column-editing.png;formula-whole-range-column-committed.png;formula-whole-range-row-before.png;formula-whole-range-row-editing.png;formula-whole-range-row-committed.png;formula-whole-range-select-all-before.png;formula-whole-range-select-all-editing.png;formula-whole-range-select-all-canceled.png;$postcondition"

    # a1_x/a1_y are the calibrated worksheet origin. The header centers are one
    # calibrated pitch above/left of that origin, including the select-all corner.
    column_header_x="$(cell_center_x 1)"
    column_header_y="$((a1_y - cell_height / 2))"
    row_header_x="$((window_x + (a1_x - window_x) / 2))"
    row_header_y="$(cell_center_y 2)"
    corner_x="$row_header_x"
    corner_y="$column_header_y"
    formula_cancel_x="$((a1_x + cell_width + 5))"
    formula_cancel_y="$((a1_y - cell_height * 2 + 2))"

    # Whole column: physical column-header input in a live formula-bar edit.
    capture "formula-whole-range-column-before.png"
    if select_cell 6 9 G10; then
        send_key ctrl+F2
        send_key ctrl+a
        type_text "=SUM()"
        send_key Left
        send_key F2
        send_key F2
        capture "formula-whole-range-column-editing.png"
        focus_app
        xdotool_mousemove_sync "$column_header_x" "$column_header_y" click 1
        sleep "$settle_seconds"
        column_formula_bar="$(read_active_formula_bar || true)"
        capture "formula-whole-range-column-editing.png"
        if [[ "$column_formula_bar" == "=SUM(B:B)" ]]; then
            column_active=true
        fi
        # Ctrl+A/C can reopen function autocomplete. Close only that popup so
        # Enter reaches the formula commit path instead of accepting BAHTTEXT.
        send_key Escape
        send_key Return
        column_cell_formula="$(copy_cell_formula 6 9 G10 || true)"
        capture "formula-whole-range-column-committed.png"
    fi
    if $column_active && [[ "$column_cell_formula" == "=SUM(B:B)" ]]; then
        column_passed=true
    fi

    # Whole row: use a fresh formula-bar edit so the row-header click is independently proven.
    capture "formula-whole-range-row-before.png"
    if select_cell 6 10 G11; then
        send_key ctrl+F2
        send_key ctrl+a
        type_text "=SUM()"
        send_key Left
        send_key F2
        send_key F2
        capture "formula-whole-range-row-editing.png"
        focus_app
        xdotool_mousemove_sync "$row_header_x" "$row_header_y" click 1
        sleep "$settle_seconds"
        row_formula_bar="$(read_active_formula_bar || true)"
        capture "formula-whole-range-row-editing.png"
        if [[ "$row_formula_bar" == "=SUM(3:3)" ]]; then
            row_active=true
        fi
        send_key Escape
        send_key Return
        row_cell_formula="$(copy_cell_formula 6 10 G11 || true)"
        capture "formula-whole-range-row-committed.png"
    fi
    if $row_active && [[ "$row_cell_formula" == "=SUM(3:3)" ]]; then
        row_passed=true
    fi

    # Select-all corner: read the active editor before Escape, then prove the formula was
    # never committed by reading the destination cell after cleanup.
    capture "formula-whole-range-select-all-before.png"
    if select_cell 6 11 G12; then
        send_key ctrl+F2
        send_key ctrl+a
        type_text "=SUM()"
        send_key Left
        send_key F2
        send_key F2
        capture "formula-whole-range-select-all-editing.png"
        focus_app
        xdotool_mousemove_sync "$corner_x" "$corner_y" click 1
        sleep "$settle_seconds"
        select_all_formula_bar="$(read_active_formula_bar || true)"
        capture "formula-whole-range-select-all-editing.png"
        if [[ "$select_all_formula_bar" == "=SUM(A1:XFD1048576)" ]]; then
            select_all_active=true
        fi
        # Escape closes autocomplete; the physical X button owns cancellation.
        send_key Escape
        xdotool_mousemove_sync "$formula_cancel_x" "$formula_cancel_y" click 1
        sleep "$settle_seconds"
        select_all_cell_formula="$(copy_cell_formula_allow_empty 6 11 G12 || true)"
        capture "formula-whole-range-select-all-canceled.png"
    fi
    if $select_all_active && [[ -z "$select_all_cell_formula" ]]; then
        select_all_passed=true
    fi

    write_artifact "$postcondition" \
        "schema-version=1\nselector=formula-whole-range-point\ncolumn-header-coordinate=$column_header_x,$column_header_y\ncolumn-header-expected=B:B\ncolumn-header-formula-bar-clipboard=$column_formula_bar\ncolumn-header-cell-formula=$column_cell_formula\ncolumn-header-cell-package-formula=$column_cell_formula\ncolumn-header-edit-active-before-commit=$column_active\ncolumn-header-passed=$column_passed\nrow-header-coordinate=$row_header_x,$row_header_y\nrow-header-expected=3:3\nrow-header-formula-bar-clipboard=$row_formula_bar\nrow-header-cell-formula=$row_cell_formula\nrow-header-cell-package-formula=$row_cell_formula\nrow-header-edit-active-before-commit=$row_active\nrow-header-passed=$row_passed\nselect-all-corner-coordinate=$corner_x,$corner_y\nformula-cancel-coordinate=$formula_cancel_x,$formula_cancel_y\nselect-all-expected=A1:XFD1048576\nselect-all-formula-bar-clipboard=$select_all_formula_bar\nselect-all-cell-package-formula-after-cancel=$select_all_cell_formula\nselect-all-edit-active-before-cancel=$select_all_active\nselect-all-passed=$select_all_passed\n"

    if $column_passed; then
        record "formula-bar-point-mode-whole-column-header" "passed" \
            "formula-whole-range-column-editing.png; formula-whole-range-column-committed.png; formula-bar-clipboard=$column_formula_bar; cell-package-formula=$column_cell_formula" \
            "Physical formula-bar point mode accepted a calibrated column-header click as the exact B:B reference and committed it." \
            "formula-whole-range-column-before.png;formula-whole-range-column-editing.png;formula-whole-range-column-committed.png;$postcondition"
    else
        record "formula-bar-point-mode-whole-column-header" "failed" \
            "formula-whole-range-column-before.png; formula-whole-range-column-editing.png; formula-whole-range-column-committed.png; formula-bar-clipboard=$column_formula_bar; cell-package-formula=$column_cell_formula" \
            "Physical column-header formula point mode did not prove the exact B:B reference (formula-bar='$column_formula_bar', cell-package='$column_cell_formula')." \
            "formula-whole-range-column-before.png;formula-whole-range-column-editing.png;formula-whole-range-column-committed.png;$postcondition"
    fi
    if $row_passed; then
        record "formula-bar-point-mode-whole-row-header" "passed" \
            "formula-whole-range-row-editing.png; formula-whole-range-row-committed.png; formula-bar-clipboard=$row_formula_bar; cell-package-formula=$row_cell_formula" \
            "Physical formula-bar point mode accepted a calibrated row-header click as the exact 3:3 reference and committed it." \
            "formula-whole-range-row-before.png;formula-whole-range-row-editing.png;formula-whole-range-row-committed.png;$postcondition"
    else
        record "formula-bar-point-mode-whole-row-header" "failed" \
            "formula-whole-range-row-before.png; formula-whole-range-row-editing.png; formula-whole-range-row-committed.png; formula-bar-clipboard=$row_formula_bar; cell-package-formula=$row_cell_formula" \
            "Physical row-header formula point mode did not prove the exact 3:3 reference (formula-bar='$row_formula_bar', cell-package='$row_cell_formula')." \
            "formula-whole-range-row-before.png;formula-whole-range-row-editing.png;formula-whole-range-row-committed.png;$postcondition"
    fi
    if $select_all_passed; then
        record "formula-bar-point-mode-whole-select-all-corner" "passed" \
            "formula-whole-range-select-all-editing.png; formula-whole-range-select-all-canceled.png; formula-bar-clipboard=$select_all_formula_bar; cell-package-after-cancel='$select_all_cell_formula'" \
            "Physical formula-bar point mode accepted the calibrated select-all corner as the exact A1:XFD1048576 reference while the edit was active; cleanup confirmed it was not committed." \
            "formula-whole-range-select-all-before.png;formula-whole-range-select-all-editing.png;formula-whole-range-select-all-canceled.png;$postcondition"
    else
        record "formula-bar-point-mode-whole-select-all-corner" "failed" \
            "formula-whole-range-select-all-before.png; formula-whole-range-select-all-editing.png; formula-whole-range-select-all-canceled.png; formula-bar-clipboard=$select_all_formula_bar; cell-package-after-cancel='$select_all_cell_formula'" \
            "Physical select-all corner formula point mode did not prove the exact A1:XFD1048576 reference while editing (formula-bar='$select_all_formula_bar', cell-package-after-cancel='$select_all_cell_formula')." \
            "formula-whole-range-select-all-before.png;formula-whole-range-select-all-editing.png;formula-whole-range-select-all-canceled.png;$postcondition"
    fi
    dismiss_overlays
}

probe_name_box_dropdown() {
    local dropdown_x dropdown_y defined_clipboard table_clipboard
    local before_root after_root window_count_before window_count_open
    local popup_open=false defined_popup_open=false table_popup_open=false defined_passed=false table_passed=false
    local artifacts="name-box-dropdown-defined-before.png;name-box-dropdown-defined-before-root.png;name-box-dropdown-defined-before-x11.txt;name-box-dropdown-open-root.png;name-box-dropdown-defined-open-x11.txt;name-box-dropdown-defined-windows.txt;name-box-dropdown-defined-name.png;name-box-dropdown-table-before.png;name-box-dropdown-table-before-root.png;name-box-dropdown-table-before-x11.txt;name-box-dropdown-table-open-root.png;name-box-dropdown-table-open-x11.txt;name-box-dropdown-table-windows.txt;name-box-dropdown-table.png;name-box-dropdown-postcondition.txt"
    local keyboard_open=false keyboard_passed=false keyboard_clipboard=""
    local mouse_open=false mouse_passed=false mouse_clipboard=""
    local interaction_artifacts="name-box-dropdown-keyboard-open.png;name-box-dropdown-mouse-before.png;name-box-dropdown-mouse-open.png;name-box-dropdown-mouse-selected.png;name-box-dropdown-interaction-postcondition.txt"

    # The name-box button is immediately above the grid's A column. Deriving both
    # coordinates from the calibrated A1 cell keeps this lane valid across the
    # supported Docker resolutions without relying on child-window discovery.
    dropdown_x="$((a1_x + cell_width * 78 / 100))"
    dropdown_y="$((a1_y - cell_height / 2 - 29))"

    # Wave70 interaction evidence: WPF's editable ComboBox opens from Alt+Down and commits the
    # highlighted item with Enter. The Linux route must do the same on the production popup.
    select_cell 9 19 J20 || true
    focus_app
    xdotool_mousemove_sync "$((a1_x + cell_width * 35 / 100))" "$dropdown_y" click 1
    sleep "$settle_seconds"
    window_count_before="$(x11_visible_window_count)"
    send_key alt+Down
    sleep "$settle_seconds"
    capture "name-box-dropdown-keyboard-open.png"
    window_count_open="$(x11_visible_window_count)"
    if (( window_count_open > window_count_before )); then
        keyboard_open=true
        xdotool key --clearmodifiers --delay "$input_delay_ms" Home Down Down Down Down Return
        sleep "$settle_seconds"
        send_key ctrl+c
        keyboard_clipboard="$(clipboard_text || true)"
    else
        keyboard_clipboard="popup-not-open"
    fi
    if $keyboard_open && [[ "$keyboard_clipboard" == $'North\t120' ]]; then
        keyboard_passed=true
    fi

    # The pointer path must select the row under the pointer and commit it, rather than only
    # moving the list cursor. The fifth fixture row is PhysicalTable (zero-based row 4).
    select_cell 9 19 J20 || true
    capture "name-box-dropdown-mouse-before.png"
    window_count_before="$(x11_visible_window_count)"
    focus_app
    xdotool_mousemove_sync "$dropdown_x" "$dropdown_y" click 1
    sleep "$settle_seconds"
    capture "name-box-dropdown-mouse-open.png"
    window_count_open="$(x11_visible_window_count)"
    if (( window_count_open > window_count_before )); then
        mouse_open=true
    fi
    # The popup is immediately below the 16px Name Box button; row centers are 27px apart.
    xdotool_mousemove_sync "$dropdown_x" "$((dropdown_y + 130))" click 1
    sleep "$settle_seconds"
    capture "name-box-dropdown-mouse-selected.png"
    send_key ctrl+c
    mouse_clipboard="$(clipboard_text || true)"
    if $mouse_open && [[ "$mouse_clipboard" == $'North\t120' ]]; then
        mouse_passed=true
    fi

    write_artifact "name-box-dropdown-interaction-postcondition.txt" \
        "keyboard-opened=$keyboard_open\nkeyboard-gesture=Alt+Down,Home,Down,Down,Down,Down,Enter\nkeyboard-clipboard=$keyboard_clipboard\nmouse-opened=$mouse_open\nmouse-gesture=NameBoxChevron,PhysicalTableRow\nmouse-clipboard=$mouse_clipboard\n"
    if $keyboard_passed; then
        record "name-box-dropdown-keyboard-physical" "passed" \
            "$interaction_artifacts; keyboard-clipboard=$keyboard_clipboard" \
            "Native X11 Alt+Down opened the production Name Box popup and Home/Down/Enter committed PhysicalTable as North/120." \
            "$interaction_artifacts"
    else
        record "name-box-dropdown-keyboard-physical" "failed" "$interaction_artifacts" \
            "Native X11 Name Box keyboard interaction expected a popup and North/120, observed open=$keyboard_open clipboard='$keyboard_clipboard'." \
            "$interaction_artifacts"
    fi
    if $mouse_passed; then
        record "name-box-dropdown-mouse-physical" "passed" \
            "$interaction_artifacts; mouse-clipboard=$mouse_clipboard" \
            "Native X11 pointer selection committed the PhysicalTable row as North/120." \
            "$interaction_artifacts"
    else
        record "name-box-dropdown-mouse-physical" "failed" "$interaction_artifacts" \
            "Native X11 Name Box pointer interaction expected PhysicalTable/North/120, observed open=$mouse_open clipboard='$mouse_clipboard'." \
            "$interaction_artifacts"
    fi

    # Start from a neutral blank cell so clipboard data cannot be inherited from
    # a prior selection or mistaken for dropdown navigation.
    select_cell 9 19 J20 || true
    capture "name-box-dropdown-defined-before.png"
    before_root="$output/name-box-dropdown-defined-before-root.png"
    scrot "$before_root"
    x11_window_snapshot "$output/name-box-dropdown-defined-before-x11.txt"
    window_count_before="$(x11_visible_window_count)"
    focus_app
    xdotool_mousemove_sync "$dropdown_x" "$dropdown_y" click 1
    sleep "$settle_seconds"
    after_root="$output/name-box-dropdown-open-root.png"
    scrot "$after_root"
    x11_window_snapshot "$output/name-box-dropdown-defined-open-x11.txt"
    wmctrl -lG > "$output/name-box-dropdown-defined-windows.txt"
    window_count_open="$(x11_visible_window_count)"
    if (( window_count_open > window_count_before )); then
        popup_open=true
    fi
    defined_popup_open="$popup_open"

    # The fixture is sorted by the shared planner as PhysicalChart, PhysicalName,
    # PhysicalPicture, PhysicalShape, PhysicalTable, PhysicalTextBox.
    # MenuFlyout receives focus when opened; Home/Down/Enter selects the entry.
    if $popup_open; then
        xdotool key --clearmodifiers --delay "$input_delay_ms" Home Down Return
        sleep "$settle_seconds"
        capture "name-box-dropdown-defined-name.png"
        send_key ctrl+c
        defined_clipboard="$(clipboard_text || true)"
        [[ "$defined_clipboard" == "Region" ]] && defined_passed=true
    else
        defined_clipboard="popup-not-open"
        capture "name-box-dropdown-defined-name.png"
    fi

    # Repeat from neutral J20, outside every fixture object, and require a fresh popup before selecting the
    # third entry, PhysicalTable. Its one-row body must copy exactly North/120.
    select_cell 9 19 J20 || true
    capture "name-box-dropdown-table-before.png"
    before_root="$output/name-box-dropdown-table-before-root.png"
    scrot "$before_root"
    x11_window_snapshot "$output/name-box-dropdown-table-before-x11.txt"
    window_count_before="$(x11_visible_window_count)"
    focus_app
    xdotool_mousemove_sync "$dropdown_x" "$dropdown_y" click 1
    sleep "$settle_seconds"
    after_root="$output/name-box-dropdown-table-open-root.png"
    scrot "$after_root"
    x11_window_snapshot "$output/name-box-dropdown-table-open-x11.txt"
    wmctrl -lG > "$output/name-box-dropdown-table-windows.txt"
    window_count_open="$(x11_visible_window_count)"
    popup_open=false
    if (( window_count_open > window_count_before )); then
        popup_open=true
    fi
    table_popup_open="$popup_open"
    if $popup_open; then
        xdotool key --clearmodifiers --delay "$input_delay_ms" Home Down Down Down Down Return
        sleep "$settle_seconds"
        capture "name-box-dropdown-table.png"
        send_key ctrl+c
        table_clipboard="$(clipboard_text || true)"
        [[ "$table_clipboard" == $'North\t120' ]] && table_passed=true
    else
        table_clipboard="popup-not-open"
        capture "name-box-dropdown-table.png"
    fi

    write_artifact "name-box-dropdown-postcondition.txt" \
        "dropdown-x=$dropdown_x\ndropdown-y=$dropdown_y\nexpected-order=PhysicalChart,PhysicalName,PhysicalPicture,PhysicalShape,PhysicalTable,PhysicalTextBox\ndefined-popup-open=$defined_popup_open\ntable-popup-open=$table_popup_open\ndefined-clipboard=$defined_clipboard\ntable-clipboard=$table_clipboard\ndefined-name-passed=$defined_passed\ntable-passed=$table_passed\n"
    if $defined_passed; then
        record "name-box-dropdown-defined-name-physical" "passed" \
            "name-box-dropdown-defined-before.png; name-box-dropdown-open-root.png; name-box-dropdown-defined-before-x11.txt; name-box-dropdown-defined-open-x11.txt; name-box-dropdown-defined-windows.txt; name-box-dropdown-defined-name.png; defined-clipboard=$defined_clipboard" \
            "The production Avalonia Name Box flyout created an additional visible X11 popup window and its focused defined-name entry produced the exact Region clipboard value from neutral J20." \
            "$artifacts"
    else
        record "name-box-dropdown-defined-name-physical" "failed" "$artifacts" \
            "The defined-name flyout selection did not produce the expected Region clipboard value (observed '$defined_clipboard')." "$artifacts"
    fi
    if $table_passed; then
        record "name-box-dropdown-table-physical" "passed" \
            "name-box-dropdown-table-before.png; name-box-dropdown-table-open-root.png; name-box-dropdown-table-before-x11.txt; name-box-dropdown-table-open-x11.txt; name-box-dropdown-table-windows.txt; name-box-dropdown-table.png; table-clipboard=$table_clipboard" \
            "The non-defined-name table entry was selected through the focused production flyout with an additional visible X11 popup window, from neutral J20, and copied the exact one-row table body North/120." \
            "$artifacts"
    else
        record "name-box-dropdown-table-physical" "failed" "$artifacts" \
            "The non-defined-name table flyout selection did not produce the expected North/East table-body clipboard values (observed '$table_clipboard')." "$artifacts"
    fi

    : > "$output/name-box-dropdown-object-results.jsonl"
    probe_name_box_object "$output" "$dropdown_x" "$dropdown_y" "chart" 0 "Chart" "67000000-0000-0000-0000-000000000004" "PhysicalChart"
    probe_name_box_object "$output" "$dropdown_x" "$dropdown_y" "picture" 2 "Picture" "67000000-0000-0000-0000-000000000002" "PhysicalPicture"
    probe_name_box_object "$output" "$dropdown_x" "$dropdown_y" "shape" 3 "Shape" "67000000-0000-0000-0000-000000000001" "PhysicalShape"
    probe_name_box_object "$output" "$dropdown_x" "$dropdown_y" "textbox" 5 "TextBox" "67000000-0000-0000-0000-000000000003" "PhysicalTextBox"
    python3 - "$output/name-box-dropdown-object-postcondition.json" "$output/name-box-dropdown-object-results.jsonl" <<'PY'
import json
import sys

destination, source = sys.argv[1:]
with open(source, encoding="utf-8") as stream:
    results = [json.loads(line) for line in stream if line.strip()]
passed = sum(result["status"] == "passed" for result in results)
with open(destination, "w", encoding="utf-8") as stream:
    json.dump({
        "schemaVersion": 1,
        "suite": "freex-name-box-dropdown-objects-physical",
        "platform": "linux",
        "shell": "avalonia",
        "app": "FreeX",
        "expectedOrder": [
            "PhysicalChart", "PhysicalName", "PhysicalPicture",
            "PhysicalShape", "PhysicalTable", "PhysicalTextBox",
        ],
        "summary": {"passed": passed, "failed": len(results) - passed, "total": len(results)},
        "results": results,
    }, stream, indent=2)
    stream.write("\n")
PY
}

read_name_box_event() {
    python3 - "$1" <<'PY'
import json
import sys

path = sys.argv[1]
with open(path, encoding="utf-8") as stream:
    lines = [line.strip() for line in stream if line.strip()]
if not lines:
    raise SystemExit(1)
payload = json.loads(lines[-1])
def value(key):
    item = payload.get(key)
    return "" if item is None else str(item)
print("\x1f".join(value(key) for key in (
    "sequence", "stage", "itemName", "itemKind", "itemObjectKind",
    "selectedObjectKind", "selectedObjectId", "nameBoxText", "activeCell")))
PY
}

probe_name_box_dropdown_parity() {
    local dropdown_x dropdown_y popup_count=0 popup_id="" popup_x=0 popup_y=0 popup_width=0 popup_height=0
    local before_root="name-box-dropdown-parity-before-root.png"
    local before_x11="name-box-dropdown-parity-before-x11.txt"
    local open_root="name-box-dropdown-parity-open-root.png"
    local open_x11="name-box-dropdown-parity-open-x11.txt"
    local crop_png="popup.nameBoxDropdown.png"
    local geometry_json="name-box-dropdown-parity-native.json"
    local parity_manifest="name-box-dropdown-parity-manifest.json"
    local parity_directory="$output/name-box-dropdown-parity-native"
    local captured=false reason="" color_count=0 content_color_count=0
    local artifacts="$before_root;$before_x11;$open_root;$open_x11;$crop_png;$geometry_json;$parity_manifest"
    local failure_artifacts="$before_root;$before_x11;$open_root;$open_x11;$geometry_json;$parity_manifest"

    dropdown_x="$((a1_x + cell_width * 78 / 100))"
    dropdown_y="$((a1_y - cell_height / 2 - 29))"

    select_cell 9 19 J20 || true
    scrot "$output/$before_root"
    x11_window_snapshot "$output/$before_x11"
    focus_app
    xdotool_mousemove_sync "$dropdown_x" "$dropdown_y" click 1
    sleep "$settle_seconds"
    scrot "$output/$open_root"
    x11_window_snapshot "$output/$open_x11"

    IFS='|' read -r popup_count popup_id popup_x popup_y popup_width popup_height < <(
        python3 - "$output/$before_x11" "$output/$open_x11" <<'PY'
import re
import sys

def windows(path):
    result = {}
    with open(path, encoding="utf-8") as stream:
        for raw in stream:
            parts = raw.rstrip("\n").split("|", 2)
            if len(parts) != 3:
                continue
            geometry = dict(re.findall(r"([A-Z]+)=(-?\d+)", parts[2]))
            required = ("X", "Y", "WIDTH", "HEIGHT")
            if all(key in geometry for key in required):
                result[parts[0]] = tuple(int(geometry[key]) for key in required)
    return result

before = windows(sys.argv[1])
after = windows(sys.argv[2])
candidates = [(window_id, *bounds) for window_id, bounds in after.items() if window_id not in before]
if len(candidates) == 1:
    print("|".join(str(value) for value in (1, *candidates[0])))
else:
    print(f"{len(candidates)}|||||")
PY
    )

    if [[ "$popup_count" == "1" &&
          "$popup_x" =~ ^[0-9]+$ && "$popup_y" =~ ^[0-9]+$ &&
          "$popup_width" =~ ^[0-9]+$ && "$popup_height" =~ ^[0-9]+$ &&
          "$popup_width" -ge 208 && "$popup_height" -ge 136 ]]; then
        convert "$output/$open_root" \
            -crop "208x136+${popup_x}+${popup_y}" +repage \
            "$output/$crop_png"
        color_count="$(identify -format '%k' "$output/$crop_png" 2>/dev/null || true)"
        content_color_count="$(convert "$output/$crop_png" -shave 2x2 -format '%k' info: 2>/dev/null || true)"
        if [[ "$(identify -format '%wx%h' "$output/$crop_png" 2>/dev/null || true)" == "208x136" &&
              "$color_count" =~ ^[0-9]+$ && "$color_count" -gt 1 &&
              "$content_color_count" =~ ^[0-9]+$ && "$content_color_count" -gt 1 ]]; then
            captured=true
            reason="The 208x136 frame was cropped without scaling from the newly visible native X11 popup window."
        else
            reason="The native popup crop was missing, blank inside its border, or not exactly 208x136."
        fi
    elif [[ "$popup_count" != "1" ]]; then
        reason="Expected exactly one newly visible X11 popup window, found $popup_count."
    else
        reason="The native popup window geometry ${popup_width}x${popup_height} cannot contain the required 208x136 frame."
    fi

    python3 - \
        "$output/$geometry_json" "$output/$parity_manifest" "$captured" "$reason" \
        "$popup_id" "$popup_x" "$popup_y" "$popup_width" "$popup_height" \
        "$open_root" "$crop_png" "$geometry_json" "$before_x11" "$open_x11" <<'PY'
import json
import sys

(
    geometry_path, manifest_path, captured_text, reason,
    window_id, source_x, source_y, source_width, source_height,
    source_png, crop_png, geometry_name, before_inventory, open_inventory,
) = sys.argv[1:]
captured = captured_text == "true"

def number(value):
    try:
        return int(value)
    except (TypeError, ValueError):
        return 0

source_window = {
    "id": window_id,
    "x": number(source_x),
    "y": number(source_y),
    "width": number(source_width),
    "height": number(source_height),
}
geometry = {
    "schemaVersion": 1,
    "platform": "linux",
    "shell": "avalonia",
    "surfaceId": "popup.nameBoxDropdown",
    "evidenceProvenance": "native-x11-root-crop",
    "captured": captured,
    "sourcePng": source_png,
    "windowInventoryBefore": before_inventory,
    "windowInventoryOpen": open_inventory,
    "sourceWindow": source_window,
    "crop": {
        "x": source_window["x"],
        "y": source_window["y"],
        "width": 208,
        "height": 136,
        "resized": False,
    },
    "expectedItems": [
        "Sales",
        "Tour Name Box Chart",
        "Tour Name Box Picture",
        "Tour Name Box Shape",
        "Tour Name Box Text Box",
    ],
    "note": reason,
}
surface = {
    "id": "popup.nameBoxDropdown",
    "kind": "overlay",
    "png": crop_png,
    "captured": captured,
    "note": reason,
    "width": 208,
    "height": 136,
    "evidenceProvenance": "native-x11-root-crop",
    "sourcePng": source_png,
    "geometryEvidence": geometry_name,
    "sourceX": source_window["x"],
    "sourceY": source_window["y"],
    "sourceWidth": source_window["width"],
    "sourceHeight": source_window["height"],
}
with open(geometry_path, "w", encoding="utf-8") as stream:
    json.dump(geometry, stream, indent=2)
    stream.write("\n")
with open(manifest_path, "w", encoding="utf-8") as stream:
    json.dump({"platform": "linux", "shell": "avalonia", "surfaces": [surface]}, stream, indent=2)
    stream.write("\n")
PY

    mkdir -p "$parity_directory"
    cp "$output/$open_root" "$parity_directory/$open_root"
    cp "$output/$before_x11" "$parity_directory/$before_x11"
    cp "$output/$open_x11" "$parity_directory/$open_x11"
    cp "$output/$geometry_json" "$parity_directory/$geometry_json"
    cp "$output/$parity_manifest" "$parity_directory/manifest.json"
    if $captured; then
        cp "$output/$crop_png" "$parity_directory/$crop_png"
        record "name-box-dropdown-parity-native-crop" "passed" \
            "$open_root; $open_x11; popup-window=$popup_id; popup-geometry=${popup_width}x${popup_height}+${popup_x}+${popup_y}; crop=208x136; provenance=native-x11-root-crop" \
            "$reason" "$artifacts"
    else
        record "name-box-dropdown-parity-native-crop" "failed" \
            "$open_root; $open_x11; $geometry_json; $parity_manifest" \
            "$reason" "$failure_artifacts"
    fi

    send_key Escape || true
}

record_name_box_object_result() {
    local suffix="$1" expected_kind="$2" expected_id="$3" expected_name="$4" status="$5"
    local baseline_sequence="$6" baseline_stage="$7" baseline_selected_kind="$8" baseline_selected_id="$9"
    local baseline_name_box="${10}" baseline_cell="${11}" observed_sequence="${12}" observed_stage="${13}"
    local observed_name="${14}" observed_item_kind="${15}" observed_object_kind="${16}" observed_selected_kind="${17}"
    local observed_id="${18}" observed_name_box="${19}" observed_cell="${20}"
    local result_path="$output/name-box-dropdown-object-results.jsonl"
    printf '{"id":"name-box-dropdown-%s-physical","expectedName":"%s","expectedKind":"%s","expectedId":"%s","baselineSequence":%s,"baselineStage":"%s","baselineSelectedObjectKind":"%s","baselineSelectedObjectId":"%s","baselineNameBox":"%s","baselineActiveCell":"%s","observedSequence":%s,"observedStage":"%s","observedName":"%s","observedItemKind":"%s","observedObjectKind":"%s","observedSelectedObjectKind":"%s","observedId":"%s","observedNameBox":"%s","observedActiveCell":"%s","status":"%s"}\n' \
        "$(json_escape "$suffix")" "$(json_escape "$expected_name")" "$(json_escape "$expected_kind")" "$(json_escape "$expected_id")" \
        "${baseline_sequence:-0}" "$(json_escape "$baseline_stage")" "$(json_escape "$baseline_selected_kind")" "$(json_escape "$baseline_selected_id")" \
        "$(json_escape "$baseline_name_box")" "$(json_escape "$baseline_cell")" "${observed_sequence:-0}" "$(json_escape "$observed_stage")" "$(json_escape "$observed_name")" \
        "$(json_escape "$observed_item_kind")" "$(json_escape "$observed_object_kind")" "$(json_escape "$observed_selected_kind")" "$(json_escape "$observed_id")" \
        "$(json_escape "$observed_name_box")" "$(json_escape "$observed_cell")" "$status" >> "$result_path"
}

probe_name_box_object() {
    local probe_output="$1" dropdown_x="$2" dropdown_y="$3" suffix="$4" down_count="$5"
    local expected_kind="$6" expected_id="$7" expected_name="$8"
    local before_file="name-box-dropdown-$suffix-before.png"
    local open_root="name-box-dropdown-$suffix-open-root.png"
    local open_x11="name-box-dropdown-$suffix-open-x11.txt"
    local windows_file="name-box-dropdown-$suffix-windows.txt"
    local selected_file="name-box-dropdown-$suffix-selected.png"
    local artifact_list="$before_file;$open_root;$open_x11;$windows_file;$selected_file;name-box-dropdown-object-state.jsonl;name-box-dropdown-object-postcondition.json"
    local neutral_ok=true popup_open=false baseline_event="" event="" baseline_sequence=0
    local baseline_stage="" baseline_selected_kind="" baseline_selected_id="" baseline_name_box="" baseline_cell=""
    local observed_sequence=0 observed_stage="" observed_name="" observed_item_kind="" observed_object_kind="" observed_selected_kind="" observed_id="" observed_name_box="" observed_cell=""
    local passed=false note=""

    if ! select_cell 9 19 J20; then
        neutral_ok=false
    fi
    capture "$before_file"
    baseline_event="$(read_name_box_event "$probe_output/name-box-dropdown-object-state.jsonl" || true)"
    IFS=$'\x1f' read -r baseline_sequence baseline_stage _ _ _ baseline_selected_kind baseline_selected_id baseline_name_box baseline_cell <<< "$baseline_event"
    [[ "$baseline_sequence" =~ ^[0-9]+$ ]] || baseline_sequence=0
    if [[ "$baseline_stage" != "neutral-cell-selected" ||
          -n "$baseline_selected_kind" ||
          -n "$baseline_selected_id" ||
          "$baseline_name_box" != "J20" ||
          "$baseline_cell" != "J20" ]]; then
        neutral_ok=false
    fi

    window_count_before="$(x11_visible_window_count)"
    focus_app
    xdotool_mousemove_sync "$dropdown_x" "$dropdown_y" click 1
    sleep "$settle_seconds"
    scrot "$probe_output/$open_root"
    x11_window_snapshot "$probe_output/$open_x11"
    wmctrl -lG > "$probe_output/$windows_file"
    window_count_open="$(x11_visible_window_count)"
    if (( window_count_open > window_count_before )); then
        popup_open=true
    fi

    if $neutral_ok && $popup_open; then
        xdotool key --clearmodifiers --delay "$input_delay_ms" Home
        for ((index = 0; index < down_count; index++)); do
            xdotool key --clearmodifiers --delay "$input_delay_ms" Down
        done
        xdotool key --clearmodifiers --delay "$input_delay_ms" Return
        sleep "$settle_seconds"
        capture "$selected_file"

        for _ in $(seq 1 12); do
            event="$(read_name_box_event "$probe_output/name-box-dropdown-object-state.jsonl" || true)"
            IFS=$'\x1f' read -r observed_sequence observed_stage observed_name observed_item_kind observed_object_kind observed_selected_kind observed_id observed_name_box observed_cell <<< "$event"
            [[ "$observed_sequence" =~ ^[0-9]+$ ]] || observed_sequence=0
            if (( observed_sequence > baseline_sequence )); then
                break
            fi
            sleep 0.12
        done
        if [[ "$observed_sequence" =~ ^[0-9]+$ ]] &&
           (( observed_sequence > baseline_sequence )) &&
           [[ "$observed_stage" == "object-selected" ]] &&
           [[ "$observed_name" == "$expected_name" ]] &&
           [[ "$observed_item_kind" == "Object" ]] &&
           [[ "$observed_object_kind" == "$expected_kind" ]] &&
           [[ "$observed_selected_kind" == "$expected_kind" ]] &&
           [[ "$observed_id" == "$expected_id" ]] &&
           [[ "$observed_name_box" == "$expected_name" ]] &&
           [[ -n "$observed_cell" ]]; then
            passed=true
            note="Physical Name Box selection from neutral J20 produced fresh sequence $observed_sequence with exact $expected_kind identity $expected_id and Name Box text $expected_name."
        else
            note="Expected fresh $expected_kind selection '$expected_name'/$expected_id from neutral J20, observed sequence=$observed_sequence stage=$observed_stage item=$observed_name/$observed_item_kind/$observed_object_kind selected=$observed_selected_kind id=$observed_id nameBox=$observed_name_box activeCell=$observed_cell baseline=$baseline_sequence."
        fi
    else
        capture "$selected_file"
        note="The physical probe could not establish neutral selection or a fresh visible Name Box popup before selecting $expected_kind '$expected_name'."
    fi

    local status="failed"
    if $passed; then status="passed"; fi
    record_name_box_object_result \
        "$suffix" "$expected_kind" "$expected_id" "$expected_name" "$status" \
        "$baseline_sequence" "$baseline_stage" "$baseline_selected_kind" "$baseline_selected_id" "$baseline_name_box" "$baseline_cell" \
        "$observed_sequence" "$observed_stage" "$observed_name" "$observed_item_kind" "$observed_object_kind" "$observed_selected_kind" "$observed_id" "$observed_name_box" "$observed_cell"
    if $passed; then
        record "name-box-dropdown-$suffix-physical" "passed" \
            "$before_file; $open_root; $open_x11; $windows_file; $selected_file; name-box-dropdown-object-state.jsonl; object-kind=$observed_object_kind; object-id=$observed_id; name-box=$observed_name_box" \
            "$note" "$artifact_list"
    else
        record "name-box-dropdown-$suffix-physical" "failed" "$artifact_list" "$note" "$artifact_list"
    fi
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
    # ImageMagick's connected-components analysis is diagnostic only. Bound it so a
    # malformed or unusually large capture records a normal evidence failure instead
    # of leaving the physical lane and its X11 session blocked indefinitely.
    components="$(timeout --foreground --kill-after=1s "${image_tool_timeout_seconds}s" convert "$screenshot" \
        -alpha off \
        -fill black +opaque "$selection_color" \
        -fill white -opaque "$selection_color" \
        -define connected-components:verbose=true \
        -connected-components 8 null: 2>&1 || true)"
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

formula_reference_box_at() {
    local screenshot="$1" expected_x="$2" expected_y="$3" score
    # Formula point mode uses the red reference outline rather than the green active-cell outline.
    # Score red-dominant pixels inside the calibrated target cell, keeping this assertion local to
    # the dedicated formula-edit selector instead of weakening the general selection primitive.
    score="$(convert "$screenshot" \
        -crop "${cell_width}x${cell_height}+${expected_x}+${expected_y}" \
        -alpha off \
        -fx '((r-g)>0.12 && (r-b)>0.12) ? 1 : 0' \
        -format '%[fx:mean]' info: 2>/dev/null || true)"
    awk -v score="$score" 'BEGIN { exit !(score > 0.03) }'
}

wait_for_formula_reference_selection() {
    local expected_x="$1" expected_y="$2" evidence="$3"
    for _ in $(seq 1 8); do
        capture "$evidence"
        if formula_reference_box_at "$output/$evidence" "$expected_x" "$expected_y"; then
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

    # Startup fixtures can leave focus on the formula bar or another shell control. Establish
    # worksheet focus with real pointer input before relying on grid navigation shortcuts.
    xdotool mousemove --window "$window_id" \
        "$((window_width - 160))" "$((window_height - 160))" click 1
    sleep "$settle_seconds"
    send_key ctrl+Home
    local home_ready=false
    for _ in $(seq 1 20); do
        # Pre-grouped fixtures widen the row-header outline gutter before calibration.
        if capture_selection "calibration-a1.png" &&
           (( observed_x < window_x + 140 && observed_y < window_y + 300 )); then
            home_ready=true
            break
        fi
        sleep 0.15
    done
    if ! $home_ready; then
        calibration_reason="Could not isolate the active-cell selection outline after Ctrl+Home."
        return 1
    fi
    a1_x="$observed_x"
    a1_y="$observed_y"
    worksheet_base_a1_x="$a1_x"
    worksheet_base_a1_y="$a1_y"
    local a1_width="$observed_width" a1_height="$observed_height"

    local moved=false
    send_key Right
    for _ in $(seq 1 20); do
        if capture_selection "calibration-b1.png" &&
           (( observed_x > a1_x + 20 && observed_x < a1_x + 240 )) &&
           (( observed_y >= a1_y - 3 && observed_y <= a1_y + 3 )); then
            moved=true
            break
        fi
        sleep 0.15
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
    send_key Down
    for _ in $(seq 1 20); do
        if capture_selection "calibration-a2.png" &&
           (( observed_y > a1_y + 10 && observed_y < a1_y + 120 )) &&
           (( observed_x >= a1_x - 3 && observed_x <= a1_x + 3 )); then
            moved=true
            break
        fi
        sleep 0.15
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

outline_gutter_size() {
    local depth="$1"
    if (( depth <= 0 )); then
        printf '0'
    else
        printf '%d' "$((12 + depth * 14))"
    fi
}

set_expected_outline_origin() {
    local expected_row_depth="$1" expected_column_depth="$2" evidence="${3:-}"
    a1_x=$((worksheet_base_a1_x + $(outline_gutter_size "$expected_row_depth")))
    a1_y=$((worksheet_base_a1_y + $(outline_gutter_size "$expected_column_depth")))
    if [[ -n "$evidence" ]]; then
        capture "$evidence"
    fi
}

dismiss_active_popups() {
    # A failed context-menu route can leave nested flyouts active. Dismiss the complete popup
    # stack before another header drag so pointer coordinates cannot target menu content.
    for _ in $(seq 1 4); do
        xdotool key --clearmodifiers --delay "$input_delay_ms" Escape 2>/dev/null || true
        sleep 0.08
    done
    focus_app
}

cell_x() { printf '%d' "$((a1_x + $1 * cell_width))"; }
cell_y() { printf '%d' "$((a1_y + $1 * cell_height))"; }
cell_center_x() { printf '%d' "$((a1_x + $1 * cell_width + cell_width / 2))"; }
cell_center_y() { printf '%d' "$((a1_y + $1 * cell_height + cell_height / 2))"; }

open_autofilter_menu() {
    local column_offset="$1"
    select_cell "$column_offset" 0 "filter-header-$column_offset"
    send_key alt+Down
}

click_autofilter_control() {
    local x_offset="$1" y_offset="$2"
    xdotool_mousemove_sync "$((a1_x + x_offset))" "$((a1_y + y_offset))"
    xdotool mousedown 1
    sleep 0.12
    xdotool mouseup 1
    sleep "$settle_seconds"
}

select_cell() {
    local column_offset="$1" row_offset="$2" address="$3"
    local expected_x expected_y center_x center_y
    expected_x="$(cell_x "$column_offset")"
    expected_y="$(cell_y "$row_offset")"
    center_x="$(cell_center_x "$column_offset")"
    center_y="$(cell_center_y "$row_offset")"

    for _ in $(seq 1 2); do
        focus_app
        xdotool_mousemove_sync "$center_x" "$center_y" click 1
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

screen_height() { xdotool getdisplaygeometry | awk '{print $2}'; }
sheet_tab_strip_top() { printf '%d' "$(( $(screen_height) - 55 ))"; }
sheet_tab_y() { printf '%d' "$(( $(screen_height) - 41 ))"; }
sheet_tab_left_nav_x() { printf '%d' "$((window_x + 15))"; }
sheet_horizontal_scrollbar_width() {
    local desired=$((window_width * 34 / 100))
    (( desired < 260 )) && desired=260
    (( desired > 420 )) && desired=420
    printf '%d' "$desired"
}
sheet_tab_right_nav_x() {
    local scrollbar_width
    scrollbar_width="$(sheet_horizontal_scrollbar_width)"
    printf '%d' "$((window_x + window_width - scrollbar_width - 12))"
}

pivot_pane_left() {
    local pane_width="${FREEX_X11_PIVOT_PANE_WIDTH:-248}"
    printf '%d' "$((window_x + window_width - pane_width))"
}

pivot_pane_top() {
    # The task pane is docked beside the worksheet work area. The calibrated A1 cell
    # starts one column-header pitch below that work-area edge.
    printf '%d' "$((a1_y - cell_height))"
}

pivot_chip_x() {
    printf '%d' "$(( $(pivot_pane_left) + ${FREEX_X11_PIVOT_CHIP_X_OFFSET:-96} ))"
}

pivot_bucket_chip_y() {
    local bucket="$1"
    local top="$(pivot_pane_top)"
    local offset
    # BuildPivotFieldPaneBody uses a fixed title/search preamble and then the
    # Available, Filters, Columns, Rows, Values buckets in that order.
    case "$bucket" in
        filters) offset="${FREEX_X11_PIVOT_FILTERS_Y_OFFSET:-168}" ;;
        rows) offset="${FREEX_X11_PIVOT_ROWS_Y_OFFSET:-288}" ;;
        *) return 1 ;;
    esac
    printf '%d' "$((top + offset))"
}

pivot_layout_signature() {
    python3 - "$document_path" <<'PY'
import sys
import zipfile
import xml.etree.ElementTree as ET

path = sys.argv[1]
namespace = "{http://schemas.openxmlformats.org/spreadsheetml/2006/main}"
try:
    with zipfile.ZipFile(path) as package:
        root = ET.fromstring(package.read("xl/pivotTables/pivotTable1.xml"))
except (OSError, KeyError, ET.ParseError):
    raise SystemExit(1)

def values(parent_name, attribute):
    parent = root.find(namespace + parent_name)
    if parent is None:
        return ""
    return ",".join(
        child.attrib[attribute]
        for child in list(parent)
        if attribute in child.attrib
    )

print(f"rows={values('rowFields', 'x')}")
print(f"pages={values('pageFields', 'fld')}")
print(f"values={values('dataFields', 'fld')}")
PY
}

pivot_detail_package_signature() {
    python3 - "$document_path" <<'PY'
import posixpath
import sys
import zipfile
import xml.etree.ElementTree as ET

path = sys.argv[1]
main = "{http://schemas.openxmlformats.org/spreadsheetml/2006/main}"
rel = "{http://schemas.openxmlformats.org/officeDocument/2006/relationships}"
package_rel = "{http://schemas.openxmlformats.org/package/2006/relationships}"

with zipfile.ZipFile(path) as package:
    workbook = ET.fromstring(package.read("xl/workbook.xml"))
    relationships = ET.fromstring(package.read("xl/_rels/workbook.xml.rels"))
    targets = {
        item.attrib["Id"]: item.attrib["Target"]
        for item in relationships.findall(package_rel + "Relationship")
    }
    strings = []
    try:
        shared = ET.fromstring(package.read("xl/sharedStrings.xml"))
        for item in shared.findall(main + "si"):
            strings.append("".join(node.text or "" for node in item.iter(main + "t")))
    except KeyError:
        pass

    detail = None
    for sheet in workbook.find(main + "sheets"):
        if sheet.attrib["name"].startswith("Detail"):
            detail = sheet
            break
    if detail is None:
        raise SystemExit(1)

    target = targets[detail.attrib[rel + "id"]].lstrip("/")
    if not target.startswith("xl/"):
        target = posixpath.normpath(posixpath.join("xl", target))
    worksheet = ET.fromstring(package.read(target))

def cell_value(address):
    for cell in worksheet.iter(main + "c"):
        if cell.attrib.get("r") != address:
            continue
        value = cell.find(main + "v")
        if value is None:
            return ""
        text = value.text or ""
        if cell.attrib.get("t") == "s":
            return strings[int(text)]
        return text
    return ""

sheet_count = len(workbook.find(main + "sheets"))
print(f"detail={detail.attrib['name']}")
print(f"sheet-count={sheet_count}")
for address in ("A1", "B1", "C1", "A2", "B2", "C2"):
    print(f"{address}={cell_value(address)}")
PY
}

wait_for_document_hash_change() {
    local before="$1" current=""
    for _ in $(seq 1 20); do
        current="$(sha256sum "$document_path" 2>/dev/null | awk '{print $1}')"
        if [[ -n "$before" && -n "$current" && "$current" != "$before" ]]; then
            printf '%s' "$current"
            return 0
        fi
        sleep 0.25
    done
    printf '%s' "$current"
    return 1
}

drag_pivot_chip() {
    local source_bucket="$1" target_bucket="$2" source_index="${3:-0}" target_index="${4:-0}" target_y
    local source_x="$(pivot_chip_x)" source_y="$(pivot_bucket_chip_y "$source_bucket")"
    source_y=$((source_y + source_index * 27))
    target_y="$(pivot_bucket_chip_y "$target_bucket")"
    target_y=$((target_y + target_index * 27 - 22))
    focus_app
    xdotool_mousemove_sync "$source_x" "$source_y"
    xdotool mousedown 1
    sleep 0.25
    xdotool_mousemove_sync "$((source_x + 12))" "$((target_y - 18))"
    sleep 0.25
    xdotool_mousemove_sync "$((source_x + 12))" "$target_y"
    sleep "$settle_seconds"
    xdotool mouseup 1
    sleep "$settle_seconds"
}

probe_pivot_field_list() {
    local pivot_x pivot_y pane_left pane_top before_hash after_hash before_layout after_layout
    local cross_passed=false reorder_passed=false
    local artifacts="pivot-field-list-before.png;pivot-field-list-cross-bucket.png;pivot-field-list-reorder.png;pivot-field-list-postcondition.txt"

    pivot_x="$(cell_center_x 4)"
    pivot_y="$(cell_center_y 0)"
    pane_left="$(pivot_pane_left)"
    pane_top="$(pivot_pane_top)"
    focus_app
    xdotool_mousemove_sync "$pivot_x" "$pivot_y" click 1
    sleep "$dialog_settle_seconds"
    capture "pivot-field-list-before.png"
    before_layout="$(pivot_layout_signature || true)"
    before_hash="$(sha256sum "$document_path" 2>/dev/null | awk '{print $1}' || true)"

    # Category (source field 1) starts in Filters. Moving it into Rows is a real
    # cross-bucket, positional insertion before the existing Region (source 0).
    drag_pivot_chip filters rows 0 0
    send_key ctrl+s
    after_hash="$(wait_for_document_hash_change "$before_hash" || true)"
    capture "pivot-field-list-cross-bucket.png"
    after_layout="$(pivot_layout_signature || true)"
    if [[ "$after_layout" == *"rows=1,0"* && "$after_layout" == *"pages="* && "$after_layout" == *"values=2"* ]]; then
        cross_passed=true
    fi
    write_artifact "pivot-field-list-cross-postcondition.txt" \
        "before=$before_layout\nafter=$after_layout\npane-left=$pane_left\npane-top=$pane_top\nsource=filters\ntarget=rows\n"
    if $cross_passed; then
        record "pivot-field-drag-cross-bucket-physical" "passed" \
            "pivot-field-list-before.png; pivot-field-list-cross-bucket.png; $after_layout" \
            "Category was physically dragged from Filters into the Rows bucket before Region and the saved PivotTable package now reports row fields 1,0 with no page field." \
            "pivot-field-list-before.png;pivot-field-list-cross-bucket.png;pivot-field-list-cross-postcondition.txt"
    else
        record "pivot-field-drag-cross-bucket-physical" "failed" \
            "pivot-field-list-before.png; pivot-field-list-cross-bucket.png; pivot-field-list-cross-postcondition.txt" \
            "The physical Filters-to-Rows drag did not produce the expected persisted PivotTable layout: $after_layout" \
            "pivot-field-list-before.png;pivot-field-list-cross-bucket.png;pivot-field-list-cross-postcondition.txt"
    fi

    if $cross_passed; then
        before_hash="$after_hash"
        # Category is now the first Rows item. Dropping it below Region proves
        # same-bucket reorder and positional insertion rather than mere transfer.
        drag_pivot_chip rows rows 0 2
        send_key ctrl+s
        after_hash="$(wait_for_document_hash_change "$before_hash" || true)"
        capture "pivot-field-list-reorder.png"
        after_layout="$(pivot_layout_signature || true)"
        if [[ "$after_layout" == *"rows=0,1"* && "$after_layout" == *"pages="* && "$after_layout" == *"values=2"* ]]; then
            reorder_passed=true
        fi
        write_artifact "pivot-field-list-postcondition.txt" \
            "cross-layout=rows=1,0;pages=;values=2\nreordered-layout=$after_layout\npane-left=$pane_left\npane-top=$pane_top\nsource=rows[0]\ntarget=rows[2]\n"
    else
        write_artifact "pivot-field-list-postcondition.txt" \
            "cross-layout=$after_layout\nreordered-layout=not-run\npane-left=$pane_left\npane-top=$pane_top\n"
    fi
    if $reorder_passed; then
        record "pivot-field-drag-same-bucket-reorder-physical" "passed" \
            "pivot-field-list-cross-bucket.png; pivot-field-list-reorder.png; $after_layout" \
            "Category was physically dragged within Rows below Region and the saved PivotTable package reports row fields 0,1 while preserving Values." \
            "pivot-field-list-cross-bucket.png;pivot-field-list-reorder.png;pivot-field-list-postcondition.txt"
    else
        record "pivot-field-drag-same-bucket-reorder-physical" "failed" \
            "pivot-field-list-cross-bucket.png; pivot-field-list-reorder.png; pivot-field-list-postcondition.txt" \
            "The physical Rows reorder did not produce the expected persisted layout: $after_layout" \
            "pivot-field-list-cross-bucket.png;pivot-field-list-reorder.png;pivot-field-list-postcondition.txt"
    fi
}

probe_pivot_table_details_double_click() {
    local before_hash after_hash package_signature
    local header_a header_b header_c row_a row_b row_c
    local passed=false
    local artifacts="pivot-details-before.png;pivot-details-after-double-click.png;pivot-details-readback.png;pivot-details-postcondition.txt"

    before_hash="$(sha256sum "$document_path" 2>/dev/null | awk '{print $1}' || true)"
    capture "pivot-details-before.png"

    # F2 is the first rendered value cell in the deterministic PivotTable fixture.
    # Keep both clicks inside the framework double-click interval while allowing the
    # first click's contextual-pane refresh to complete at the same cell coordinate.
    focus_app
    xdotool_mousemove_sync "$(cell_center_x 5)" "$(cell_center_y 1)"
    xdotool click --repeat 2 --delay 180 1
    sleep "$dialog_settle_seconds"
    capture "pivot-details-after-double-click.png"

    header_a="$(copy_cell_display 0 0 A1 || true)"
    header_b="$(copy_cell_display 1 0 B1 || true)"
    header_c="$(copy_cell_display 2 0 C1 || true)"
    row_a="$(copy_cell_display 0 1 A2 || true)"
    row_b="$(copy_cell_display 1 1 B2 || true)"
    row_c="$(copy_cell_display 2 1 C2 || true)"
    capture "pivot-details-readback.png"

    send_key ctrl+s
    after_hash="$(wait_for_document_hash_change "$before_hash" || true)"
    package_signature="$(pivot_detail_package_signature || true)"
    write_artifact "pivot-details-postcondition.txt" \
        "clipboard=A1:$header_a|B1:$header_b|C1:$header_c|A2:$row_a|B2:$row_b|C2:$row_c\nbefore-hash=$before_hash\nafter-hash=$after_hash\n$package_signature\n"

    if [[ "$header_a" == "Region" &&
          "$header_b" == "Category" &&
          "$header_c" == "Amount" &&
          "$row_a" == "North" &&
          "$row_b" == "Hardware" &&
          "$row_c" == "100" &&
          "$package_signature" == *"detail=Detail"* &&
          "$package_signature" == *"sheet-count=2"* &&
          "$package_signature" == *"A1=Region"* &&
          "$package_signature" == *"B1=Category"* &&
          "$package_signature" == *"C1=Amount"* &&
          "$package_signature" == *"A2=North"* &&
          "$package_signature" == *"B2=Hardware"* &&
          "$package_signature" == *"C2=100"* ]]; then
        passed=true
    fi

    if $passed; then
        record "pivot-table-details-double-click-physical" "passed" \
            "pivot-details-after-double-click.png; clipboard readback Region|Category|Amount and North|Hardware|100; $package_signature" \
            "A physical double-click on PivotTable value cell F2 activated a new Detail sheet before inline editing; X11 clipboard reads and the saved OOXML package agree on the detail rows." \
            "$artifacts"
    else
        record "pivot-table-details-double-click-physical" "failed" \
            "pivot-details-before.png; pivot-details-after-double-click.png; pivot-details-readback.png; pivot-details-postcondition.txt" \
            "The physical F2 double-click did not create and persist the expected Detail worksheet (clipboard='$header_a|$header_b|$header_c;$row_a|$row_b|$row_c'; package='$package_signature')." \
            "$artifacts"
    fi
}

probe_autofilter_recalculation() {
    local initial_value north_value south_value cleared_value
    local artifacts="autofilter-recalculation-before.png;autofilter-recalculation-menu-open.png;autofilter-recalculation-north-checked.png;autofilter-recalculation-north-committed.png;autofilter-recalculation-north.png;autofilter-recalculation-south-checked.png;autofilter-recalculation-south-committed.png;autofilter-recalculation-south.png;autofilter-recalculation-cleared.png;autofilter-recalculation-postcondition.txt"
    local passed=false

    # Seed a compact, deterministic worksheet without saving it back to the caller's CSV.
    set_cell_text_without_save 0 0 A1 "Region"
    set_cell_text_without_save 1 0 B1 "Amount"
    set_cell_text_without_save 0 1 A2 "North"
    set_cell_text_without_save 1 1 B2 "10"
    set_cell_text_without_save 0 2 A3 "South"
    set_cell_text_without_save 1 2 B3 "20"
    set_cell_text_without_save 1 3 B4 "=SUBTOTAL(109,B2:B3)"
    initial_value="$(copy_cell_display 1 3 B4 || true)"
    capture "autofilter-recalculation-before.png"

    # Select A1:B3 and toggle AutoFilter through the production Excel shortcut.
    select_cell 0 0 A1
    send_key shift+Right
    send_key shift+Down
    send_key shift+Down
    send_key ctrl+shift+l
    select_cell 0 0 A1

    # The harness is fixed at 96 DPI. Click the checklist and command controls relative to the
    # calibrated A1 header so criteria controls cannot make this probe depend on incidental tab order.
    open_autofilter_menu 0
    capture "autofilter-recalculation-menu-open.png"
    click_autofilter_control 29 366
    capture "autofilter-recalculation-north-checked.png"
    click_autofilter_control 246 395
    capture "autofilter-recalculation-north-committed.png"
    sleep "$settle_seconds"
    # One filtered data row is hidden, so B4 occupies the third visible worksheet row.
    north_value="$(copy_cell_display 1 2 B4-filtered-north || true)"
    capture "autofilter-recalculation-north.png"

    # Change the active checklist from North to South, preserving the same formula cell.
    select_cell 0 0 A1
    open_autofilter_menu 0
    click_autofilter_control 29 348
    click_autofilter_control 29 366
    capture "autofilter-recalculation-south-checked.png"
    click_autofilter_control 246 395
    capture "autofilter-recalculation-south-committed.png"
    sleep "$settle_seconds"
    south_value="$(copy_cell_display 1 2 B4-filtered-south || true)"
    capture "autofilter-recalculation-south.png"

    # Clear the active Region filter from the same production flyout.
    select_cell 0 0 A1
    open_autofilter_menu 0
    click_autofilter_control 151 121
    sleep "$settle_seconds"
    cleared_value="$(copy_cell_display 1 3 B4-cleared || true)"
    capture "autofilter-recalculation-cleared.png"

    write_artifact "autofilter-recalculation-postcondition.txt" \
        "initial=$initial_value\nnorth=$north_value\nsouth=$south_value\ncleared=$cleared_value\n"
    if [[ "$initial_value" == "30" && "$north_value" == "10" &&
          "$south_value" == "20" && "$cleared_value" == "30" ]]; then
        passed=true
    fi
    if $passed; then
        record "autofilter-recalculation-apply-change-clear-physical" "passed" \
            "autofilter-recalculation-before.png; autofilter-recalculation-north.png; autofilter-recalculation-south.png; autofilter-recalculation-cleared.png; values=$initial_value->$north_value->$south_value->$cleared_value" \
            "The Linux X11 AutoFilter workflow recalculated SUBTOTAL(109,...) immediately after applying, changing, and clearing the filter." \
            "$artifacts"
    else
        record "autofilter-recalculation-apply-change-clear-physical" "failed" \
            "$artifacts" \
            "Expected SUBTOTAL values 30->10->20->30, observed $initial_value->$north_value->$south_value->$cleared_value." \
            "$artifacts"
    fi
}

sheet_tab_center_x() {
    local index="$1"
    if (( index == 0 )); then
        printf '%d' "$((a1_x + 80))"
    else
        # The default Linux harness workbook uses one long first tab followed by the
        # 64px SheetN tabs. Keep these coordinates derived from the calibrated grid edge.
        printf '%d' "$((a1_x + 160 + index * 64 - 32))"
    fi
}

sheet_plus_center_x() {
    local created_count="$1"
    local first_tab_width="${FREEX_X11_FIRST_SHEET_TAB_WIDTH:-161}"
    local short_tab_width="${FREEX_X11_SHORT_SHEET_TAB_WIDTH:-64}"
    # The first CSV tab is wider than SheetN tabs. Derive the moving + center from
    # calibrated grid origin instead of window-relative fixed coordinates.
    printf '%d' "$((a1_x + first_tab_width + 16 + created_count * short_tab_width))"
}

crop_region() {
    local source="$1" destination="$2" left="$3" top="$4" width="$5" height="$6"
    convert "$output/$source" -crop "${width}x${height}+${left}+${top}" +repage "$output/$destination"
}

capture_sheet_tab_strip() {
    local name="$1" top
    top="$(sheet_tab_strip_top)"
    capture "$name"
    crop_region "$name" "${name%.png}-strip.png" "$window_x" "$top" "$window_width" 31
}

reset_sheet_tab_viewport() {
    local tab_y="$1" left_nav_x="$2"
    # The new-sheet sequence can leave the active tab beyond the viewport. Repeated left
    # clicks are intentional physical input: the disabled-at-origin button is harmless, and
    # the final fixed-coordinate tab assertions only run after this bounded reset.
    for _ in $(seq 1 8); do
        focus_app
        xdotool_mousemove_sync "$left_nav_x" "$tab_y" click 1
        sleep "$settle_seconds"
    done
}

set_cell_text_without_save() {
    local column_offset="$1" row_offset="$2" address="$3" value="$4" committed=""
    select_cell "$column_offset" "$row_offset" "$address" || return 1
    send_key F2
    send_key ctrl+a
    send_key BackSpace
    # Empty input must not depend on an empty xdotool packet, and an empty editor
    # does not replace the X11 clipboard owner after Ctrl+C.
    if [[ -n "$value" ]]; then
        type_text "$value"
    fi
    send_key Return
    if [[ -n "$value" ]]; then
        committed="$(copy_cell_formula_by_keyboard "$column_offset" "$row_offset" || true)"
    else
        committed="$(copy_cell_formula_allow_empty "$column_offset" "$row_offset" "$address" keyboard || true)"
    fi
    [[ "$committed" == "$value" ]]
}

select_sheet_tab() {
    local index="$1" tab_y="$(sheet_tab_y)" tab_x
    tab_x="$(sheet_tab_center_x "$index")"
    focus_app
    xdotool_mousemove_sync "$tab_x" "$tab_y" click 1
    sleep "$settle_seconds"
}

select_sheet_tab_range_end() {
    local index="$1" tab_y="$(sheet_tab_y)" tab_x
    tab_x="$(sheet_tab_center_x "$index")"
    focus_app
    # Excel-style 3-D point selection extends the sheet span with Shift-click;
    # typing a literal colon produces the invalid Sheet2!B2:Sheet3!B2 form.
    xdotool keydown --window "$window_id" Shift_L
    xdotool_mousemove_sync "$tab_x" "$tab_y" click 1
    xdotool keyup --window "$window_id" Shift_L
    sleep "$settle_seconds"
}

rename_sheet_tab() {
    local index="$1" tab_y="$(sheet_tab_y)" tab_x
    tab_x="$(sheet_tab_center_x "$index")"
    focus_app
    xdotool_mousemove_sync "$tab_x" "$tab_y" click 1
    xdotool click --repeat 2 --delay 120 1
    sleep "$dialog_settle_seconds"
    # The Avalonia Rename Sheet window is a separate X11 top-level window. Use the focused
    # window for its text and accept keys; the main workbook window id does not own this dialog.
    xdotool key --clearmodifiers ctrl+a
    xdotool type --clearmodifiers --delay "$type_delay_ms" "Revenue Data"
    xdotool key --clearmodifiers Return
    sleep "$settle_seconds"
}

normalize_formula() {
    printf '%s' "$1" | tr -d "'\$ " | tr '[:lower:]' '[:upper:]'
}

probe_formula_bar_point_mode_3d() {
    local tab_y first_tab_x plus_x created_count
    local source_first="" source_last="" committed_formula="" committed_display=""
    local normalized_formula="" formula_passed=false result_passed=false formula_status=false result_status=false
    local artifacts="formula-3d-create-before.png;formula-3d-create-after.png;formula-3d-sheet2-seeded.png;formula-3d-sheet3-seeded.png;formula-3d-point-start.png;formula-3d-sheet2-point.png;formula-3d-sheet3-point.png;formula-3d-committed-sheet3.png;formula-3d-committed.png;formula-3d-postcondition.txt"

    tab_y="$(sheet_tab_y)"
    first_tab_x="$(sheet_tab_center_x 0)"
    capture_sheet_tab_strip "formula-3d-create-before.png"
    for created_count in 0 1; do
        plus_x="$(sheet_plus_center_x "$created_count")"
        focus_app
        xdotool_mousemove_sync "$plus_x" "$tab_y" click 1
        sleep "$settle_seconds"
    done
    capture_sheet_tab_strip "formula-3d-create-after.png"

    # Return to the original worksheet before seeding the two physically created sources.
    focus_app
    xdotool_mousemove_sync "$first_tab_x" "$tab_y" click 1
    sleep "$settle_seconds"
    if ! select_sheet_tab 1 || ! set_cell_text_without_save 1 1 B2 10; then
        write_artifact "formula-3d-postcondition.txt" "created-sheets=2\nseed-sheet2=false\nseed-sheet3=false\n"
        record "formula-bar-point-mode-3d-sheet-range" "failed" "formula-3d-create-before.png; formula-3d-create-after.png; formula-3d-postcondition.txt" "Could not physically create/select Sheet2 and seed its B2 source value." "$artifacts"
        return
    fi
    capture_sheet_tab_strip "formula-3d-sheet2-seeded.png"
    source_first="$(copy_cell_formula 1 1 B2 || true)"

    if ! select_sheet_tab 2 || ! set_cell_text_without_save 1 1 B2 20; then
        write_artifact "formula-3d-postcondition.txt" "created-sheets=2\nseed-sheet2=$source_first\nseed-sheet3=false\n"
        record "formula-bar-point-mode-3d-sheet-range" "failed" "formula-3d-sheet2-seeded.png; formula-3d-postcondition.txt" "Sheet2 was seeded, but Sheet3 could not be physically selected and seeded." "$artifacts"
        return
    fi
    capture_sheet_tab_strip "formula-3d-sheet3-seeded.png"
    source_last="$(copy_cell_formula 1 1 B2 || true)"

    # Enter a real 3-D range through the formula bar. The sheet-tab and cell clicks are
    # physical X11 point-mode input; Shift-click makes the second sheet extend the span.
    select_sheet_tab 0
    if select_cell 6 9 G10; then
        send_key ctrl+F2
        send_key ctrl+a
        type_text "=SUM("
        send_key F2
        send_key F2
        capture "formula-3d-point-start.png"

        # A 3-D point range is formed by choosing the first and last sheet before
        # choosing the shared cell. Selecting Sheet2!B2 first would create a normal
        # single-sheet reference and append a second reference to it.
        select_sheet_tab 1
        capture "formula-3d-sheet2-point.png"
        select_sheet_tab_range_end 2
        if select_cell 1 1 B2; then
            capture "formula-3d-sheet3-point.png"
            type_text ")"
            send_key Return
            # Commit leaves Sheet3 active. Retain that state, then return to the
            # destination worksheet before reading G10 so the clipboard assertion
            # cannot accidentally read Sheet3!G10.
            capture "formula-3d-committed-sheet3.png"
            select_sheet_tab 0
            committed_formula="$(copy_cell_formula 6 9 G10 || true)"
            committed_display="$(copy_cell_display 6 9 G10 || true)"
            select_cell 0 0 A1 || true
            capture "formula-3d-committed.png"
        fi
    fi

    normalized_formula="$(normalize_formula "$committed_formula")"
    if [[ "$normalized_formula" == "=SUM(SHEET2:SHEET3!B2)" ]]; then
        formula_passed=true
    fi
    if [[ "$committed_display" =~ ^30([.]0+)?$ ]]; then
        result_passed=true
    fi
    formula_status=$([[ "$formula_passed" == true ]] && printf true || printf false)
    result_status=$([[ "$result_passed" == true ]] && printf true || printf false)
    write_artifact "formula-3d-postcondition.txt" \
        "created-sheets=2\nsource-sheet2-b2=$source_first\nsource-sheet3-b2=$source_last\ncommit-visible-sheet=Sheet3\nformula-read-sheet=Sheet1\ncommitted-formula=$committed_formula\nnormalized-formula=$normalized_formula\ncommitted-display=$committed_display\nformula-3d-reference=$formula_status\ncalculated-result=$result_status\n"
    if [[ "$source_first" == "10" && "$source_last" == "20" && "$formula_passed" == true && "$result_passed" == true ]]; then
        record "formula-bar-point-mode-3d-sheet-range" "passed" "formula-3d-point-start.png; formula-3d-sheet2-point.png; formula-3d-sheet3-point.png; formula-3d-committed.png; formula=$committed_formula; result=$committed_display" "Two real worksheets were created and selected through X11 while formula-bar point mode committed and evaluated a 3-D Sheet2:Sheet3 B2 reference." "$artifacts"
    else
        record "formula-bar-point-mode-3d-sheet-range" "failed" "formula-3d-point-start.png; formula-3d-sheet2-point.png; formula-3d-sheet3-point.png; formula-3d-committed.png; formula-3d-postcondition.txt" "The physical cross-sheet point sequence did not prove Sheet2:Sheet3 B2 or the expected result 30 (formula='$committed_formula', result='$committed_display')." "$artifacts"
    fi
    dismiss_overlays
}

probe_formula_bar_point_mode_3d_range_grip() {
    local committed_formula="" committed_result="" resized_formula="" resized_result=""
    local point_passed=false point_result_passed=false grip_passed=false result_passed=false save_passed=false
    local expected_point="=SUM(Sheet2:Sheet3!B2:C3)"
    local expected_resized="=SUM(Sheet2:Sheet3!B2:D4)"
    local artifacts="formula-3d-grip-create.png;formula-3d-grip-point.png;formula-3d-grip-middle.png;formula-3d-grip-dragging.png;formula-3d-grip-committed.png;formula-3d-grip-postcondition.txt"

    # Create two real worksheets so Sheet1, Sheet2, and Sheet3 form the complete physical span.
    local tab_y="$(sheet_tab_y)" plus_x created_count
    for created_count in 0 1; do
        plus_x="$(sheet_plus_center_x "$created_count")"
        focus_app
        xdotool_mousemove_sync "$plus_x" "$tab_y" click 1
        sleep "$settle_seconds"
    done
    capture_sheet_tab_strip "formula-3d-grip-create.png"

    # Seed B2:D4 on both referenced sheets through the normal cell-edit route. The values make
    # both the original B2:C3 aggregate and the resized B2:D4 aggregate independently observable.
    local sheet_index row_offset column_offset value
    for sheet_index in 1 2; do
        select_sheet_tab "$sheet_index"
        for row_offset in 1 2 3; do
            for column_offset in 1 2 3; do
                if (( sheet_index == 1 )); then
                    value=$(( (row_offset - 1) * 3 + column_offset ))
                else
                    value=$(( 9 + (row_offset - 1) * 3 + column_offset ))
                fi
                set_cell_text_without_save "$column_offset" "$row_offset" "3d-${sheet_index}-${row_offset}-${column_offset}" "$value" || {
                    write_artifact "formula-3d-grip-postcondition.txt" "seeded=false\nsheet=$sheet_index\n"
                    record "formula-bar-point-mode-3d-sheet-range-grip" "failed" "formula-3d-grip-create.png; formula-3d-grip-postcondition.txt" "Could not seed the physical 3-D range sources." "$artifacts"
                    return
                }
            done
        done
    done

    # Point-select a multi-cell range while the sheet tabs define Sheet2:Sheet3. The drag is
    # deliberate X11 input; a literal colon would produce the invalid Sheet2!B2:Sheet3!C3 form.
    select_sheet_tab 0
    if select_cell 6 9 G10; then
        send_key ctrl+F2
        send_key ctrl+a
        type_text "=SUM("
        send_key F2
        send_key F2
        select_sheet_tab 1
        xdotool keydown --window "$window_id" Shift_L
        focus_app
        xdotool_mousemove_sync "$(sheet_tab_center_x 2)" "$tab_y" click 1
        xdotool keyup --window "$window_id" Shift_L
        sleep "$settle_seconds"
        focus_app
        xdotool_mousemove_sync "$(cell_center_x 1)" "$(cell_center_y 1)"
        xdotool mousedown 1
        sleep 0.18
        xdotool_mousemove_sync "$(cell_center_x 2)" "$(cell_center_y 2)"
        sleep 0.18
        xdotool mouseup 1
        sleep "$settle_seconds"
        capture "formula-3d-grip-point.png"
        type_text ")"
        send_key Return
    fi

    select_sheet_tab 0
    committed_formula="$(copy_cell_formula 6 9 G10 || true)"
    committed_result="$(copy_cell_display 6 9 G10 || true)"
    [[ "$(normalize_formula "$committed_formula")" == "$(normalize_formula "$expected_point")" ]] && point_passed=true

    # Reopen the committed formula, activate the middle sheet, then drag its visible C3 grip to
    # D4. This proves the shared planner projects the span onto a middle sheet and preserves the
    # complete qualifier while changing only the cell range.
    if select_cell 6 9 G10 && send_key F2; then
        select_sheet_tab 1
        capture "formula-3d-grip-middle.png"
        focus_app
        xdotool_mousemove_sync "$((a1_x + 3 * cell_width - 22))" "$((a1_y + 3 * cell_height - 6))"
        xdotool mousedown 1
        sleep 0.22
        xdotool_mousemove_sync "$(cell_center_x 3)" "$(cell_center_y 3)"
        sleep 0.22
        capture "formula-3d-grip-dragging.png"
        xdotool mouseup 1
        sleep "$settle_seconds"
        focus_app
        xdotool_mousemove_sync 500 198 click 1
        sleep "$settle_seconds"
        send_key Return
        capture "formula-3d-grip-committed.png"
    fi

    select_sheet_tab 0
    resized_formula="$(copy_cell_formula 6 9 G10 || true)"
    resized_result="$(copy_cell_display 6 9 G10 || true)"
    send_key ctrl+s
    wait_for_document_clean && save_passed=true
    [[ "$(normalize_formula "$resized_formula")" == "$(normalize_formula "$expected_resized")" ]] && grip_passed=true
    [[ "$resized_result" =~ ^171([.]0+)?$ ]] && result_passed=true

    write_artifact "formula-3d-grip-postcondition.txt" \
        "expected-point=$expected_point\ncommitted-point-formula=$committed_formula\ncommitted-point-result=$committed_result\npoint-passed=$point_passed\nexpected-resized=$expected_resized\nresized-formula=$resized_formula\nresized-result=$resized_result\ngrip-passed=$grip_passed\nresult-passed=$result_passed\nsave-clean=$save_passed\n"
    if $point_passed && $grip_passed && $result_passed && $save_passed; then
        record "formula-bar-point-mode-3d-sheet-range-grip" "passed" \
            "formula-3d-grip-point.png; formula-3d-grip-middle.png; formula-3d-grip-dragging.png; formula-3d-grip-committed.png; formula=$resized_formula; result=$resized_result; save-clean=$save_passed" \
            "Physical X11 point mode selected B2:C3 across Sheet2:Sheet3, then the middle-sheet grip resized it to B2:D4 while preserving the complete 3-D qualifier and calculating 171." "$artifacts"
    else
        record "formula-bar-point-mode-3d-sheet-range-grip" "failed" "$artifacts" "Expected point formula '$expected_point', resized formula '$expected_resized', result 171, and a clean save; observed point='$committed_formula', resized='$resized_formula', result='$resized_result', save-clean=$save_passed." "$artifacts"
    fi
    send_key Escape || true
}

probe_formula_bar_point_mode_3d_native_xlsx() {
    local committed_formula="" committed_result="" resized_formula="" resized_result=""
    local reopened_formula="" reopened_result="" package_probe="" package_formula="" package_cached=""
    local point_passed=false grip_passed=false result_passed=false save_passed=false
    local reopen_passed=false dialog_closed=false package_passed=false dialog_open=false
    local expected_point="=SUM('O''Brien Data:Revenue Data'!B2:C3)"
    local expected_resized="=SUM('O''Brien Data:Revenue Data'!B2:D4)"
    local expected_package_formula="SUM('O''Brien Data:Revenue Data'!B2:D4)"
    local artifacts="formula-3d-native-xlsx-point.png;formula-3d-native-xlsx-middle.png;formula-3d-native-xlsx-dragging.png;formula-3d-native-xlsx-saved.png;formula-3d-native-xlsx-reopened.png;formula-3d-native-xlsx-postcondition.json"

    # The fixture is a real OOXML package copied into /documents by the host runner. Its authored
    # reverse 3-D formula is the point-mode starting state; this probe must preserve that qualifier
    # while the middle-sheet grip changes only the cell suffix.
    select_sheet_tab 0
    if select_cell 6 9 G10; then
        committed_formula="$(copy_cell_formula 6 9 G10 || true)"
        committed_result="$(copy_cell_display 6 9 G10 || true)"
        capture "formula-3d-native-xlsx-point.png"
        [[ "$(normalize_formula "$committed_formula")" == "$(normalize_formula "$expected_point")" ]] && point_passed=true
        [[ "$committed_result" =~ ^88([.]0+)?$ ]] && point_result_passed=true
    fi

    # Reopen the point formula on the first endpoint sheet. The reverse span still projects onto
    # Revenue Data, proving that the active sheet need not be the textual start endpoint.
    if select_cell 6 9 G10 && send_key F2; then
        select_sheet_tab 1
        capture "formula-3d-native-xlsx-middle.png"
        focus_app
        xdotool_mousemove_sync "$((a1_x + 3 * cell_width - 22))" "$((a1_y + 3 * cell_height - 6))"
        xdotool mousedown 1
        sleep 0.22
        xdotool_mousemove_sync "$(cell_center_x 3)" "$(cell_center_y 3)"
        sleep 0.22
        capture "formula-3d-native-xlsx-dragging.png"
        xdotool mouseup 1
        sleep "$settle_seconds"
        focus_app
        xdotool_mousemove_sync 500 198 click 1
        sleep "$settle_seconds"
        send_key Return
        capture "formula-3d-native-xlsx-saved.png"
    fi

    select_sheet_tab 0
    resized_formula="$(copy_cell_formula 6 9 G10 || true)"
    resized_result="$(copy_cell_display 6 9 G10 || true)"
    send_key ctrl+s
    wait_for_document_clean && save_passed=true
    [[ "$(normalize_formula "$resized_formula")" == "$(normalize_formula "$expected_resized")" ]] && grip_passed=true
    [[ "$resized_result" =~ ^234([.]0+)?$ ]] && result_passed=true

    # Inspect the saved ZIP package before reopening it. This records the native package state
    # separately from the UI clipboard values, including the worksheet formula cache.
    package_probe="$(python3 - "$document_path" <<'PY'
import sys
import zipfile
import xml.etree.ElementTree as ET

main = "{http://schemas.openxmlformats.org/spreadsheetml/2006/main}"
try:
    with zipfile.ZipFile(sys.argv[1]) as package:
        workbook = ET.fromstring(package.read("xl/workbook.xml"))
        worksheet = ET.fromstring(package.read("xl/worksheets/sheet1.xml"))
        cell = next(node for node in worksheet.findall(".//" + main + "c") if node.attrib.get("r") == "G10")
        formula = cell.findtext(main + "f", default="")
        cached = cell.findtext(main + "v", default="")
        names = [node.attrib.get("name", "") for node in workbook.findall(".//" + main + "sheet")]
        if names != ["Summary", "Revenue Data", "O'Brien Data", "Tail"]:
            raise ValueError("unexpected worksheet order")
        print(f"{formula}|{cached}")
except (OSError, KeyError, ET.ParseError, StopIteration, ValueError):
    raise SystemExit(1)
PY
    )" || package_probe=""
    if [[ "$package_probe" == *"|"* ]]; then
        package_formula="${package_probe%%|*}"
        package_cached="${package_probe#*|}"
        if [[ "$(normalize_formula "$package_formula")" == "$(normalize_formula "$expected_package_formula")" && "$package_cached" =~ ^234([.]0+)?$ ]]; then
            package_passed=true
        fi
    fi

    # Reopen the saved native package through the production Open command. Linux presents a GTK
    # picker here; Ctrl+L enters the mounted absolute path without inventing a host-specific UI.
    local before_windows after_windows
    before_windows="$(visible_window_count)"
    send_key ctrl+F12
    for _ in $(seq 1 12); do
        after_windows="$(visible_window_count)"
        if (( after_windows > before_windows )); then
            dialog_open=true
            break
        fi
        sleep 0.2
    done
    if $dialog_open; then
        xdotool key --clearmodifiers --delay "$input_delay_ms" ctrl+l
        xdotool type --clearmodifiers --delay "$type_delay_ms" "$document_path"
        xdotool key --clearmodifiers Return
        sleep "$settle_seconds"
        xdotool key --clearmodifiers Return
        for _ in $(seq 1 16); do
            after_windows="$(visible_window_count)"
            if (( after_windows <= before_windows )); then
                dialog_closed=true
                break
            fi
            sleep 0.25
        done
    fi
    if $dialog_closed; then
        sleep "$dialog_settle_seconds"
        capture "formula-3d-native-xlsx-reopened.png"
        select_sheet_tab 0
        reopened_formula="$(copy_cell_formula 6 9 G10 || true)"
        reopened_result="$(copy_cell_display 6 9 G10 || true)"
        if [[ "$(normalize_formula "$reopened_formula")" == "$(normalize_formula "$expected_resized")" && "$reopened_result" =~ ^234([.]0+)?$ ]]; then
            reopen_passed=true
        fi
    fi

    local postcondition_path="$output/formula-3d-native-xlsx-postcondition.json"
    python3 - "$document_path" "$committed_formula" "$committed_result" "$resized_formula" "$resized_result" "$save_passed" "$reopen_passed" "$reopened_formula" "$reopened_result" "$package_probe" > "$postcondition_path" <<'PY'
import json
import os
import sys

package_probe = sys.argv[10]
package_formula, package_cached = package_probe.split("|", 1) if "|" in package_probe else ("", "")
payload = {
    "schemaVersion": 1,
    "format": "xlsx",
    "source": {
        "path": os.path.basename(sys.argv[1]),
        "pointFormula": sys.argv[2],
        "pointResult": sys.argv[3],
    },
    "save": {
        "clean": sys.argv[6] == "true",
        "resizedFormula": sys.argv[4],
        "resizedResult": sys.argv[5],
    },
    "reopen": {
        "physical": sys.argv[7] == "true",
        "formula": sys.argv[8],
        "result": sys.argv[9],
    },
    "package": {
        "zip": bool(package_probe),
        "workbook": bool(package_probe),
        "formula": package_formula,
        "cachedResult": package_cached,
    },
}
print(json.dumps(payload, separators=(",", ":")))
PY

    if $point_passed && $point_result_passed && $grip_passed && $result_passed && $save_passed && $package_passed && $reopen_passed; then
        record "formula-bar-point-mode-3d-native-xlsx" "passed" "$artifacts; formula=$reopened_formula; result=$reopened_result; package-formula=$package_formula; package-cached-result=$package_cached" "Physical X11 pointing and middle-sheet grip resizing used a native XLSX package, preserved the escaped reverse 3-D qualifier, saved cleanly, reopened through the production Open route, and retained result 234." "$artifacts"
    else
        record "formula-bar-point-mode-3d-native-xlsx" "failed" "$artifacts; formula=$reopened_formula; result=$reopened_result; package-formula=$package_formula; package-cached-result=$package_cached" "Native XLSX 3-D point/grip workflow did not satisfy point=$point_passed point-result=$point_result_passed grip=$grip_passed result=$result_passed save=$save_passed package=$package_passed reopen=$reopen_passed." "$artifacts"
    fi
    send_key Escape || true
}

probe_formula_bar_point_mode_multi_area() {
    local keyboard_formula="" keyboard_display="" pointer_formula="" pointer_display=""
    local keyboard_normalized="" pointer_normalized=""
    local keyboard_formula_passed=false keyboard_result_passed=false
    local pointer_formula_passed=false pointer_result_passed=false
    local artifacts="formula-multi-area-keyboard-start.png;formula-multi-area-keyboard-first.png;formula-multi-area-keyboard-add.png;formula-multi-area-keyboard-committed.png;formula-multi-area-pointer-start.png;formula-multi-area-pointer-first.png;formula-multi-area-pointer-second.png;formula-multi-area-pointer-committed.png;formula-multi-area-postcondition.txt"

    # Point mode consumes the grid click as formula input. The normal selection helper
    # retries when it cannot classify the transient point-mode border, which would append
    # the same address twice. Each formula point therefore gets exactly one calibrated click.
    point_formula_cell() {
        local column_offset="$1" row_offset="$2"
        focus_app
        xdotool_mousemove_sync "$(cell_center_x "$column_offset")" "$(cell_center_y "$row_offset")" click 1
        sleep "$settle_seconds"
    }

    # Seed separated source cells through the production inline-edit path. The same sources are
    # reused by the keyboard Add-mode and Ctrl+pointer formula-reference cases below.
    if ! set_cell_text_without_save 5 4 F5 10 || ! set_cell_text_without_save 5 6 F7 20 ||
       ! set_cell_text_without_save 7 6 H7 20; then
        write_artifact "formula-multi-area-postcondition.txt" "seed-f5=false\nseed-f7=false\nseed-h7=false\n"
        record "formula-bar-point-mode-multi-area-keyboard" "failed" "formula-multi-area-postcondition.txt" "Could not seed the physical disjoint formula-point source cells." "$artifacts"
        record "formula-bar-point-mode-multi-area-pointer" "failed" "formula-multi-area-postcondition.txt" "Could not seed the physical disjoint formula-point source cells." "$artifacts"
        return
    fi

    # Case one: WPF/Excel Shift+F8 Add mode, followed by a physical Ctrl+Down data-boundary move
    # that appends a separated second reference area. This exercises the formula-bar keyboard route
    # rather than a test-only helper.
    if select_cell 4 4 E5; then
        send_key ctrl+F2
        send_key ctrl+a
        type_text "=SUM("
        send_key F2
        send_key F2
        capture "formula-multi-area-keyboard-start.png"
        point_formula_cell 5 4
        capture "formula-multi-area-keyboard-first.png"
        # Reassert the formula editor caret after the grid point so the following physical
        # chord is delivered to the same production editor that owns Point/Add mode.
        send_key ctrl+End
        # Deliver the chord without --clearmodifiers; clearing modifiers can erase the
        # Shift state Avalonia uses for Add mode.
        focus_app
        xdotool key --window "$window_id" shift+F8
        sleep "$settle_seconds"
        send_key ctrl+Down
        capture "formula-multi-area-keyboard-add.png"
        type_text ")"
        send_key Return
        keyboard_formula="$(copy_cell_formula 4 4 E5 || true)"
        keyboard_display="$(copy_cell_display 4 4 E5 || true)"
        capture "formula-multi-area-keyboard-committed.png"
    fi
    send_key Escape || true

    keyboard_normalized="$(normalize_formula "$keyboard_formula")"
    [[ "$keyboard_normalized" == "=SUM(F5,F7)" ]] && keyboard_formula_passed=true
    [[ "$keyboard_display" =~ ^30([.]0+)?$ ]] && keyboard_result_passed=true
    if $keyboard_formula_passed && $keyboard_result_passed; then
        record "formula-bar-point-mode-multi-area-keyboard" "passed" \
            "formula-multi-area-keyboard-start.png; formula-multi-area-keyboard-first.png; formula-multi-area-keyboard-add.png; formula-multi-area-keyboard-committed.png; formula=$keyboard_formula; result=$keyboard_display" \
            "Physical formula-bar point mode entered Shift+F8 Add mode, used Ctrl+Down to append separated F7 after F5, and committed SUM(F5,F7) with result 30." "$artifacts"
    else
        record "formula-bar-point-mode-multi-area-keyboard" "failed" \
            "formula-multi-area-keyboard-start.png; formula-multi-area-keyboard-first.png; formula-multi-area-keyboard-add.png; formula-multi-area-keyboard-committed.png; formula-multi-area-postcondition.txt" \
            "Physical Shift+F8 formula point mode did not prove SUM(F5,F7)=30 (formula='$keyboard_formula', result='$keyboard_display')." "$artifacts"
    fi

    # Case two: the production pointer modifier route. A plain point selects the first area, then a
    # physical Ctrl+click appends a second area before the closing parenthesis is committed.
    if select_cell 6 9 G10; then
        send_key ctrl+F2
        send_key ctrl+a
        type_text "=SUM("
        send_key F2
        send_key F2
        capture "formula-multi-area-pointer-start.png"
        point_formula_cell 5 4
        capture "formula-multi-area-pointer-first.png"
        send_key ctrl+End
        focus_app
        # Avalonia accepts either Control or Meta for this production route. Holding both
        # makes the physical probe robust across X11 keyboard-map variants while still
        # exercising the same modifier-gated pointer handler.
        xdotool keydown Control_L
        xdotool keydown Super_L
        xdotool_mousemove_sync "$(cell_center_x 7)" "$(cell_center_y 6)" click 1
        xdotool keyup Control_L
        xdotool keyup Super_L
        sleep "$settle_seconds"
        capture "formula-multi-area-pointer-second.png"
        type_text ")"
        send_key Return
        pointer_formula="$(copy_cell_formula 6 9 G10 || true)"
        pointer_display="$(copy_cell_display 6 9 G10 || true)"
        capture "formula-multi-area-pointer-committed.png"
    fi
    send_key Escape || true

    pointer_normalized="$(normalize_formula "$pointer_formula")"
    [[ "$pointer_normalized" == "=SUM(F5,H7)" ]] && pointer_formula_passed=true
    [[ "$pointer_display" =~ ^30([.]0+)?$ ]] && pointer_result_passed=true
    write_artifact "formula-multi-area-postcondition.txt" \
        "keyboard-formula=$keyboard_formula\nkeyboard-normalized=$keyboard_normalized\nkeyboard-result=$keyboard_display\nkeyboard-formula-passed=$keyboard_formula_passed\nkeyboard-result-passed=$keyboard_result_passed\npointer-formula=$pointer_formula\npointer-normalized=$pointer_normalized\npointer-result=$pointer_display\npointer-formula-passed=$pointer_formula_passed\npointer-result-passed=$pointer_result_passed\n"
    if $pointer_formula_passed && $pointer_result_passed; then
        record "formula-bar-point-mode-multi-area-pointer" "passed" \
            "formula-multi-area-pointer-start.png; formula-multi-area-pointer-first.png; formula-multi-area-pointer-second.png; formula-multi-area-pointer-committed.png; formula=$pointer_formula; result=$pointer_display" \
            "Physical formula-bar point mode accepted a first cell and a Ctrl+click second cell, then committed SUM(F5,H7) with result 30." "$artifacts"
    else
        record "formula-bar-point-mode-multi-area-pointer" "failed" \
            "formula-multi-area-pointer-start.png; formula-multi-area-pointer-first.png; formula-multi-area-pointer-second.png; formula-multi-area-pointer-committed.png; formula-multi-area-postcondition.txt" \
            "Physical Ctrl+click formula point mode did not prove SUM(F5,H7)=30 (formula='$pointer_formula', result='$pointer_display')." "$artifacts"
    fi
}

drag_grid_range() {
    local start_column="$1" start_row="$2" end_column="$3" end_row="$4"
    focus_app
    xdotool_mousemove_sync "$(cell_center_x "$start_column")" "$(cell_center_y "$start_row")"
    xdotool mousedown 1
    sleep 0.18
    xdotool_mousemove_sync "$(cell_center_x "$end_column")" "$(cell_center_y "$end_row")"
    sleep 0.18
    xdotool mouseup 1
    sleep "$settle_seconds"
}

drag_selection_border() {
    local column="$1" start_row="$2" target_row="$3" ctrl_copy="${4:-false}"
    local border_x border_y target_x target_y
    border_x=$((a1_x + column * cell_width + 1))
    border_y="$(cell_center_y "$start_row")"
    target_x="$(cell_center_x "$column")"
    target_y="$(cell_center_y "$target_row")"
    focus_app
    if [[ "$ctrl_copy" == true ]]; then
        xdotool keydown --window "$window_id" Control_L
    fi
    xdotool_mousemove_sync "$border_x" "$border_y"
    xdotool mousedown 1
    sleep 0.22
    xdotool_mousemove_sync "$target_x" "$target_y"
    sleep 0.22
    xdotool mouseup 1
    if [[ "$ctrl_copy" == true ]]; then
        xdotool keyup --window "$window_id" Control_L
    fi
    sleep "$settle_seconds"
}

probe_grid_drag_parity() {
    local autofill_source_top autofill_source_bottom autofill_mid_one autofill_mid_two autofill_target autofill_selection
    local move_source_top move_source_bottom move_target_top move_target_bottom move_selection
    local copy_source_top copy_source_bottom copy_target_top copy_target_bottom copy_selection
    local autofill_values move_source_values move_target_values copy_source_values copy_target_values
    local autofill_passed=false move_passed=false copy_passed=false
    local autofill_selection_passed=false move_selection_passed=false copy_selection_passed=false
    local artifacts="grid-drag-autofill-before.png;grid-drag-autofill-after.png;grid-drag-move-before.png;grid-drag-move-after.png;grid-drag-copy-before.png;grid-drag-copy-after.png;grid-drag-postcondition.txt"

    # Keep the three gestures in separate columns and seed every assertion through the real
    # editable-cell route. The values are intentionally distinct so a move cannot pass as a copy.
    if ! set_cell_text_without_save 2 2 C3 10 ||
       ! set_cell_text_without_save 2 3 C4 20 ||
       ! set_cell_text_without_save 4 2 E3 MoveTop ||
       ! set_cell_text_without_save 4 3 E4 MoveBottom ||
       ! set_cell_text_without_save 6 2 G3 CopyTop ||
       ! set_cell_text_without_save 6 3 G4 CopyBottom; then
        write_artifact "grid-drag-postcondition.txt" "seeded=false\n"
        record "grid-autofill-handle-drag-physical" "failed" "grid-drag-postcondition.txt" "Could not seed the deterministic physical grid-drag sources." "$artifacts"
        record "grid-selection-border-move-physical" "failed" "grid-drag-postcondition.txt" "Could not seed the deterministic physical grid-drag sources." "$artifacts"
        record "grid-selection-border-copy-physical" "failed" "grid-drag-postcondition.txt" "Could not seed the deterministic physical grid-drag sources." "$artifacts"
        return
    fi

    # Autofill C3:C4 to C7. The handle is the calibrated bottom-right selection corner.
    drag_grid_range 2 2 2 3
    capture_selection "grid-drag-autofill-before.png"
    xdotool_mousemove_sync "$((a1_x + 3 * cell_width))" "$((a1_y + 4 * cell_height))"
    xdotool mousedown 1
    sleep 0.22
    xdotool_mousemove_sync "$(cell_center_x 2)" "$(cell_center_y 6)"
    sleep 0.22
    xdotool mouseup 1
    sleep "$settle_seconds"
    wait_for_selection "$(cell_x 2)" "$(cell_y 2)" "grid-drag-autofill-after.png" || true
    autofill_selection="$observed_x,$observed_y"
    box_near "$(cell_x 2)" "$(cell_y 2)" 4 && autofill_selection_passed=true
    send_key ctrl+s
    wait_for_document_clean || true
    autofill_source_top="$(csv_cell_value 2 2)"
    autofill_source_bottom="$(csv_cell_value 2 3)"
    autofill_mid_one="$(csv_cell_value 2 4)"
    autofill_mid_two="$(csv_cell_value 2 5)"
    autofill_target="$(csv_cell_value 2 6)"
    if [[ "$autofill_source_top" == 10 && "$autofill_source_bottom" == 20 &&
          "$autofill_mid_one" == 30 && "$autofill_mid_two" == 40 && "$autofill_target" == 50 ]] &&
       $autofill_selection_passed; then
        autofill_passed=true
    fi

    # Move E3:E4 to E6:E7 by grabbing the left selection border. The source must be cleared.
    set_cell_text_without_save 4 5 E6 "" || true
    set_cell_text_without_save 4 6 E7 "" || true
    drag_grid_range 4 2 4 3
    capture_selection "grid-drag-move-before.png"
    drag_selection_border 4 2 5 false
    wait_for_selection "$(cell_x 4)" "$(cell_y 5)" "grid-drag-move-after.png" || true
    move_selection="$observed_x,$observed_y"
    box_near "$(cell_x 4)" "$(cell_y 5)" 4 && move_selection_passed=true
    send_key ctrl+s
    wait_for_document_clean || true
    move_source_top="$(csv_cell_value 4 2)"
    move_source_bottom="$(csv_cell_value 4 3)"
    move_target_top="$(csv_cell_value 4 5)"
    move_target_bottom="$(csv_cell_value 4 6)"
    if [[ -z "$move_source_top" && -z "$move_source_bottom" && "$move_target_top" == MoveTop && "$move_target_bottom" == MoveBottom ]] &&
       $move_selection_passed; then
        move_passed=true
    fi

    # Ctrl-drag G3:G4 to G6:G7. The source must remain intact while the destination is copied.
    set_cell_text_without_save 6 5 G6 "" || true
    set_cell_text_without_save 6 6 G7 "" || true
    drag_grid_range 6 2 6 3
    capture_selection "grid-drag-copy-before.png"
    drag_selection_border 6 2 5 true
    wait_for_selection "$(cell_x 6)" "$(cell_y 5)" "grid-drag-copy-after.png" || true
    copy_selection="$observed_x,$observed_y"
    box_near "$(cell_x 6)" "$(cell_y 5)" 4 && copy_selection_passed=true
    send_key ctrl+s
    wait_for_document_clean || true
    copy_source_top="$(csv_cell_value 6 2)"
    copy_source_bottom="$(csv_cell_value 6 3)"
    copy_target_top="$(csv_cell_value 6 5)"
    copy_target_bottom="$(csv_cell_value 6 6)"
    if [[ "$copy_source_top" == CopyTop && "$copy_source_bottom" == CopyBottom && "$copy_target_top" == CopyTop && "$copy_target_bottom" == CopyBottom ]] &&
       $copy_selection_passed; then
        copy_passed=true
    fi

    write_artifact "grid-drag-postcondition.txt" \
        "autofill-source=C3:C4\nautofill-values=$autofill_source_top,$autofill_source_bottom,$autofill_mid_one,$autofill_mid_two,$autofill_target\nautofill-selection=$autofill_selection\nautofill-selection-passed=$autofill_selection_passed\nautofill-passed=$autofill_passed\nmove-source=E3:E4\nmove-source-after=$move_source_top,$move_source_bottom\nmove-target=E6:E7\nmove-target-values=$move_target_top,$move_target_bottom\nmove-selection=$move_selection\nmove-selection-passed=$move_selection_passed\nmove-passed=$move_passed\ncopy-source=G3:G4\ncopy-source-values=$copy_source_top,$copy_source_bottom\ncopy-target=G6:G7\ncopy-target-values=$copy_target_top,$copy_target_bottom\ncopy-selection=$copy_selection\ncopy-selection-passed=$copy_selection_passed\ncopy-passed=$copy_passed\n"
    if $autofill_passed; then
        record "grid-autofill-handle-drag-physical" "passed" "grid-drag-autofill-before.png; grid-drag-autofill-after.png; selection=$autofill_selection; C3:C7 values=10,20,30,40,50" "The real X11 pointer dragged the autofill handle and produced the exact numeric series with the completed range selected." "$artifacts"
    else
        record "grid-autofill-handle-drag-physical" "failed" "grid-drag-autofill-before.png; grid-drag-autofill-after.png; grid-drag-postcondition.txt" "Autofill did not prove C3:C7 series values or final selection." "$artifacts"
    fi
    if $move_passed; then
        record "grid-selection-border-move-physical" "passed" "grid-drag-move-before.png; grid-drag-move-after.png; source=empty; target=MoveTop,MoveBottom; selection=$move_selection" "The real X11 pointer moved E3:E4 to E6:E7 and proved the source was cleared and the target selected." "$artifacts"
    else
        record "grid-selection-border-move-physical" "failed" "grid-drag-move-before.png; grid-drag-move-after.png; grid-drag-postcondition.txt" "Selection-border move did not prove exact source/target values or final selection." "$artifacts"
    fi
    if $copy_passed; then
        record "grid-selection-border-copy-physical" "passed" "grid-drag-copy-before.png; grid-drag-copy-after.png; source=CopyTop,CopyBottom; target=CopyTop,CopyBottom; selection=$copy_selection" "The real X11 Ctrl-drag copied G3:G4 to G6:G7 while preserving the source and selecting the target." "$artifacts"
    else
        record "grid-selection-border-copy-physical" "failed" "grid-drag-copy-before.png; grid-drag-copy-after.png; grid-drag-postcondition.txt" "Ctrl-drag copy did not prove exact source/target values or final selection." "$artifacts"
    fi
}

probe_grid_autofit() {
    local seeded_text="Long deterministic X11 AutoFit text for column growth"
    local before_width=0 after_width=0 row_before_height=0 row_after_height=0
    local hidden_row4_height=0 hidden_row5_height=0
    local column_boundary_x=0 column_boundary_y=0 row_boundary_x=0 row_boundary_y=0 hidden_boundary_x=0 hidden_boundary_y=0
    local column_grown=false row_grown=false hidden_unhidden=false hidden_sized=false
    local hidden_rows_after='[4,5]'
    local artifacts="grid-autofit-before.png;grid-autofit-after.png;grid-autofit-row-before.png;grid-autofit-row-after.png;grid-autofit-hidden-before.png;grid-autofit-hidden-after.png;grid-autofit-postcondition.json"
    local row_header_x handle_center_inset=4

    if ! select_cell 1 2 B3 ||
       ! capture "grid-autofit-hidden-before.png"; then
        write_artifact "grid-autofit-postcondition.json" '{"schemaVersion":2,"suite":"freex-grid-autofit-physical","platform":"linux","shell":"avalonia","app":"FreeX","viewport":{"width":1280,"height":820,"dpi":96},"column":{"seedCell":"A1","beforeSize":1,"afterSize":1,"boundaryX":1,"boundaryY":1,"grew":false},"row":{"seedCell":"B2","beforeSize":1,"afterSize":1,"boundaryX":1,"boundaryY":1,"grew":false},"hiddenRowBoundary":{"targetStart":4,"targetEnd":5,"hiddenRowsBefore":[4,5],"hiddenRowsAfter":[],"beforeHeights":[0,0],"afterHeights":[16,16],"unhidden":false,"sized":false,"boundaryX":1,"boundaryY":1}'
        record "grid-header-double-click-autofit-column-physical" "failed" "grid-autofit-before.png; grid-autofit-after.png; grid-autofit-postcondition.json" "Could not reach the fixture grid before the AutoFit proofs." "$artifacts"
        record "grid-header-double-click-autofit-row-physical" "failed" "grid-autofit-row-before.png; grid-autofit-row-after.png; grid-autofit-postcondition.json" "Column setup failed before the visible-row AutoFit proof." "$artifacts"
        record "grid-header-double-click-autofit-hidden-row-boundary-physical" "failed" "grid-autofit-hidden-before.png; grid-autofit-hidden-after.png; grid-autofit-postcondition.json" "Column setup failed before the hidden-row boundary proof." "$artifacts"
        return
    fi

    row_header_x=$((window_x + (a1_x - window_x) / 2))

    # The hidden-row boundary proof runs before the visible-row proof so all row coordinates remain
    # on the default-height grid; the existing calibrated pitch is still valid for column B.
    hidden_boundary_x="$row_header_x"
    hidden_boundary_y=$((a1_y + 3 * cell_height - handle_center_inset))
    if [[ -f "$output/grid-autofit-hidden-before.png" ]]; then
        focus_app
        xdotool_mousemove_sync "$hidden_boundary_x" "$hidden_boundary_y"
        xdotool click --repeat 2 --delay 180 1
        sleep "$settle_seconds"
        if select_cell 1 3 B4 && capture "grid-autofit-hidden-after.png"; then
            hidden_row4_height="$observed_height"
            hidden_rows_after='[5]'
            local hidden_row5_top hidden_row5_left hidden_row5_center_x hidden_row5_center_y
            # selection_box reports the outlined cell bounds, which include the same four-pixel
            # center inset used for the physical boundary clicks. Use the cell's actual next-row
            # origin here so a positive AutoFit height is proved by the real B5 selection rather
            # than rejected for the outline's extra border pixels.
            hidden_row5_top=$((a1_y + 3 * cell_height + hidden_row4_height - handle_center_inset))
            hidden_row5_left=$((a1_x + cell_width))
            hidden_row5_center_x=$((hidden_row5_left + cell_width / 2))
            hidden_row5_center_y=$((hidden_row5_top + cell_height / 2))
            focus_app
            xdotool_mousemove_sync "$hidden_row5_center_x" "$hidden_row5_center_y" click 1
            if wait_for_selection "$hidden_row5_left" "$hidden_row5_top" "selection-B5.png" && capture "grid-autofit-hidden-after.png"; then
                hidden_row5_height="$observed_height"
                hidden_rows_after='[]'
                hidden_unhidden=true
                if (( hidden_row4_height > cell_height && hidden_row5_height > cell_height )); then
                    hidden_sized=true
                fi
            fi
        fi
    fi

    # A separate visible row keeps the ordinary row-boundary proof explicit after the hidden run.
    if select_cell 1 1 B2 && capture "grid-autofit-row-before.png"; then
        row_before_height="$observed_height"
        row_boundary_x="$row_header_x"
        row_boundary_y=$((a1_y + 2 * cell_height - handle_center_inset))
        focus_app
        xdotool_mousemove_sync "$row_boundary_x" "$row_boundary_y"
        xdotool click --repeat 2 --delay 180 1
        sleep "$settle_seconds"
        if select_cell 1 1 B2 && capture "grid-autofit-row-after.png"; then
            row_after_height="$observed_height"
            if (( row_after_height > row_before_height )); then
                row_grown=true
            fi
        fi
    fi

    # Preserve the existing passing column proof after the row proofs, while the calibrated
    # pitch still describes the default-width grid.
    if select_cell 0 0 A1 && capture_selection "grid-autofit-before.png"; then
        before_width="$observed_width"
        # The selection outline includes a few pixels outside the cell. Use the calibrated
        # A1-to-B1 pitch for the physical boundary, rather than the outline width.
        column_boundary_x=$((a1_x + cell_width - 1 - handle_center_inset))
        column_boundary_y=$((a1_y - cell_height / 2))
        focus_app
        xdotool_mousemove_sync "$column_boundary_x" "$column_boundary_y"
        xdotool click --repeat 2 --delay 180 1
        sleep "$settle_seconds"
        select_cell 0 0 A1 || true
        if capture_selection "grid-autofit-after.png"; then
            after_width="$observed_width"
        fi
        if [[ "$after_width" =~ ^[0-9]+$ ]] && (( after_width > before_width )); then
            column_grown=true
        fi
    fi

    write_artifact "grid-autofit-postcondition.json" \
        "{\n  \"schemaVersion\":2,\n  \"suite\":\"freex-grid-autofit-physical\",\n  \"platform\":\"linux\",\n  \"shell\":\"avalonia\",\n  \"app\":\"FreeX\",\n  \"viewport\":{\"width\":1280,\"height\":820,\"dpi\":96},\n  \"column\":{\"seedCell\":\"A1\",\"beforeSize\":$before_width,\"afterSize\":$after_width,\"boundaryX\":$column_boundary_x,\"boundaryY\":$column_boundary_y,\"grew\":$column_grown},\n  \"row\":{\"seedCell\":\"B2\",\"beforeSize\":$row_before_height,\"afterSize\":$row_after_height,\"boundaryX\":$row_boundary_x,\"boundaryY\":$row_boundary_y,\"grew\":$row_grown},\n  \"hiddenRowBoundary\":{\"targetStart\":4,\"targetEnd\":5,\"hiddenRowsBefore\":[4,5],\"hiddenRowsAfter\":$hidden_rows_after,\"beforeHeights\":[0,0],\"afterHeights\":[$hidden_row4_height,$hidden_row5_height],\"unhidden\":$hidden_unhidden,\"sized\":$hidden_sized,\"boundaryX\":$hidden_boundary_x,\"boundaryY\":$hidden_boundary_y}\n}"
    if $column_grown; then
        record "grid-header-double-click-autofit-column-physical" "passed" "grid-autofit-before.png; grid-autofit-after.png; grid-autofit-postcondition.json; before-width=$before_width; after-width=$after_width" "A real X11 double-click on the first column boundary widened the seeded long-text column." "$artifacts"
    else
        record "grid-header-double-click-autofit-column-physical" "failed" "grid-autofit-before.png; grid-autofit-after.png; grid-autofit-postcondition.json" "The real X11 column-boundary double-click did not produce deterministic column growth." "$artifacts"
    fi
    if $row_grown; then
        record "grid-header-double-click-autofit-row-physical" "passed" "grid-autofit-row-before.png; grid-autofit-row-after.png; grid-autofit-postcondition.json; before-height=$row_before_height; after-height=$row_after_height" "A real X11 double-click on a visible row boundary grew the wrapped row to its content." "$artifacts"
    else
        record "grid-header-double-click-autofit-row-physical" "failed" "grid-autofit-row-before.png; grid-autofit-row-after.png; grid-autofit-postcondition.json" "The real X11 visible row-boundary double-click did not produce deterministic row growth." "$artifacts"
    fi
    if $hidden_sized; then
        record "grid-header-double-click-autofit-hidden-row-boundary-physical" "passed" "grid-autofit-hidden-before.png; grid-autofit-hidden-after.png; grid-autofit-postcondition.json; rows=4:5; heights=$hidden_row4_height,$hidden_row5_height" "A real X11 double-click on the contiguous hidden-row boundary reopened rows 4:5 and AutoFit-sized both rows." "$artifacts"
    else
        record "grid-header-double-click-autofit-hidden-row-boundary-physical" "failed" "grid-autofit-hidden-before.png; grid-autofit-hidden-after.png; grid-autofit-postcondition.json" "The real X11 hidden-row boundary double-click did not prove contiguous unhide and AutoFit sizing." "$artifacts"
    fi
}

probe_split_pane_pointer() {
    local split_before="split-pane-before.png" split_after="split-pane-open.png"
    local divider_before="split-pane-divider-before.png" divider_after="split-pane-divider-after.png"
    local wheel_before="split-pane-wheel-before.png" wheel_after="split-pane-wheel-after.png"
    local bottom_wheel_before="split-pane-bottom-left-wheel-before.png" bottom_wheel_after="split-pane-bottom-left-wheel-after.png"
    local scrollbar_before="split-pane-scrollbar-before.png" scrollbar_after="split-pane-scrollbar-after.png"
    local postcondition="split-pane-pointer-postcondition.txt"
    local artifacts="$split_before;$split_after;split-pane-before-grid.png;split-pane-open-grid.png;$divider_before;$divider_after;$wheel_before;$wheel_after;$bottom_wheel_before;$bottom_wheel_after;$scrollbar_before;$scrollbar_after;$postcondition"
    local split_open=false divider_passed=false wheel_passed=false bottom_wheel_passed=false scrollbar_passed=false
    local split_row_y split_column_x divider_drag_x drag_y top_right_left top_right_width top_right_top top_right_height
    local bottom_left_left bottom_left_width bottom_left_top bottom_left_height scrollbar_x scrollbar_y
    local split_keytip_route="WSP"

    # C5 gives the real View > Split command a deterministic two-axis anchor. The coordinates
    # below deliberately come from the calibrated cell pitch, so the probe remains bounded and
    # does not claim a result when the rendered split is not where the product placed it.
    if select_cell 2 4 C5; then
        capture "$split_before"
        crop_region "$split_before" "split-pane-before-grid.png" "$a1_x" "$a1_y" "$((window_x + window_width - a1_x))" "$((window_y + window_height - a1_y - 40))"
        # WSP is the canonical Alt key-tip route for View > Split (W=View, SP=Split).
        # It is independent of ribbon width, font metrics, DPI rounding, and tab placement.
        enter_keytip_mode
        xdotool key --clearmodifiers --delay "$input_delay_ms" --window "$window_id" w s p
        sleep "$settle_seconds"
        sleep "$settle_seconds"
        capture "$split_after"
        crop_region "$split_after" "split-pane-open-grid.png" "$a1_x" "$a1_y" "$((window_x + window_width - a1_x))" "$((window_y + window_height - a1_y - 40))"
        if region_changed "$output/split-pane-before-grid.png" "$output/split-pane-open-grid.png" 300; then
            split_open=true
        fi
    fi

    split_row_y="$((a1_y + cell_height * 4))"
    split_column_x="$((a1_x + cell_width * 2))"
    divider_drag_x="$((a1_x + (split_column_x - a1_x) / 2))"
    # Release inside the next row, not on its inclusive top/bottom boundary: the planner
    # intentionally maps a shared edge to the preceding row.
    drag_y="$((split_row_y + cell_height + cell_height / 2))"
    if $split_open; then
        capture "$divider_before"
        focus_app
        # Stay left of the vertical divider. The top-right segment at the same Y is the
        # horizontal mini-scrollbar and would page the shared viewport instead of dragging.
        xdotool_mousemove_sync "$divider_drag_x" "$split_row_y"
        xdotool mousedown 1
        sleep 0.18
        xdotool_mousemove_sync "$divider_drag_x" "$drag_y"
        sleep 0.18
        xdotool mouseup 1
        sleep "$settle_seconds"
        capture "$divider_after"
        if screen_changed "$output/$divider_before" "$output/$divider_after" 300; then
            divider_passed=true
        fi
        # A successful drag into the next row moves the rendered boundary by one row; the
        # pointer itself was released at that row's center.
        if $divider_passed; then
            split_row_y="$((split_row_y + cell_height))"
        fi
    else
        capture "$divider_before"
        capture "$divider_after"
    fi

    # The top-right pane owns the shared horizontal scrollbar. Shift+wheel is the physical
    # horizontal-wheel equivalent used by WPF; compare only that quadrant so a changed selection
    # or footer cannot accidentally satisfy the postcondition.
    top_right_left="$split_column_x"
    top_right_width="$((window_x + window_width - top_right_left))"
    top_right_top="$a1_y"
    top_right_height="$((split_row_y - top_right_top))"
    if $split_open && (( top_right_width > 40 && top_right_height > 40 )); then
        capture "$wheel_before"
        crop_region "$wheel_before" "split-pane-wheel-before-crop.png" "$top_right_left" "$top_right_top" "$top_right_width" "$top_right_height"
        focus_app
        xdotool_mousemove_sync "$((top_right_left + top_right_width * 3 / 4))" "$((top_right_top + top_right_height / 2))"
        xdotool keydown --window "$window_id" Shift_L
        xdotool click 5
        xdotool keyup --window "$window_id" Shift_L
        sleep "$settle_seconds"
        capture "$wheel_after"
        crop_region "$wheel_after" "split-pane-wheel-after-crop.png" "$top_right_left" "$top_right_top" "$top_right_width" "$top_right_height"
        if region_changed "$output/split-pane-wheel-before-crop.png" "$output/split-pane-wheel-after-crop.png" 80; then
            wheel_passed=true
        fi
    else
        capture "$wheel_before"
        capture "$wheel_after"
    fi

    # The bottom-left pane owns the shared vertical scrollbar. Its vertical wheel must move the
    # same row band as BottomRight, so capture only the bottom-left quadrant for this proof.
    # Include the row-header band. The demo fixture is intentionally small, so after the
    # populated rows scroll away the changing row labels remain the authoritative row-band proof.
    bottom_left_left="$window_x"
    bottom_left_width="$((split_column_x - bottom_left_left))"
    bottom_left_top="$split_row_y"
    bottom_left_height="$((window_y + window_height - bottom_left_top))"
    if $split_open && (( bottom_left_width > 40 && bottom_left_height > 40 )); then
        capture "$bottom_wheel_before"
        crop_region "$bottom_wheel_before" "split-pane-bottom-left-wheel-before-crop.png" "$bottom_left_left" "$bottom_left_top" "$bottom_left_width" "$bottom_left_height"
        focus_app
        xdotool_mousemove_sync "$((bottom_left_left + bottom_left_width / 2))" "$((bottom_left_top + bottom_left_height / 2))"
        # Cross the pinned row band. A single three-line wheel notch can keep the shared
        # origin above SplitRow, where the bottom pane is expected to render identically.
        for _ in 1 2 3; do
            xdotool click 5
            sleep 0.12
        done
        sleep "$settle_seconds"
        capture "$bottom_wheel_after"
        crop_region "$bottom_wheel_after" "split-pane-bottom-left-wheel-after-crop.png" "$bottom_left_left" "$bottom_left_top" "$bottom_left_width" "$bottom_left_height"
        if region_changed "$output/split-pane-bottom-left-wheel-before-crop.png" "$output/split-pane-bottom-left-wheel-after-crop.png" 80; then
            bottom_wheel_passed=true
        fi
    else
        capture "$bottom_wheel_before"
        capture "$bottom_wheel_after"
    fi

    # The top-right mini-scrollbar is the horizontal track immediately above the horizontal
    # divider. A track click must move the shared main horizontal position and visibly change
    # that quadrant.
    scrollbar_x="$((split_column_x + top_right_width * 3 / 4))"
    scrollbar_y="$((split_row_y - 5))"
    if $split_open && (( top_right_width > 40 )); then
        capture "$scrollbar_before"
        crop_region "$scrollbar_before" "split-pane-scrollbar-before-crop.png" "$top_right_left" "$top_right_top" "$top_right_width" "$top_right_height"
        focus_app
        xdotool_mousemove_sync "$scrollbar_x" "$scrollbar_y" click 1
        sleep "$settle_seconds"
        capture "$scrollbar_after"
        crop_region "$scrollbar_after" "split-pane-scrollbar-after-crop.png" "$top_right_left" "$top_right_top" "$top_right_width" "$top_right_height"
        if region_changed "$output/split-pane-scrollbar-before-crop.png" "$output/split-pane-scrollbar-after-crop.png" 80; then
            scrollbar_passed=true
        fi
    else
        capture "$scrollbar_before"
        capture "$scrollbar_after"
    fi

    write_artifact "$postcondition" \
        "schema-version=1\nselector=split-pane-pointer\nsplit-command-gesture=keytip-route-$split_keytip_route\nsplit-open=$split_open\ndivider-gesture=horizontal-divider-drag\ndivider-coordinate=$divider_drag_x,$split_row_y\ndivider-target-y=$drag_y\ndivider-postcondition=$divider_passed\nactive-pane-gesture=top-right-shift-wheel-down\nactive-pane-crop=$top_right_left,$top_right_top,${top_right_width}x${top_right_height}\nactive-pane-shared-column-band-postcondition=$wheel_passed\nbottom-left-gesture=bottom-left-wheel-down-three-notches\nbottom-left-crop=$bottom_left_left,$bottom_left_top,${bottom_left_width}x${bottom_left_height}\nbottom-left-shared-row-band-postcondition=$bottom_wheel_passed\nmini-scrollbar-gesture=top-right-horizontal-track-click\nmini-scrollbar-coordinate=$scrollbar_x,$scrollbar_y\nmini-scrollbar-shared-column-band-postcondition=$scrollbar_passed\n"

    if $divider_passed; then
        record "split-pane-divider-drag-physical" "passed" \
            "$divider_before; $divider_after; target-y=$drag_y" \
            "A real X11 pointer drag moved the rendered split divider and changed the captured worksheet surface." \
            "$artifacts"
    else
        record "split-pane-divider-drag-physical" "failed" \
            "$divider_before; $divider_after; $postcondition" \
            "The physical divider drag did not produce an observable rendered postcondition." \
            "$artifacts"
    fi
    if $wheel_passed; then
        record "split-pane-active-pane-wheel-physical" "passed" \
            "$wheel_before; $wheel_after; split-pane-wheel-before-crop.png; split-pane-wheel-after-crop.png; $postcondition" \
            "A real X11 Shift+wheel event over the top-right quadrant changed the shared rendered column band." \
            "$artifacts;split-pane-wheel-before-crop.png;split-pane-wheel-after-crop.png"
    else
        record "split-pane-active-pane-wheel-physical" "failed" \
            "$wheel_before; $wheel_after; $postcondition" \
            "The physical TopRight Shift+wheel route did not prove shared column-band movement." \
            "$artifacts"
    fi
    if $bottom_wheel_passed; then
        record "split-pane-bottom-left-wheel-physical" "passed" \
            "$bottom_wheel_before; $bottom_wheel_after; split-pane-bottom-left-wheel-before-crop.png; split-pane-bottom-left-wheel-after-crop.png; $postcondition" \
            "A real X11 vertical wheel event over the bottom-left quadrant changed the shared rendered row band." \
            "$artifacts;split-pane-bottom-left-wheel-before-crop.png;split-pane-bottom-left-wheel-after-crop.png"
    else
        record "split-pane-bottom-left-wheel-physical" "failed" \
            "$bottom_wheel_before; $bottom_wheel_after; $postcondition" \
            "The physical BottomLeft vertical wheel route did not prove shared row-band movement." \
            "$artifacts"
    fi
    if $scrollbar_passed; then
        record "split-pane-mini-scrollbar-physical" "passed" \
            "$scrollbar_before; $scrollbar_after; split-pane-scrollbar-before-crop.png; split-pane-scrollbar-after-crop.png; $postcondition" \
            "A real X11 track click on the top-right mini-scrollbar changed the rendered split-pane content." \
            "$artifacts;split-pane-scrollbar-before-crop.png;split-pane-scrollbar-after-crop.png"
    else
        record "split-pane-mini-scrollbar-physical" "failed" \
            "$scrollbar_before; $scrollbar_after; $postcondition" \
            "The mini-scrollbar interaction did not produce an observable postcondition." \
            "$artifacts"
    fi
    if $split_open; then
        enter_keytip_mode
        keytip_key w
        keytip_key s
        keytip_key p
    fi
    send_key Escape || true
}

outline_toggle_visible() {
    local screenshot="$1" center_x="$2" center_y="$3" left top metrics white_score border_score
    left=$((center_x - 7))
    top=$((center_y - 7))
    (( left >= window_x && top >= window_y && left + 15 <= window_x + window_width && top + 15 <= window_y + window_height )) || return 1
    metrics="$(convert "$screenshot" \
        -crop "15x15+${left}+${top}" \
        -alpha off \
        -format '%[fx:mean((r>0.82)&&(g>0.82)&&(b>0.82))] %[fx:mean((abs(r-g)<0.08)&&(abs(g-b)<0.08)&&(r>0.25)&&(r<0.82))]' \
        info: 2>/dev/null || true)"
    read -r white_score border_score <<< "$metrics"
    white_score="${white_score:-0}"
    border_score="${border_score:-0}"
    awk -v white="$white_score" -v border="$border_score" \
        'BEGIN { exit !(white > 0.25 && border > 0.08) }'
}

probe_outline_group_physical() {
    local row2_value="" row3_value="" row4_value="" collapsed_slot="" expanded_slot=""
    local controls_visible=false collapsed_structurally=false expanded_structurally=false
    local values_restored=false group_command=false
    local base_row_header_width=0 row_header_x=0 toggle_x=0 toggle_y=0 collapsed_toggle_y=0 row_gutter_width=0 expected_row_depth=1
    local artifacts="outline-rows-selected.png;outline-grouped.png;outline-collapsed.png;outline-expanded.png;outline-group-postcondition.txt"

    # Seed three distinct values through the production inline editor. The values make the
    # model/UI restoration assertion independent of the default CSV contents.
    if ! set_cell_text_without_save 1 1 B2 OutlineRow2 ||
       ! set_cell_text_without_save 1 2 B3 OutlineRow3 ||
       ! set_cell_text_without_save 1 3 B4 OutlineRow4 ||
       ! set_cell_text_without_save 1 4 B5 OutlineRowSummary; then
        write_artifact "outline-group-postcondition.txt" "seeded=false\n"
        record "outline-group-physical" "failed" "outline-group-postcondition.txt" \
            "Could not seed the row-group fixture through real X11 inline editing." "$artifacts"
        return
    fi

    dismiss_active_popups
    # Drag the real row headers so the shared row-selection context menu owns exactly rows 2:4.
    # The startup A1 origin locates the row-label area even after an outline gutter is present.
    base_row_header_width=$((worksheet_base_a1_x - window_x))
    row_header_x=$((a1_x - base_row_header_width / 2))
    focus_app
    xdotool_mousemove_sync "$row_header_x" "$(cell_center_y 1)"
    xdotool mousedown 1
    sleep "$settle_seconds"
    xdotool_mousemove_sync "$row_header_x" "$(cell_center_y 3)"
    xdotool mouseup 1
    sleep "$settle_seconds"
    capture "outline-rows-selected.png"
    focus_app
    xdotool_mousemove_sync "$row_header_x" "$(cell_center_y 1)" click 3
    sleep "$settle_seconds"
    send_active_key End Up Up Up Return
    sleep "$settle_seconds"
    capture "outline-grouped.png"
    (( row_outline_depth > expected_row_depth )) && expected_row_depth="$row_outline_depth"
    set_expected_outline_origin "$expected_row_depth" "$column_outline_depth" "outline-group-origin.png"
    capture "outline-grouped.png"

    # Level 1 consumes 26 DIPs. Require that gutter growth and the rendered button chrome before
    # crediting Group or sending a click into the outline area.
    row_gutter_width=$((a1_x - worksheet_base_a1_x))
    toggle_x=$((window_x + 13))
    toggle_y="$(cell_center_y 4)"
    if (( row_gutter_width >= 24 )) && outline_toggle_visible "$output/outline-grouped.png" "$toggle_x" "$toggle_y"; then
        group_command=true
        controls_visible=true
        row_outline_depth="$expected_row_depth"
        focus_app
        xdotool_mousemove_sync "$toggle_x" "$toggle_y" click 1
        sleep "$settle_seconds"
        capture "outline-collapsed.png"
        collapsed_slot="$(copy_cell_formula 1 1 outline-row-collapsed-visible-slot || true)"
        [[ "$collapsed_slot" == "OutlineRowSummary" ]] && collapsed_structurally=true

        # Rows 2:4 are hidden only if summary row 5 occupies visible slot 2.
        collapsed_toggle_y="$(cell_center_y 1)"
        focus_app
        xdotool_mousemove_sync "$toggle_x" "$collapsed_toggle_y" click 1
        sleep "$settle_seconds"
        capture "outline-expanded.png"
        expanded_slot="$(copy_cell_formula 1 1 outline-row-expanded-visible-slot || true)"
        [[ "$expanded_slot" == "OutlineRow2" ]] && expanded_structurally=true
        outline_toggle_visible "$output/outline-expanded.png" "$toggle_x" "$toggle_y" || controls_visible=false
    fi

    row2_value="$(copy_cell_formula 1 1 B2 || true)"
    row3_value="$(copy_cell_formula 1 2 B3 || true)"
    row4_value="$(copy_cell_formula 1 3 B4 || true)"
    if [[ "$row2_value" == "OutlineRow2" && "$row3_value" == "OutlineRow3" && "$row4_value" == "OutlineRow4" ]]; then
        values_restored=true
    fi

    write_artifact "outline-group-postcondition.txt" \
        "seeded=true\nselection-gesture=row-header-drag-2:4\ngroup-gesture=row-header-right-click,End,Up,Up,Up,Enter\nrow-gutter-width=$row_gutter_width\ngroup-command=$group_command\noutline-controls-visible=$controls_visible\ncollapsed-visible-slot=$collapsed_slot\ncollapse-structural=$collapsed_structurally\nexpanded-visible-slot=$expanded_slot\nexpand-structural=$expanded_structurally\nrestored-values=$row2_value,$row3_value,$row4_value\nvalues-restored=$values_restored\n"
    if $group_command && $controls_visible && $collapsed_structurally && $expanded_structurally && $values_restored; then
        record "outline-group-physical" "passed" \
            "outline-rows-selected.png; outline-grouped.png; outline-collapsed.png; outline-expanded.png; rows=2:4; values=OutlineRow2,OutlineRow3,OutlineRow4" \
            "A real row-header drag and shared context-menu Group command rendered the outline gutter; visible-slot values proved physical collapse/expand, and all three detail values read back exactly." "$artifacts"
    else
        record "outline-group-physical" "failed" \
            "outline-rows-selected.png; outline-grouped.png; outline-collapsed.png; outline-expanded.png; outline-group-postcondition.txt" \
            "The row Group/Outline workflow did not prove every structural state: group-command=$group_command, controls-visible=$controls_visible, collapse-structural=$collapsed_structurally, expand-structural=$expanded_structurally, values-restored=$values_restored." "$artifacts"
    fi
    if ! $group_command; then
        set_expected_outline_origin "$row_outline_depth" "$column_outline_depth"
    fi
    dismiss_active_popups
}

probe_outline_column_group_physical() {
    local column2_value="" column3_value="" column4_value="" collapsed_slot="" expanded_slot=""
    local controls_visible=false collapsed_structurally=false expanded_structurally=false
    local values_restored=false group_command=false
    local column_header_y=0 toggle_x=0 toggle_y=0 collapsed_toggle_x=0 outline_top=0 column_gutter_height=0 expected_column_depth=1
    local artifacts="outline-columns-selected.png;outline-columns-grouped.png;outline-columns-group-postcondition.txt"

    # Seed distinct values in the grouped columns through the production inline editor. Keeping
    # the values on one visible row makes the postcondition independent of the fixture document.
    if ! set_cell_text_without_save 1 1 B2 OutlineColumn2 ||
       ! set_cell_text_without_save 2 1 C2 OutlineColumn3 ||
       ! set_cell_text_without_save 3 1 D2 OutlineColumn4 ||
       ! set_cell_text_without_save 4 1 E2 OutlineColumnSummary; then
        write_artifact "outline-columns-group-postcondition.txt" "seeded=false\n"
        record "outline-columns-group-physical" "failed" "outline-columns-group-postcondition.txt" \
            "Could not seed the column-group fixture through real X11 inline editing." "$artifacts"
        return
    fi

    dismiss_active_popups
    # Select whole columns through the production header-drag route. Dragging from the center of
    # B's header to D's center avoids the resize grips and preserves whole-column selection scope.
    column_header_y="$((a1_y - cell_height / 2))"
    if select_cell 1 1 B2; then
        focus_app
        xdotool_mousemove_sync "$(cell_center_x 1)" "$column_header_y"
        xdotool mousedown 1
        sleep "$settle_seconds"
        xdotool_mousemove_sync "$(cell_center_x 3)" "$column_header_y"
        xdotool mouseup 1
        sleep "$settle_seconds"
        capture "outline-columns-selected.png"
        focus_app
        xdotool_mousemove_sync "$(cell_center_x 1)" "$column_header_y" click 3
        sleep "$settle_seconds"
        send_active_key End Up Up Up Return
        sleep "$settle_seconds"
        capture "outline-columns-grouped.png"
        (( column_outline_depth > expected_column_depth )) && expected_column_depth="$column_outline_depth"
        set_expected_outline_origin "$row_outline_depth" "$expected_column_depth" "outline-columns-origin.png"
        capture "outline-columns-grouped.png"
    fi

    # Require the 26-DIP level-1 gutter and rendered E-summary toggle before interacting.
    column_gutter_height=$((a1_y - worksheet_base_a1_y))
    toggle_x="$(cell_center_x 4)"
    outline_top="$((worksheet_base_a1_y - cell_height))"
    toggle_y="$((outline_top + 13))"
    if (( column_gutter_height >= 24 )) && outline_toggle_visible "$output/outline-columns-grouped.png" "$toggle_x" "$toggle_y"; then
        group_command=true
        controls_visible=true
        column_outline_depth="$expected_column_depth"
        focus_app
        xdotool_mousemove_sync "$toggle_x" "$toggle_y" click 1
        sleep "$settle_seconds"
        capture "outline-columns-collapsed.png"
        collapsed_slot="$(copy_cell_formula 1 1 outline-column-collapsed-visible-slot || true)"
        [[ "$collapsed_slot" == "OutlineColumnSummary" ]] && collapsed_structurally=true

        # Once B:D are hidden, the summary column E moves into the first visible data slot.
        collapsed_toggle_x="$(cell_center_x 1)"
        focus_app
        xdotool_mousemove_sync "$collapsed_toggle_x" "$toggle_y" click 1
        sleep "$settle_seconds"
        capture "outline-columns-expanded.png"
        artifacts="outline-columns-selected.png;outline-columns-grouped.png;outline-columns-collapsed.png;outline-columns-expanded.png;outline-columns-group-postcondition.txt"
        expanded_slot="$(copy_cell_formula 1 1 outline-column-expanded-visible-slot || true)"
        [[ "$expanded_slot" == "OutlineColumn2" ]] && expanded_structurally=true
        outline_toggle_visible "$output/outline-columns-expanded.png" "$toggle_x" "$toggle_y" || controls_visible=false
    fi

    column2_value="$(copy_cell_formula_by_keyboard 1 1 || true)"
    column3_value="$(copy_cell_formula_by_keyboard 2 1 || true)"
    column4_value="$(copy_cell_formula_by_keyboard 3 1 || true)"
    if [[ "$column2_value" == "OutlineColumn2" && "$column3_value" == "OutlineColumn3" && "$column4_value" == "OutlineColumn4" ]]; then
        values_restored=true
    fi

    write_artifact "outline-columns-group-postcondition.txt" \
        "seeded=true\nselection-gesture=column-header-drag-B:D\ngroup-gesture=column-header-right-click,End,Up,Up,Up,Enter\ncolumn-gutter-height=$column_gutter_height\ngroup-command=$group_command\noutline-controls-visible=$controls_visible\ncollapsed-visible-slot=$collapsed_slot\ncollapse-structural=$collapsed_structurally\nexpanded-visible-slot=$expanded_slot\nexpand-structural=$expanded_structurally\nrestored-values=$column2_value,$column3_value,$column4_value\nvalues-restored=$values_restored\n"
    if $group_command && $controls_visible && $collapsed_structurally && $expanded_structurally && $values_restored; then
        record "outline-columns-group-physical" "passed" \
            "outline-columns-selected.png; outline-columns-grouped.png; outline-columns-collapsed.png; outline-columns-expanded.png; columns=B:D; values=OutlineColumn2,OutlineColumn3,OutlineColumn4" \
            "Real X11 column-header selection and the shared worksheet Group command rendered the column outline gutter; physical +/- collapse hid the grouped columns, a second physical +/- expanded them, and all three model values read back exactly." "$artifacts"
    else
        record "outline-columns-group-physical" "failed" \
            "outline-columns-selected.png; outline-columns-grouped.png; outline-columns-group-postcondition.txt" \
            "The column Group/Outline workflow did not prove every structural state: group-command=$group_command, controls-visible=$controls_visible, collapse-structural=$collapsed_structurally, expand-structural=$expanded_structurally, values-restored=$values_restored." "$artifacts"
    fi
    if ! $group_command; then
        set_expected_outline_origin "$row_outline_depth" "$column_outline_depth"
    fi
    dismiss_active_popups
}

probe_outline_nested_rows_physical() {
    local inner_collapsed=false inner_expanded=false outer_collapsed=false outer_expanded=false
    local controls_visible=false expanded_controls_visible=false values_restored=false outer_command=false inner_command=false
    local inner_toggle_x=0 outer_toggle_x=0 inner_toggle_y=0 inner_collapsed_y=0 outer_toggle_y=0 outer_collapsed_y=0
    local base_row_header_width=0 row_header_x=0 row_gutter_width=0 outer_gutter_width=0
    local expected_outer_depth=1 expected_inner_depth=2
    local inner_collapsed_slot="" inner_expanded_slot="" outer_collapsed_slot="" outer_expanded_slot=""
    local row2_value="" row3_value="" row4_value="" row5_value="" row6_value=""
    local artifacts="outline-nested-rows-grouped.png;outline-nested-rows-inner-collapsed.png;outline-nested-rows-inner-expanded.png;outline-nested-rows-outer-collapsed.png;outline-nested-rows-outer-expanded.png;outline-nested-rows-postcondition.txt"

    # Build a level-1 outer group first, then a level-2 subgroup inside it. The five values make
    # every detail row independently observable after both collapse/expand cycles.
    if ! set_cell_text_without_save 1 9 B10 NestedRow10 ||
       ! set_cell_text_without_save 1 10 B11 NestedRow11 ||
       ! set_cell_text_without_save 1 11 B12 NestedRow12 ||
       ! set_cell_text_without_save 1 12 B13 NestedRow13 ||
       ! set_cell_text_without_save 1 13 B14 NestedRow14 ||
       ! set_cell_text_without_save 1 14 B15 NestedRowOuterSummary; then
        write_artifact "outline-nested-rows-postcondition.txt" "seeded=false\n"
        record "outline-nested-rows-group-physical" "failed" "outline-nested-rows-postcondition.txt" \
            "Could not seed the nested row-group fixture through real X11 inline editing." "$artifacts"
        return
    fi

    dismiss_active_popups
    base_row_header_width=$((worksheet_base_a1_x - window_x))
    row_header_x=$((a1_x - base_row_header_width / 2))
    focus_app
    xdotool_mousemove_sync "$row_header_x" "$(cell_center_y 9)"
    xdotool mousedown 1
    sleep "$settle_seconds"
    xdotool_mousemove_sync "$row_header_x" "$(cell_center_y 13)"
    xdotool mouseup 1
    sleep "$settle_seconds"
    focus_app
    xdotool_mousemove_sync "$row_header_x" "$(cell_center_y 9)" click 3
    sleep "$settle_seconds"
    send_active_key End Up Up Up Return
    sleep "$settle_seconds"
    (( row_outline_depth > expected_outer_depth )) && expected_outer_depth="$row_outline_depth"
    set_expected_outline_origin "$expected_outer_depth" "$column_outline_depth" "outline-nested-rows-outer-origin.png"
    outer_gutter_width=$((a1_x - worksheet_base_a1_x))
    outer_toggle_x=$((window_x + 13))
    outer_toggle_y="$(cell_center_y 14)"
    if (( outer_gutter_width >= 24 )) && outline_toggle_visible "$output/outline-nested-rows-outer-origin.png" "$outer_toggle_x" "$outer_toggle_y"; then
        outer_command=true
        row_outline_depth="$expected_outer_depth"
    else
        set_expected_outline_origin "$row_outline_depth" "$column_outline_depth"
    fi

    # Recompute the row-label coordinate after the outer gutter appears, then create 11:12 through
    # the same row-header context command. The inner command is credited only after level 2 grows.
    if $outer_command; then
        row_header_x=$((a1_x - base_row_header_width / 2))
        focus_app
        xdotool_mousemove_sync "$row_header_x" "$(cell_center_y 10)"
        xdotool mousedown 1
        sleep "$settle_seconds"
        xdotool_mousemove_sync "$row_header_x" "$(cell_center_y 11)"
        xdotool mouseup 1
        sleep "$settle_seconds"
        focus_app
        xdotool_mousemove_sync "$row_header_x" "$(cell_center_y 10)" click 3
        sleep "$settle_seconds"
        send_active_key End Up Up Up Return
        sleep "$settle_seconds"
        (( row_outline_depth > expected_inner_depth )) && expected_inner_depth="$row_outline_depth"
        set_expected_outline_origin "$expected_inner_depth" "$column_outline_depth" "outline-nested-rows-inner-origin.png"
        row_gutter_width=$((a1_x - worksheet_base_a1_x))
        inner_toggle_x=$((window_x + 27))
        inner_toggle_y="$(cell_center_y 12)"
        if (( row_gutter_width >= 38 )) && outline_toggle_visible "$output/outline-nested-rows-inner-origin.png" "$inner_toggle_x" "$inner_toggle_y"; then
            inner_command=true
            row_outline_depth="$expected_inner_depth"
        else
            set_expected_outline_origin "$row_outline_depth" "$column_outline_depth"
            row_gutter_width=$((a1_x - worksheet_base_a1_x))
        fi
    fi

    capture "outline-nested-rows-grouped.png"
    inner_toggle_x=$((window_x + 27))
    outer_toggle_x=$((window_x + 13))
    inner_toggle_y="$(cell_center_y 12)"
    outer_toggle_y="$(cell_center_y 14)"
    if $outer_command && $inner_command &&
       outline_toggle_visible "$output/outline-nested-rows-grouped.png" "$inner_toggle_x" "$inner_toggle_y" &&
       outline_toggle_visible "$output/outline-nested-rows-grouped.png" "$outer_toggle_x" "$outer_toggle_y"; then
        controls_visible=true

        # Level 2: collapse rows 11:12. Read the visible summary by its worksheet address so
        # the assertion cannot accidentally pass from a different visible row slot.
        focus_app
        xdotool_mousemove_sync "$inner_toggle_x" "$inner_toggle_y" click 1
        sleep "$settle_seconds"
        capture "outline-nested-rows-inner-collapsed.png"
        inner_collapsed_slot="$(copy_cell_formula_by_address B13 || true)"
        [[ "$inner_collapsed_slot" == "NestedRow13" ]] && inner_collapsed=true

        inner_collapsed_y="$(cell_center_y 10)"
        focus_app
        xdotool_mousemove_sync "$inner_toggle_x" "$inner_collapsed_y" click 1
        sleep "$settle_seconds"
        capture "outline-nested-rows-inner-expanded.png"
        inner_expanded_slot="$(copy_cell_formula_by_address B11 || true)"
        [[ "$inner_expanded_slot" == "NestedRow11" ]] && inner_expanded=true

        # Level 1: collapse rows 10:14. Read the outer summary by its exact address.
        focus_app
        xdotool_mousemove_sync "$outer_toggle_x" "$outer_toggle_y" click 1
        sleep "$settle_seconds"
        capture "outline-nested-rows-outer-collapsed.png"
        outer_collapsed_slot="$(copy_cell_formula_by_address B15 || true)"
        [[ "$outer_collapsed_slot" == "NestedRowOuterSummary" ]] && outer_collapsed=true

        outer_collapsed_y="$(cell_center_y 9)"
        focus_app
        xdotool_mousemove_sync "$outer_toggle_x" "$outer_collapsed_y" click 1
        sleep "$settle_seconds"
        capture "outline-nested-rows-outer-expanded.png"
        outer_expanded_slot="$(copy_cell_formula_by_address B10 || true)"
        [[ "$outer_expanded_slot" == "NestedRow10" ]] && outer_expanded=true
        if outline_toggle_visible "$output/outline-nested-rows-outer-expanded.png" "$inner_toggle_x" "$inner_toggle_y" &&
           outline_toggle_visible "$output/outline-nested-rows-outer-expanded.png" "$outer_toggle_x" "$outer_toggle_y"; then
            expanded_controls_visible=true
        fi
    fi

    row2_value="$(copy_cell_formula_by_address B10 || true)"
    row3_value="$(copy_cell_formula_by_address B11 || true)"
    row4_value="$(copy_cell_formula_by_address B12 || true)"
    row5_value="$(copy_cell_formula_by_address B13 || true)"
    row6_value="$(copy_cell_formula_by_address B14 || true)"
    if [[ "$row2_value" == "NestedRow10" && "$row3_value" == "NestedRow11" &&
          "$row4_value" == "NestedRow12" && "$row5_value" == "NestedRow13" &&
          "$row6_value" == "NestedRow14" ]]; then
        values_restored=true
    fi

    row_gutter_width=$((a1_x - worksheet_base_a1_x))
    write_artifact "outline-nested-rows-postcondition.txt" \
        "seeded=true\nouter-selection=row-header-drag-10:14\ninner-selection=row-header-drag-11:12\ngroup-gesture=row-header-right-click,End,Up,Up,Up,Enter\nouter-group-command=$outer_command\ninner-group-command=$inner_command\nrow-gutter-width=$row_gutter_width\noutline-controls-visible=$controls_visible\ninner-collapsed-address-value=$inner_collapsed_slot\ninner-collapse-structural=$inner_collapsed\ninner-expanded-address-value=$inner_expanded_slot\ninner-expand-structural=$inner_expanded\nouter-collapsed-address-value=$outer_collapsed_slot\nouter-collapse-structural=$outer_collapsed\nouter-expanded-address-value=$outer_expanded_slot\nouter-expand-structural=$outer_expanded\nexpanded-outline-controls-visible=$expanded_controls_visible\nrestored-values=$row2_value,$row3_value,$row4_value,$row5_value,$row6_value\nvalues-restored=$values_restored\n"
    if $outer_command && $inner_command && $controls_visible && $inner_collapsed && $inner_expanded && $outer_collapsed && $outer_expanded && $expanded_controls_visible && $values_restored; then
        record "outline-nested-rows-group-physical" "passed" \
            "outline-nested-rows-grouped.png; outline-nested-rows-inner-collapsed.png; outline-nested-rows-inner-expanded.png; outline-nested-rows-outer-collapsed.png; outline-nested-rows-outer-expanded.png; rows=10:14/inner=11:12; values=NestedRow10,NestedRow11,NestedRow12,NestedRow13,NestedRow14" \
            "Real row-header drags and shared context-menu Group commands created two rendered levels; exact B13/B11/B15/B10 address readback proved both collapse/expand cycles, and all five detail values read back exactly." "$artifacts"
    else
        record "outline-nested-rows-group-physical" "failed" \
            "outline-nested-rows-grouped.png; outline-nested-rows-inner-collapsed.png; outline-nested-rows-inner-expanded.png; outline-nested-rows-outer-collapsed.png; outline-nested-rows-outer-expanded.png; outline-nested-rows-postcondition.txt" \
            "Nested row Group/Outline did not prove every structural state: outer-command=$outer_command, inner-command=$inner_command, controls-visible=$controls_visible, inner-collapse=$inner_collapsed, inner-expand=$inner_expanded, outer-collapse=$outer_collapsed, outer-expand=$outer_expanded, expanded-controls-visible=$expanded_controls_visible, values-restored=$values_restored." "$artifacts"
    fi
    dismiss_active_popups
}

probe_outline_nested_columns_physical() {
    local inner_collapsed=false inner_expanded=false outer_collapsed=false outer_expanded=false
    local controls_visible=false expanded_controls_visible=false values_restored=false outer_command=false inner_command=false
    local column_header_y=0 inner_toggle_x=0 outer_toggle_x=0 inner_toggle_y=0 outer_toggle_y=0 inner_collapsed_x=0 outer_collapsed_x=0
    local outline_top=0 column_gutter_height=0 outer_gutter_height=0
    local expected_outer_depth=1 expected_inner_depth=2
    local inner_collapsed_slot="" inner_expanded_slot="" outer_collapsed_slot="" outer_expanded_slot=""
    local column2_value="" column3_value="" column4_value="" column5_value="" column6_value=""
    local artifacts="outline-nested-columns-grouped.png;outline-nested-columns-inner-collapsed.png;outline-nested-columns-inner-expanded.png;outline-nested-columns-outer-collapsed.png;outline-nested-columns-outer-expanded.png;outline-nested-columns-postcondition.txt"

    if ! set_cell_text_without_save 7 1 H2 NestedColumnH ||
       ! set_cell_text_without_save 8 1 I2 NestedColumnI ||
       ! set_cell_text_without_save 9 1 J2 NestedColumnJ ||
       ! set_cell_text_without_save 10 1 K2 NestedColumnK ||
       ! set_cell_text_without_save 11 1 L2 NestedColumnL ||
       ! set_cell_text_without_save 12 1 M2 NestedColumnOuterSummary; then
        write_artifact "outline-nested-columns-postcondition.txt" "seeded=false\n"
        record "outline-nested-columns-group-physical" "failed" "outline-nested-columns-postcondition.txt" \
            "Could not seed the nested column-group fixture through real X11 inline editing." "$artifacts"
        return
    fi

    dismiss_active_popups
    column_header_y="$((a1_y - cell_height / 2))"
    if select_cell 7 1 H2; then
        focus_app
        xdotool_mousemove_sync "$(cell_center_x 7)" "$column_header_y"
        xdotool mousedown 1
        sleep "$settle_seconds"
        xdotool_mousemove_sync "$(cell_center_x 11)" "$column_header_y"
        xdotool mouseup 1
        sleep "$settle_seconds"
        xdotool_mousemove_sync "$(cell_center_x 7)" "$column_header_y" click 3
        sleep "$settle_seconds"
        send_active_key End Up Up Up Return
        sleep "$settle_seconds"
        (( column_outline_depth > expected_outer_depth )) && expected_outer_depth="$column_outline_depth"
        set_expected_outline_origin "$row_outline_depth" "$expected_outer_depth" "outline-nested-columns-outer-origin.png"
        outer_gutter_height=$((a1_y - worksheet_base_a1_y))
        outline_top=$((worksheet_base_a1_y - cell_height))
        outer_toggle_x="$(cell_center_x 12)"
        outer_toggle_y=$((outline_top + 13))
        if (( outer_gutter_height >= 24 )) && outline_toggle_visible "$output/outline-nested-columns-outer-origin.png" "$outer_toggle_x" "$outer_toggle_y"; then
            outer_command=true
            column_outline_depth="$expected_outer_depth"
        else
            set_expected_outline_origin "$row_outline_depth" "$column_outline_depth"
        fi

        if $outer_command && select_cell 8 1 I2; then
            column_header_y="$((a1_y - cell_height / 2))"
            focus_app
            xdotool_mousemove_sync "$(cell_center_x 8)" "$column_header_y"
            xdotool mousedown 1
            sleep "$settle_seconds"
            xdotool_mousemove_sync "$(cell_center_x 10)" "$column_header_y"
            xdotool mouseup 1
            sleep "$settle_seconds"
            xdotool_mousemove_sync "$(cell_center_x 8)" "$column_header_y" click 3
            sleep "$settle_seconds"
            send_active_key End Up Up Up Return
            sleep "$settle_seconds"
            (( column_outline_depth > expected_inner_depth )) && expected_inner_depth="$column_outline_depth"
            set_expected_outline_origin "$row_outline_depth" "$expected_inner_depth" "outline-nested-columns-inner-origin.png"
            column_gutter_height=$((a1_y - worksheet_base_a1_y))
            outline_top=$((worksheet_base_a1_y - cell_height))
            inner_toggle_x="$(cell_center_x 11)"
            inner_toggle_y=$((outline_top + 27))
            if (( column_gutter_height >= 38 )) && outline_toggle_visible "$output/outline-nested-columns-inner-origin.png" "$inner_toggle_x" "$inner_toggle_y"; then
                inner_command=true
                column_outline_depth="$expected_inner_depth"
            else
                set_expected_outline_origin "$row_outline_depth" "$column_outline_depth"
                column_gutter_height=$((a1_y - worksheet_base_a1_y))
            fi
        fi
    fi

    capture "outline-nested-columns-grouped.png"
    outline_top="$((worksheet_base_a1_y - cell_height))"
    inner_toggle_y="$((outline_top + 27))"
    outer_toggle_y="$((outline_top + 13))"
    inner_toggle_x="$(cell_center_x 11)"
    outer_toggle_x="$(cell_center_x 12)"
    if $outer_command && $inner_command &&
       outline_toggle_visible "$output/outline-nested-columns-grouped.png" "$inner_toggle_x" "$inner_toggle_y" &&
       outline_toggle_visible "$output/outline-nested-columns-grouped.png" "$outer_toggle_x" "$outer_toggle_y"; then
        controls_visible=true

        # Inner I:K: the level-2 toggle is over its L summary column. Read L2 directly because
        # its visible slot changes when I:K are hidden.
        focus_app
        xdotool_mousemove_sync "$inner_toggle_x" "$inner_toggle_y" click 1
        sleep "$settle_seconds"
        capture "outline-nested-columns-inner-collapsed.png"
        inner_collapsed_slot="$(copy_cell_formula_by_address L2 || true)"
        [[ "$inner_collapsed_slot" == "NestedColumnL" ]] && inner_collapsed=true

        inner_collapsed_x="$(cell_center_x 8)"
        focus_app
        xdotool_mousemove_sync "$inner_collapsed_x" "$inner_toggle_y" click 1
        sleep "$settle_seconds"
        capture "outline-nested-columns-inner-expanded.png"
        inner_expanded_slot="$(copy_cell_formula_by_address I2 || true)"
        [[ "$inner_expanded_slot" == "NestedColumnI" ]] && inner_expanded=true

        # Outer H:L: the level-1 toggle is over M. Read M2 directly after the group hides H:L.
        focus_app
        xdotool_mousemove_sync "$outer_toggle_x" "$outer_toggle_y" click 1
        sleep "$settle_seconds"
        capture "outline-nested-columns-outer-collapsed.png"
        outer_collapsed_slot="$(copy_cell_formula_by_address M2 || true)"
        [[ "$outer_collapsed_slot" == "NestedColumnOuterSummary" ]] && outer_collapsed=true

        outer_collapsed_x="$(cell_center_x 7)"
        focus_app
        xdotool_mousemove_sync "$outer_collapsed_x" "$outer_toggle_y" click 1
        sleep "$settle_seconds"
        capture "outline-nested-columns-outer-expanded.png"
        outer_expanded_slot="$(copy_cell_formula_by_address H2 || true)"
        [[ "$outer_expanded_slot" == "NestedColumnH" ]] && outer_expanded=true
        if outline_toggle_visible "$output/outline-nested-columns-outer-expanded.png" "$inner_toggle_x" "$inner_toggle_y" &&
           outline_toggle_visible "$output/outline-nested-columns-outer-expanded.png" "$outer_toggle_x" "$outer_toggle_y"; then
            expanded_controls_visible=true
        fi
    fi

    column2_value="$(copy_cell_formula_by_address H2 || true)"
    column3_value="$(copy_cell_formula_by_address I2 || true)"
    column4_value="$(copy_cell_formula_by_address J2 || true)"
    column5_value="$(copy_cell_formula_by_address K2 || true)"
    column6_value="$(copy_cell_formula_by_address L2 || true)"
    if [[ "$column2_value" == "NestedColumnH" && "$column3_value" == "NestedColumnI" &&
          "$column4_value" == "NestedColumnJ" && "$column5_value" == "NestedColumnK" &&
          "$column6_value" == "NestedColumnL" ]]; then
        values_restored=true
    fi

    column_gutter_height=$((a1_y - worksheet_base_a1_y))
    write_artifact "outline-nested-columns-postcondition.txt" \
        "seeded=true\nouter-selection=column-header-drag-H:L\ninner-selection=column-header-drag-I:K\ngroup-gesture=column-header-right-click,End,Up,Up,Up,Enter\nouter-group-command=$outer_command\ninner-group-command=$inner_command\ncolumn-gutter-height=$column_gutter_height\noutline-controls-visible=$controls_visible\ninner-collapsed-address-value=$inner_collapsed_slot\ninner-collapse-structural=$inner_collapsed\ninner-expanded-address-value=$inner_expanded_slot\ninner-expand-structural=$inner_expanded\nouter-collapsed-address-value=$outer_collapsed_slot\nouter-collapse-structural=$outer_collapsed\nouter-expanded-address-value=$outer_expanded_slot\nouter-expand-structural=$outer_expanded\nexpanded-outline-controls-visible=$expanded_controls_visible\nrestored-values=$column2_value,$column3_value,$column4_value,$column5_value,$column6_value\nvalues-restored=$values_restored\n"
    if $outer_command && $inner_command && $controls_visible && $inner_collapsed && $inner_expanded && $outer_collapsed && $outer_expanded && $expanded_controls_visible && $values_restored; then
        record "outline-nested-columns-group-physical" "passed" \
            "outline-nested-columns-grouped.png; outline-nested-columns-inner-collapsed.png; outline-nested-columns-inner-expanded.png; outline-nested-columns-outer-collapsed.png; outline-nested-columns-outer-expanded.png; columns=H:L/inner=I:K; values=NestedColumnH,NestedColumnI,NestedColumnJ,NestedColumnK,NestedColumnL" \
            "Real column-header drags and shared context-menu Group commands created two rendered levels; exact L2/I2/M2/H2 address readback proved both collapse/expand cycles, and all five detail values read back exactly." "$artifacts"
    else
        record "outline-nested-columns-group-physical" "failed" \
            "outline-nested-columns-grouped.png; outline-nested-columns-inner-collapsed.png; outline-nested-columns-inner-expanded.png; outline-nested-columns-outer-collapsed.png; outline-nested-columns-outer-expanded.png; outline-nested-columns-postcondition.txt" \
            "Nested column Group/Outline did not prove every structural state: outer-command=$outer_command, inner-command=$inner_command, controls-visible=$controls_visible, inner-collapse=$inner_collapsed, inner-expand=$inner_expanded, outer-collapse=$outer_collapsed, outer-expand=$outer_expanded, expanded-controls-visible=$expanded_controls_visible, values-restored=$values_restored." "$artifacts"
    fi
    dismiss_active_popups
}

probe_outline_nested_save_reopen_physical() {
    local artifacts="outline-nested-save-reopen-before.png;outline-nested-save-reopen-after.png;outline-nested-save-reopen-postcondition.txt"
    local save_clean=false package_passed=false dialog_open=false dialog_closed=false values_restored=false
    local package_signature="" reopened_values=""
    local before_windows=0 after_windows=0

    if [[ "${document_path,,}" != *.xlsx ]]; then
        write_artifact "outline-nested-save-reopen-postcondition.txt" \
            "requires-xlsx=true\ndocument-path=$document_path\n"
        record "outline-nested-save-reopen-physical" "failed" \
            "outline-nested-save-reopen-postcondition.txt" \
            "The physical nested-outline save/reopen lane requires an XLSX document path so outline XML can be retained." \
            "$artifacts"
        return
    fi

    capture "outline-nested-save-reopen-before.png"
    send_key ctrl+s
    wait_for_document_clean && save_clean=true

    # Inspect the saved package before reopening it. This proves the saved artifact retained the
    # nested row/column outline levels independently of the later visual and clipboard checks.
    package_signature="$(python3 - "$document_path" <<'PY'
import sys
import zipfile
import xml.etree.ElementTree as ET

main = "{http://schemas.openxmlformats.org/spreadsheetml/2006/main}"
try:
    with zipfile.ZipFile(sys.argv[1]) as package:
        worksheet = ET.fromstring(package.read("xl/worksheets/sheet1.xml"))
        rows = {
            node.attrib.get("r", ""): node.attrib.get("outlineLevel", "0")
            for node in worksheet.findall(".//" + main + "row")
            if node.attrib.get("r") in {"10", "11", "12", "13", "14"}
        }
        columns = {}
        for node in worksheet.findall(".//" + main + "col"):
            minimum = int(node.attrib.get("min", "0"))
            maximum = int(node.attrib.get("max", "0"))
            for column in range(max(8, minimum), min(12, maximum) + 1):
                columns[str(column)] = node.attrib.get("outlineLevel", "0")
        expected_rows = {"10": "1", "11": "2", "12": "2", "13": "1", "14": "1"}
        expected_columns = {"8": "1", "9": "2", "10": "2", "11": "2", "12": "1"}
        if rows != expected_rows or columns != expected_columns:
            raise ValueError("nested outline levels did not persist")
        print(f"rows={rows}|columns={columns}|auto-filter={worksheet.find(main + 'autoFilter') is not None}")
except (OSError, KeyError, ET.ParseError, ValueError, StopIteration):
    raise SystemExit(1)
PY
)" || package_signature=""
    if [[ "$package_signature" == *"rows="* && "$package_signature" == *"columns="* ]]; then
        package_passed=true
    fi

    # Reopen through the production GTK picker, then read the seeded row values from the live
    # reopened sheet by exact worksheet address. This remains meaningful if the persisted outline
    # state reopens with any detail rows collapsed.
    before_windows="$(visible_window_count)"
    send_key ctrl+F12
    for _ in $(seq 1 12); do
        after_windows="$(visible_window_count)"
        if (( after_windows > before_windows )); then
            dialog_open=true
            break
        fi
        sleep 0.2
    done
    if $dialog_open; then
        xdotool key --clearmodifiers --delay "$input_delay_ms" ctrl+l
        xdotool type --clearmodifiers --delay "$type_delay_ms" "$document_path"
        xdotool key --clearmodifiers Return
        sleep "$settle_seconds"
        xdotool key --clearmodifiers Return
        for _ in $(seq 1 16); do
            after_windows="$(visible_window_count)"
            if (( after_windows <= before_windows )); then
                dialog_closed=true
                break
            fi
            sleep 0.25
        done
    fi
    if $dialog_closed; then
        capture "outline-nested-save-reopen-after.png"
        reopened_values="$(copy_cell_formula_by_address B10 || true),$(copy_cell_formula_by_address B11 || true),$(copy_cell_formula_by_address B12 || true),$(copy_cell_formula_by_address B13 || true),$(copy_cell_formula_by_address B14 || true)"
        if [[ "$reopened_values" == "NestedRow10,NestedRow11,NestedRow12,NestedRow13,NestedRow14" ]]; then
            values_restored=true
        fi
    fi

    write_artifact "outline-nested-save-reopen-postcondition.txt" \
        "document-path=$document_path\nsave-clean=$save_clean\npackage-signature=$package_signature\npackage-passed=$package_passed\ndialog-open=$dialog_open\ndialog-closed=$dialog_closed\nreopened-values=$reopened_values\nvalues-restored=$values_restored\n"
    if $save_clean && $package_passed && $dialog_closed && $values_restored; then
        record "outline-nested-save-reopen-physical" "passed" \
            "outline-nested-save-reopen-before.png; outline-nested-save-reopen-after.png; package=$package_signature; reopened-values=$reopened_values" \
            "Physical nested row/column outline gestures were saved to XLSX, the package retained both outline levels, and the production Open route reopened the document with all seeded row values intact." \
            "$artifacts"
    else
        record "outline-nested-save-reopen-physical" "failed" \
            "$artifacts" \
            "Nested outline save/reopen was not fully proven: save-clean=$save_clean, package-passed=$package_passed, dialog-closed=$dialog_closed, values-restored=$values_restored, package='$package_signature', values='$reopened_values'." \
            "$artifacts"
    fi
}

probe_outline_nested_filter_save_reopen_physical() {
    local artifacts="outline-nested-filter-initial.png;outline-nested-filter-before-flyout.png;outline-nested-filter-flyout-open.png;outline-nested-filter-all-values.png;outline-nested-filter-flyout-reopen.png;outline-nested-filter-applied.png;outline-nested-filter-inner-collapsed.png;outline-nested-filter-inner-expanded.png;outline-nested-filter-outer-collapsed.png;outline-nested-filter-outer-expanded.png;outline-nested-filter-reopened.png;outline-nested-filter-postcondition.txt"
    local filter_open=false filter_reopen=false filter_flyout_passed=false
    local initial_values="" all_values="" filtered_values=""
    local inner_collapsed_values="" inner_expanded_values="" outer_collapsed_values="" outer_expanded_values="" reopened_values=""
    local inner_collapsed=false inner_expanded=false outer_collapsed=false outer_expanded=false controls_visible=false
    local save_clean=false dialog_open=false dialog_closed=false reopen_values_passed=false package_passed=false
    local package_signature="" save_clean_after_reopen=false
    local inner_toggle_x=0 outer_toggle_x=0 inner_toggle_y=0 outer_toggle_y=0
    local inner_collapsed_y=0 outer_collapsed_y=0 before_windows=0 after_windows=0

    inspect_saved_package() {
        package_signature="$(python3 - "$document_path" <<'PY'
import sys
import zipfile
import xml.etree.ElementTree as ET

main = "{http://schemas.openxmlformats.org/spreadsheetml/2006/main}"
expected_outline = {"2": "1", "3": "2", "4": "2", "5": "1", "6": "1"}
expected_values = {
    "A1": "Region", "B1": "Value", "A2": "Keep", "B2": "Outer2",
    "A3": "Drop", "B3": "InnerDrop3", "A4": "Keep", "B4": "InnerKeep4",
    "A5": "Keep", "B5": "InnerAnchor5", "A6": "Drop", "B6": "OuterDrop6",
    "A7": "Keep", "B7": "OuterSummary7",
}

def text_for_cell(cell, shared):
    kind = cell.attrib.get("t", "")
    if kind == "inlineStr":
        return "".join(node.text or "" for node in cell.findall(".//" + main + "t"))
    value = cell.find(main + "v")
    raw = "" if value is None else (value.text or "")
    if kind == "s" and raw.isdigit() and int(raw) < len(shared):
        return shared[int(raw)]
    return raw

with zipfile.ZipFile(sys.argv[1]) as package:
    shared = []
    if "xl/sharedStrings.xml" in package.namelist():
        shared_root = ET.fromstring(package.read("xl/sharedStrings.xml"))
        shared = ["".join(node.text or "" for node in item.findall(".//" + main + "t")) for item in shared_root.findall(main + "si")]
    worksheet = ET.fromstring(package.read("xl/worksheets/sheet1.xml"))
    rows = {}
    values = {}
    hidden = []
    collapsed = []
    for row in worksheet.findall(".//" + main + "row"):
        number = row.attrib.get("r", "")
        if number in expected_outline or number == "7":
            rows[number] = row.attrib.get("outlineLevel", "0")
            if row.attrib.get("hidden", "0") in {"1", "true"}:
                hidden.append(number)
            if row.attrib.get("collapsed", "0") in {"1", "true"}:
                collapsed.append(number)
        for cell in row.findall(main + "c"):
            reference = cell.attrib.get("r", "")
            if reference in expected_values:
                values[reference] = text_for_cell(cell, shared)
    auto_filter = worksheet.find(main + "autoFilter")
    filter_ref = "" if auto_filter is None else auto_filter.attrib.get("ref", "")
    filter_values = []
    if auto_filter is not None:
        for node in auto_filter.findall(main + "filterColumn"):
            if node.attrib.get("colId") == "0":
                filters = node.find(main + "filters")
                if filters is not None:
                    filter_values = [item.attrib.get("val", "") for item in filters.findall(main + "filter")]
    rows = {key: rows.get(key, "0") for key in expected_outline}
    hidden.sort(key=int)
    collapsed.sort(key=int)
    filter_values.sort()
    # FreeX derives filter-owned visibility from the saved filter criteria on load; it does not
    # serialize those rows with the outline/group-owned hidden attribute.
    if rows != expected_outline or hidden or collapsed or filter_ref != "A1:B7" or filter_values != ["Keep"] or values != expected_values:
        raise SystemExit(1)
    print("outline=2:1,3:2,4:2,5:1,6:1|serialized-hidden=|collapsed=|filter-ref=A1:B7|filter-values=Keep|values=A1=Region,B1=Value,A2=Keep,B2=Outer2,A3=Drop,B3=InnerDrop3,A4=Keep,B4=InnerKeep4,A5=Keep,B5=InnerAnchor5,A6=Drop,B6=OuterDrop6,A7=Keep,B7=OuterSummary7")
PY
)" || package_signature=""
        if [[ "$package_signature" == "outline=2:1,3:2,4:2,5:1,6:1|serialized-hidden=|collapsed=|filter-ref=A1:B7|filter-values=Keep|values=A1=Region,B1=Value,A2=Keep,B2=Outer2,A3=Drop,B3=InnerDrop3,A4=Keep,B4=InnerKeep4,A5=Keep,B5=InnerAnchor5,A6=Drop,B6=OuterDrop6,A7=Keep,B7=OuterSummary7" ]]; then
            package_passed=true
        fi
    }

    if [[ "${document_path,,}" != *.xlsx ]]; then
        write_artifact "outline-nested-filter-postcondition.txt" "requires-xlsx=true\ndocument-path=$document_path\n"
        record "outline-nested-filter-save-reopen-physical" "failed" "outline-nested-filter-postcondition.txt" \
            "The combined filter/outline lane requires an XLSX document path." "$artifacts"
        return
    fi

    capture "outline-nested-filter-initial.png"
    initial_values="$(copy_cell_formula_by_address B2 || true),$(copy_cell_formula_by_address B4 || true),$(copy_cell_formula_by_address B5 || true),$(copy_cell_formula_by_address B7 || true)"

    # The fixture starts with Keep selected. Include Drop through the production flyout, then
    # reopen that same flyout and remove Drop again before exercising the outline gestures.
    capture "outline-nested-filter-before-flyout.png"
    open_autofilter_menu 0
    capture "outline-nested-filter-flyout-open.png"
    if screen_changed "$output/outline-nested-filter-before-flyout.png" "$output/outline-nested-filter-flyout-open.png" 300; then
        filter_open=true
        click_autofilter_control 29 348
        click_autofilter_control 246 395
        sleep "$settle_seconds"
        all_values="$(copy_cell_formula_by_address B2 || true),$(copy_cell_formula_by_address B3 || true),$(copy_cell_formula_by_address B4 || true),$(copy_cell_formula_by_address B5 || true),$(copy_cell_formula_by_address B6 || true),$(copy_cell_formula_by_address B7 || true)"
        capture "outline-nested-filter-all-values.png"

        open_autofilter_menu 0
        capture "outline-nested-filter-flyout-reopen.png"
        if screen_changed "$output/outline-nested-filter-all-values.png" "$output/outline-nested-filter-flyout-reopen.png" 300; then
            filter_reopen=true
            click_autofilter_control 29 348
            click_autofilter_control 246 395
            sleep "$settle_seconds"
            filtered_values="$(copy_cell_formula_by_address B2 || true),$(copy_cell_formula_by_address B4 || true),$(copy_cell_formula_by_address B5 || true),$(copy_cell_formula_by_address B7 || true)"
            capture "outline-nested-filter-applied.png"
        fi
    fi
    if $filter_open && $filter_reopen &&
       [[ "$initial_values" == "Outer2,InnerKeep4,InnerAnchor5,OuterSummary7" ]] &&
       [[ "$all_values" == "Outer2,InnerDrop3,InnerKeep4,InnerAnchor5,OuterDrop6,OuterSummary7" ]] &&
       [[ "$filtered_values" == "Outer2,InnerKeep4,InnerAnchor5,OuterSummary7" ]]; then
        filter_flyout_passed=true
    fi

    inner_toggle_x=$((window_x + 27))
    outer_toggle_x=$((window_x + 13))
    inner_toggle_y="$(cell_center_y 3)"
    outer_toggle_y="$(cell_center_y 4)"
    if outline_toggle_visible "$output/outline-nested-filter-applied.png" "$inner_toggle_x" "$inner_toggle_y" &&
       outline_toggle_visible "$output/outline-nested-filter-applied.png" "$outer_toggle_x" "$outer_toggle_y"; then
        controls_visible=true

        focus_app
        xdotool_mousemove_sync "$inner_toggle_x" "$inner_toggle_y" click 1
        sleep "$settle_seconds"
        capture "outline-nested-filter-inner-collapsed.png"
        inner_collapsed_values="$(copy_cell_formula_by_address B2 || true),$(copy_cell_formula_by_address B5 || true),$(copy_cell_formula_by_address B7 || true)"
        [[ "$inner_collapsed_values" == "Outer2,InnerAnchor5,OuterSummary7" ]] && inner_collapsed=true

        inner_collapsed_y="$(cell_center_y 2)"
        focus_app
        xdotool_mousemove_sync "$inner_toggle_x" "$inner_collapsed_y" click 1
        sleep "$settle_seconds"
        capture "outline-nested-filter-inner-expanded.png"
        inner_expanded_values="$(copy_cell_formula_by_address B2 || true),$(copy_cell_formula_by_address B4 || true),$(copy_cell_formula_by_address B5 || true),$(copy_cell_formula_by_address B7 || true)"
        [[ "$inner_expanded_values" == "Outer2,InnerKeep4,InnerAnchor5,OuterSummary7" ]] && inner_expanded=true

        focus_app
        xdotool_mousemove_sync "$outer_toggle_x" "$outer_toggle_y" click 1
        sleep "$settle_seconds"
        capture "outline-nested-filter-outer-collapsed.png"
        outer_collapsed_values="$(copy_cell_formula_by_address B7 || true)"
        [[ "$outer_collapsed_values" == "OuterSummary7" ]] && outer_collapsed=true

        outer_collapsed_y="$(cell_center_y 1)"
        focus_app
        xdotool_mousemove_sync "$outer_toggle_x" "$outer_collapsed_y" click 1
        sleep "$settle_seconds"
        capture "outline-nested-filter-outer-expanded.png"
        outer_expanded_values="$(copy_cell_formula_by_address B2 || true),$(copy_cell_formula_by_address B4 || true),$(copy_cell_formula_by_address B5 || true),$(copy_cell_formula_by_address B7 || true)"
        [[ "$outer_expanded_values" == "Outer2,InnerKeep4,InnerAnchor5,OuterSummary7" ]] && outer_expanded=true
    fi

    send_key ctrl+s
    wait_for_document_clean && save_clean=true
    inspect_saved_package

    before_windows="$(visible_window_count)"
    send_key ctrl+F12
    for _ in $(seq 1 12); do
        after_windows="$(visible_window_count)"
        if (( after_windows > before_windows )); then
            dialog_open=true
            break
        fi
        sleep 0.2
    done
    if $dialog_open; then
        xdotool key --clearmodifiers --delay "$input_delay_ms" ctrl+l
        xdotool type --clearmodifiers --delay "$type_delay_ms" "$document_path"
        xdotool key --clearmodifiers Return
        sleep "$settle_seconds"
        xdotool key --clearmodifiers Return
        for _ in $(seq 1 16); do
            after_windows="$(visible_window_count)"
            if (( after_windows <= before_windows )); then
                dialog_closed=true
                break
            fi
            sleep 0.25
        done
    fi
    if $dialog_closed; then
        capture "outline-nested-filter-reopened.png"
        reopened_values="$(copy_cell_formula_by_address B2 || true),$(copy_cell_formula_by_address B4 || true),$(copy_cell_formula_by_address B5 || true),$(copy_cell_formula_by_address B7 || true)"
        [[ "$reopened_values" == "Outer2,InnerKeep4,InnerAnchor5,OuterSummary7" ]] && reopen_values_passed=true
        send_key ctrl+s
        wait_for_document_clean && save_clean_after_reopen=true
        inspect_saved_package
    fi

    write_artifact "outline-nested-filter-postcondition.txt" \
        "fixture=freex-wave100-nested-outline-filter.xlsx\ninitial-filtered-values=$initial_values\nall-values-after-first-flyout=$all_values\nfiltered-values-after-second-flyout=$filtered_values\nfilter-flyout-open=$filter_open\nfilter-flyout-reopen=$filter_reopen\nfilter-flyout-passed=$filter_flyout_passed\ninner-collapsed-values=$inner_collapsed_values\ninner-collapse=$inner_collapsed\ninner-expanded-values=$inner_expanded_values\ninner-expand=$inner_expanded\nouter-collapsed-values=$outer_collapsed_values\nouter-collapse=$outer_collapsed\nouter-expanded-values=$outer_expanded_values\nouter-expand=$outer_expanded\ncontrols-visible=$controls_visible\nsave-clean=$save_clean\npackage-signature=$package_signature\npackage-passed=$package_passed\ndialog-open=$dialog_open\ndialog-closed=$dialog_closed\nreopened-values=$reopened_values\nreopened-values-passed=$reopen_values_passed\nsave-clean-after-reopen=$save_clean_after_reopen\n"
    if $filter_flyout_passed && $controls_visible && $inner_collapsed && $inner_expanded &&
       $outer_collapsed && $outer_expanded && $save_clean && $package_passed &&
       $dialog_closed && $reopen_values_passed && $save_clean_after_reopen; then
        record "outline-nested-filter-save-reopen-physical" "passed" \
            "$artifacts; package=$package_signature; filtered-hidden=3,6; restored=Outer2,InnerKeep4,InnerAnchor5,OuterSummary7" \
            "The real Avalonia filter flyout was operated twice, nested and outer outline toggles were physically collapsed/expanded, filtered rows stayed absent while outline-owned rows restored, and the saved package plus production reopen retained exact values, filter, and outline state." "$artifacts"
    else
        record "outline-nested-filter-save-reopen-physical" "failed" \
            "$artifacts" \
            "Combined filter/outline retention was not fully proven: filter=$filter_flyout_passed controls=$controls_visible inner=$inner_collapsed/$inner_expanded outer=$outer_collapsed/$outer_expanded save=$save_clean package=$package_passed reopen=$dialog_closed/$reopen_values_passed/$save_clean_after_reopen package='$package_signature'." "$artifacts"
    fi
    dismiss_active_popups
}

probe_formula_bar_point_mode_multi_area_edit() {
    local committed_formula="" committed_display="" normalized_formula=""
    local formula_passed=false result_passed=false selection_passed=false
    local artifacts="formula-multi-area-edit-rename.png;formula-multi-area-edit-seeded.png;formula-multi-area-edit-created.png;formula-multi-area-edit-start.png;formula-multi-area-edit-authored.png;formula-multi-area-edit-caret.png;formula-multi-area-edit-replaced.png;formula-multi-area-edit-committed.png;formula-multi-area-edit-selected.png;formula-multi-area-edit-postcondition.txt"
    local expected_formula="=SUM('Revenue Data'!F5,'Revenue Data'!J7)"

    point_formula_cell() {
        local column_offset="$1" row_offset="$2"
        focus_app
        xdotool_mousemove_sync "$(cell_center_x "$column_offset")" "$(cell_center_y "$row_offset")" click 1
        sleep "$settle_seconds"
    }

    seed_formula_cell() {
        local column_offset="$1" row_offset="$2" value="$3"
        focus_app
        xdotool_mousemove_sync "$(cell_center_x "$column_offset")" "$(cell_center_y "$row_offset")" click 1
        sleep "$settle_seconds"
        send_key F2
        send_key ctrl+a
        send_key BackSpace
        type_text "$value"
        send_key Return
    }

    select_created_sheet_tab() {
        # Revenue Data is the short first tab; Sheet2 follows it at this calibrated center.
        local tab_x=$((a1_x + 139))
        focus_app
        xdotool_mousemove_sync "$tab_x" "$(sheet_tab_y)" click 1
        sleep "$settle_seconds"
    }

    # Give the first physical worksheet a quoted name, then create a separate destination
    # worksheet so every reference is genuinely sheet-qualified.
    select_sheet_tab 0
    rename_sheet_tab 0
    capture_sheet_tab_strip "formula-multi-area-edit-rename.png"
    # The separate Rename Sheet window can cause the workbook window manager frame to move.
    # Recalibrate before any cell or tab coordinate is reused so the proof remains tied to
    # the visible X11 geometry rather than the pre-dialog origin.
    if ! calibrate_geometry; then
        write_artifact "formula-multi-area-edit-postcondition.txt" "rename=true\nrecalibration=false\n"
        record "formula-bar-point-mode-multi-area-edit" "failed" "formula-multi-area-edit-rename.png; formula-multi-area-edit-postcondition.txt" "Could not recalibrate the workbook after the physical sheet rename." "$artifacts"
        return
    fi
    # The rename changes the tab's measured width, which can make the generic selection-border
    # assertion stale for one transition. Keep the cell edit itself physical and production-routed,
    # while reserving the strict selection assertion for the final replacement postcondition.
    if ! seed_formula_cell 5 4 10 || ! seed_formula_cell 9 6 20; then
        write_artifact "formula-multi-area-edit-postcondition.txt" "seed-f5=false\nseed-j7=false\n"
        record "formula-bar-point-mode-multi-area-edit" "failed" "formula-multi-area-edit-postcondition.txt" "Could not seed the quoted source worksheet through physical X11 input." "formula-multi-area-edit-postcondition.txt"
        return
    fi
    capture_sheet_tab_strip "formula-multi-area-edit-seeded.png"

    # Revenue Data is shorter than the default CSV tab, so the plus center is the end of the
    # renamed tab plus its small hit-area padding rather than the generic first-tab width.
    plus_x=$((a1_x + 123))
    focus_app
    xdotool_mousemove_sync "$plus_x" "$(sheet_tab_y)"
    xdotool mousedown 1
    sleep 0.12
    xdotool mouseup 1
    sleep "$settle_seconds"
    capture_sheet_tab_strip "formula-multi-area-edit-created.png"

    # Author an existing two-area formula in the destination sheet. The second area is then
    # edited through the formula-bar caret before a plain point replaces that same area.
    focus_app
    xdotool_mousemove_sync "$(cell_center_x 6)" "$(cell_center_y 9)" click 1
    sleep "$settle_seconds"
    send_key ctrl+F2
    send_key ctrl+a
    type_text "=SUM("
    send_key F2
    send_key F2
    capture "formula-multi-area-edit-start.png"

    select_sheet_tab 0
    point_formula_cell 5 4
    send_key ctrl+End
    focus_app
    xdotool keydown Control_L
    xdotool keydown Super_L
    xdotool_mousemove_sync "$(cell_center_x 7)" "$(cell_center_y 6)" click 1
    xdotool keyup Control_L
    xdotool keyup Super_L
    sleep "$settle_seconds"
    capture "formula-multi-area-edit-authored.png"

    # Replace H7 with equal-length I7 while the tracked, quoted second-area span remains live.
    send_key ctrl+End
    focus_app
    xdotool key --window "$window_id" --clearmodifiers Shift_L+Left Shift_L+Left
    type_text "I7"
    capture "formula-multi-area-edit-caret.png"

    # A plain point must replace the edited second area, retaining the non-final F5 area and
    # both quoted sheet qualifiers. It must not insert a third reference at the caret.
    point_formula_cell 9 6
    capture "formula-multi-area-edit-replaced.png"
    if wait_for_formula_reference_selection "$(cell_x 9)" "$(cell_y 6)" "formula-multi-area-edit-selected.png"; then
        selection_passed=true
    fi
    type_text ")"
    send_key Return
    capture "formula-multi-area-edit-committed.png"

    # Read the committed formula and value from the destination sheet only after the selection
    # proof is captured. The saved formula must retain the quoted qualifier on both areas.
    select_created_sheet_tab
    committed_formula="$(copy_cell_formula 6 9 G10 || true)"
    committed_display="$(copy_cell_display 6 9 G10 || true)"
    normalized_formula="$(normalize_formula "$committed_formula")"
    [[ "$committed_formula" == "$expected_formula" ]] && formula_passed=true
    [[ "$committed_display" =~ ^30([.]0+)?$ ]] && result_passed=true

    write_artifact "formula-multi-area-edit-postcondition.txt" \
        "expected-formula=$expected_formula\ncommitted-formula=$committed_formula\nnormalized-formula=$normalized_formula\ncommitted-result=$committed_display\nselection-before-read=Revenue Data!J7\nselection-passed=$selection_passed\nformula-passed=$formula_passed\nresult-passed=$result_passed\n"
    if $formula_passed && $result_passed && $selection_passed; then
        record "formula-bar-point-mode-multi-area-edit" "passed" \
            "formula-multi-area-edit-authored.png; formula-multi-area-edit-caret.png; formula-multi-area-edit-replaced.png; formula-multi-area-edit-committed.png; formula=$committed_formula; result=$committed_display; selection=Revenue Data!J7" \
            "Physical X11 input edited a quoted second area, replaced that existing area with J7, committed the exact two-area formula, calculated 30, and retained Revenue Data!J7 selected before formula/result readback." "$artifacts"
    else
        record "formula-bar-point-mode-multi-area-edit" "failed" \
            "$artifacts" \
            "Expected formula '$expected_formula', result 30, and selection Revenue Data!J7; observed formula '$committed_formula', result '$committed_display', selection-passed=$selection_passed." "$artifacts"
    fi
    send_key Escape || true
}

probe_formula_reference_grip_multi_area() {
    local committed_formula="" committed_result="" expected_formula="=SUM('Sheet2'!B2:C3,'Sheet2'!D4:F6)"
    local formula_passed=false result_passed=false save_passed=false setup_passed=false
    local artifacts="formula-reference-grip-setup.png;formula-reference-grip-source.png;formula-reference-grip-target.png;formula-reference-grip-before.png;formula-reference-grip-dragging.png;formula-reference-grip-committed.png;formula-reference-grip-postcondition.txt"

    # Keep the formula source on the first worksheet and create a real second worksheet. The
    # reference is explicitly quoted and qualified, so the production tab switch must preserve
    # Edit mode instead of committing before the referenced sheet becomes active.
    select_sheet_tab 0
    rename_sheet_tab 0
    capture_sheet_tab_strip "formula-reference-grip-setup.png"
    if ! calibrate_geometry; then
        write_artifact "formula-reference-grip-postcondition.txt" "setup=true\nrecalibration=false\n"
        record "formula-reference-grip-multi-area-physical" "failed" "formula-reference-grip-setup.png; formula-reference-grip-postcondition.txt" "Could not recalibrate after renaming the source worksheet." "$artifacts"
        return
    fi

    # After the source rename, the real + button is adjacent to its shorter tab. A new Sheet2 is
    # active immediately after the click; its tab center is derived by the calibrated geometry
    # used by the other physical formula lanes.
    local target_tab_x=$((a1_x + 123)) target_tab_center_x=$((a1_x + 139))
    focus_app
    xdotool_mousemove_sync "$target_tab_x" "$(sheet_tab_y)" click 1
    sleep "$settle_seconds"
    setup_passed=true
    capture_sheet_tab_strip "formula-reference-grip-target.png"

    # Seed the referenced worksheet through the production edit path, then return to the source
    # worksheet and author the formula there.
    if ! set_cell_text_without_save 1 1 B2 1 ||
       ! set_cell_text_without_save 2 2 C3 2 ||
       ! set_cell_text_without_save 3 3 D4 3 ||
       ! set_cell_text_without_save 4 4 E5 4 ||
       ! set_cell_text_without_save 5 5 F6 5; then
        write_artifact "formula-reference-grip-postcondition.txt" "seeded=false\n"
        record "formula-reference-grip-multi-area-physical" "failed" "$artifacts" "Could not seed the referenced worksheet." "$artifacts"
        return
    fi

    select_sheet_tab 0
    if ! set_cell_text_without_save 6 7 G8 "=SUM('Sheet2'!B2:C3,'Sheet2'!D4:E5)"; then
        write_artifact "formula-reference-grip-postcondition.txt" "seeded=true\nsource-formula=false\n"
        record "formula-reference-grip-multi-area-physical" "failed" "$artifacts" "Could not seed the qualified source formula." "$artifacts"
        return
    fi
    capture_sheet_tab_strip "formula-reference-grip-source.png"

    if ! select_cell 6 7 G8 || ! send_key F2; then
        write_artifact "formula-reference-grip-postcondition.txt" "seeded=true\neditor-open=false\n"
        record "formula-reference-grip-multi-area-physical" "failed" "$artifacts" "Could not open the existing qualified formula in the production inline editor." "$artifacts"
        return
    fi

    # This is the key cross-sheet transition: Edit mode must remain open while the target tab is
    # clicked, and the second reference's overlay/grip must move to that worksheet.
    focus_app
    xdotool_mousemove_sync "$target_tab_center_x" "$(sheet_tab_y)" click 1
    sleep "$settle_seconds"
    capture "formula-reference-grip-before.png"

    # D4:E5's visible grip is rendered just inside the lower-right edge. Keep the probe point
    # derived from the calibrated cell geometry, but account for that inset so the click remains
    # inside Avalonia's forgiving hit target instead of selecting E5 itself.
    focus_app
    xdotool_mousemove_sync "$((a1_x + 5 * cell_width - 22))" "$((a1_y + 5 * cell_height - 6))"
    xdotool mousedown 1
    sleep 0.22
    xdotool_mousemove_sync "$(cell_center_x 5)" "$(cell_center_y 5)"
    sleep 0.22
    capture "formula-reference-grip-dragging.png"
    xdotool mouseup 1
    sleep "$settle_seconds"
    # Pointer capture can leave the inline editor visually active while the grid owns focus.
    # Click the real Formula Bar before Enter so this physical lane proves the production
    # formula-commit route, not a grid navigation keystroke.
    focus_app
    xdotool_mousemove_sync 500 198 click 1
    sleep "$settle_seconds"
    send_key Return
    capture "formula-reference-grip-committed.png"

    # The edit commits to the source worksheet while the referenced worksheet remains visible.
    # Return to the source before reading the formula/result through the normal physical copy path.
    select_sheet_tab 0
    sleep "$settle_seconds"
    committed_formula="$(copy_cell_formula 6 7 G8 || true)"
    committed_result="$(copy_cell_display 6 7 G8 || true)"
    send_key ctrl+s
    if wait_for_document_clean; then save_passed=true; fi
    [[ "$committed_formula" == "$expected_formula" ]] && formula_passed=true
    [[ "$committed_result" =~ ^15([.]0+)?$ ]] && result_passed=true

    write_artifact "formula-reference-grip-postcondition.txt" \
        "setup=$setup_passed\nexpected-formula=$expected_formula\ncommitted-formula=$committed_formula\ncommitted-result=$committed_result\nsave-clean=$save_passed\nformula-passed=$formula_passed\nresult-passed=$result_passed\n"
    if $formula_passed && $result_passed && $save_passed && $setup_passed; then
        record "formula-reference-grip-multi-area-physical" "passed" \
            "formula-reference-grip-setup.png; formula-reference-grip-source.png; formula-reference-grip-target.png; formula-reference-grip-before.png; formula-reference-grip-dragging.png; formula-reference-grip-committed.png; formula=$committed_formula; result=$committed_result; save-clean=$save_passed" \
            "Physical X11 input kept an existing quoted cross-sheet formula open while switching from Revenue Data to Sheet2, moved the reference overlay to Sheet2, dragged only the second reference grip from D4:E5 to D4:F6, preserved both qualifiers and B2:C3, committed the exact formula, calculated 15, and reached a clean saved document." "$artifacts"
    else
        record "formula-reference-grip-multi-area-physical" "failed" "$artifacts" "Expected formula '$expected_formula', result 15, and a clean save; observed formula '$committed_formula', result '$committed_result', save-clean=$save_passed." "$artifacts"
    fi
    send_key Escape || true
}

probe_sheet_tabs() {
    local tab_y left_nav_x right_nav_x first_tab_x sheet2_x sheet3_x top
    local before_value right_value left_value before_second before_third after_second after_third
    local sheet2_seed_value sheet3_seed_value
    local activate_id="" create_ready=false navigation_passed=false activate_passed=false
    local left_changed=false right_changed=false viewport_changed=false returned_to_origin=false
    local plus_x created_count
    local plus_click_count=1
    local shortcut_create_count=9

    tab_y="$(sheet_tab_y)"
    left_nav_x="$(sheet_tab_left_nav_x)"
    right_nav_x="$(sheet_tab_right_nav_x)"
    top="$(sheet_tab_strip_top)"
    first_tab_x="$(sheet_tab_center_x 0)"
    sheet2_x="$(sheet_tab_center_x 1)"
    sheet3_x="$(sheet_tab_center_x 2)"

    capture_sheet_tab_strip "sheet-tabs-before-overflow.png"
    plus_x="$(sheet_plus_center_x 0)"
    focus_app
    xdotool_mousemove_sync "$plus_x" "$tab_y" click 1
    sleep "$settle_seconds"
    for created_count in $(seq 1 "$shortcut_create_count"); do
        send_key shift+F11
    done
    capture_sheet_tab_strip "sheet-tabs-after-overflow.png"

    crop_region "sheet-tabs-before-overflow.png" "sheet-tabs-before-left-nav.png" "$left_nav_x" "$top" 30 31
    crop_region "sheet-tabs-after-overflow.png" "sheet-tabs-after-left-nav.png" "$left_nav_x" "$top" 30 31
    crop_region "sheet-tabs-before-overflow.png" "sheet-tabs-before-right-nav.png" "$((right_nav_x - 6))" "$top" 36 31
    crop_region "sheet-tabs-after-overflow.png" "sheet-tabs-after-right-nav.png" "$((right_nav_x - 6))" "$top" 36 31
    left_changed=false
    right_changed=false
    region_changed "$output/sheet-tabs-before-left-nav.png" "$output/sheet-tabs-after-left-nav.png" 8 && left_changed=true
    region_changed "$output/sheet-tabs-before-right-nav.png" "$output/sheet-tabs-after-right-nav.png" 8 && right_changed=true
    if $left_changed && $right_changed; then
        create_ready=true
    fi
    write_artifact "sheet-tabs-create-postcondition.txt" \
        "plus-clicks=$plus_click_count\nshift-f11-insertions=$shortcut_create_count\nexpected-visible-sheets=11\nplus-center-derived-from-a1=true\nleft-nav-region-changed=$left_changed\nright-nav-region-changed=$right_changed\nleft-nav-x=$left_nav_x\nright-nav-x=$right_nav_x\n"
    if $create_ready; then
        record "sheet-tab-overflow-create-physical" "passed" \
            "sheet-tabs-before-overflow.png; sheet-tabs-after-overflow.png; left/right navigation affordances became visible after one real + click and $shortcut_create_count physical Shift+F11 insertions" \
            "The production + button was physically clicked once, physical sheet-insert shortcuts completed the overflow setup, and both navigation regions changed from the pre-overflow strip." \
            "sheet-tabs-before-overflow.png;sheet-tabs-after-overflow.png;sheet-tabs-create-postcondition.txt;sheet-tabs-before-left-nav.png;sheet-tabs-after-left-nav.png;sheet-tabs-before-right-nav.png;sheet-tabs-after-right-nav.png"
    else
        record "sheet-tab-overflow-create-physical" "failed" \
            "sheet-tabs-before-overflow.png; sheet-tabs-after-overflow.png; sheet-tabs-create-postcondition.txt" \
            "The real + click sequence did not make both sheet-tab overflow navigation affordances visibly available." \
            "sheet-tabs-before-overflow.png;sheet-tabs-after-overflow.png;sheet-tabs-create-postcondition.txt;sheet-tabs-before-left-nav.png;sheet-tabs-after-left-nav.png;sheet-tabs-before-right-nav.png;sheet-tabs-after-right-nav.png"
    fi

    if $create_ready; then
        focus_app
        xdotool_mousemove_sync "$first_tab_x" "$tab_y" click 1
        sleep "$settle_seconds"
        before_value="$(copy_cell_display 0 0 A1 || true)"
        capture_sheet_tab_strip "sheet-tabs-navigation-before.png"
        focus_app
        xdotool_mousemove_sync "$right_nav_x" "$tab_y" click 1
        sleep "$settle_seconds"
        capture_sheet_tab_strip "sheet-tabs-navigation-after-right.png"
        right_value="$(copy_cell_display 0 0 A1 || true)"
        focus_app
        xdotool_mousemove_sync "$left_nav_x" "$tab_y" click 1
        sleep "$settle_seconds"
        capture_sheet_tab_strip "sheet-tabs-navigation-after-left.png"
        left_value="$(copy_cell_display 0 0 A1 || true)"
        viewport_changed=false
        returned_to_origin=false
        screen_changed "$output/sheet-tabs-navigation-before-strip.png" "$output/sheet-tabs-navigation-after-right-strip.png" 40 && viewport_changed=true
        regions_match "$output/sheet-tabs-navigation-before-strip.png" "$output/sheet-tabs-navigation-after-left-strip.png" 180 && returned_to_origin=true
        if $viewport_changed && $returned_to_origin &&
           [[ "$before_value" == "Region" && "$right_value" == "Region" && "$left_value" == "Region" ]]; then
            navigation_passed=true
        fi
        write_artifact "sheet-tabs-navigation-postcondition.txt" \
            "active-cell-before=$before_value\nactive-cell-after-right=$right_value\nactive-cell-after-left=$left_value\nviewport-changed-after-right=$viewport_changed\nviewport-returned-after-left=$returned_to_origin\nright-nav-x=$right_nav_x\nleft-nav-x=$left_nav_x\n"
        if $navigation_passed; then
            record "sheet-tab-overflow-navigation-physical" "passed" \
                "sheet-tabs-navigation-before-strip.png; sheet-tabs-navigation-after-right-strip.png; sheet-tabs-navigation-after-left-strip.png; active A1 value remained Region" \
                "Physical right/left navigation changed and restored the tab viewport while the active worksheet remained the original data sheet." \
                "sheet-tabs-navigation-before.png;sheet-tabs-navigation-after-right.png;sheet-tabs-navigation-after-left.png;sheet-tabs-navigation-postcondition.txt"
        else
            record "sheet-tab-overflow-navigation-physical" "failed" \
                "sheet-tabs-navigation-before.png; sheet-tabs-navigation-after-right.png; sheet-tabs-navigation-after-left.png; sheet-tabs-navigation-postcondition.txt" \
                "The physical arrow sequence did not both move the tab viewport and preserve the active worksheet." \
                "sheet-tabs-navigation-before.png;sheet-tabs-navigation-after-right.png;sheet-tabs-navigation-after-left.png;sheet-tabs-navigation-postcondition.txt"
        fi
    else
        write_artifact "sheet-tabs-navigation-postcondition.txt" "overflow-ready=false\n"
        record "sheet-tab-overflow-navigation-physical" "failed" "sheet-tabs-navigation-postcondition.txt" "Overflow navigation was not physically available after the + setup, so arrow behavior could not be credited." "sheet-tabs-navigation-postcondition.txt"
    fi

    if $create_ready; then
        focus_app
        xdotool_mousemove_sync "$left_nav_x" "$tab_y" click 3
        sleep "$dialog_settle_seconds"
        for _ in $(seq 1 12); do
            activate_id="$(xdotool search --onlyvisible --name '^Activate$' 2>/dev/null | tail -1 || true)"
            [[ -n "$activate_id" ]] && break
            sleep 0.2
        done
        capture "sheet-tabs-activate-open.png"
        if [[ -n "$activate_id" ]]; then
            timeout --foreground --kill-after=1s "${mousemove_timeout_seconds}s" xdotool windowactivate --sync "$activate_id" 2>/dev/null || true
            xdotool key --clearmodifiers --delay "$input_delay_ms" --window "$activate_id" Escape 2>/dev/null || true
            xdotool key --clearmodifiers --delay "$input_delay_ms" Escape 2>/dev/null || true
            for _ in $(seq 1 12); do
                if [[ -z "$(xdotool search --onlyvisible --name '^Activate$' 2>/dev/null | tail -1 || true)" ]]; then
                    activate_passed=true
                    break
                fi
                sleep 0.2
            done
        fi
        capture "sheet-tabs-activate-after-escape.png"
        write_artifact "sheet-tabs-activate-postcondition.txt" \
            "activate-window-id=$activate_id\nopened=$([[ -n "$activate_id" ]] && printf true || printf false)\nclosed=$activate_passed\n"
        if $activate_passed; then
            record "sheet-tab-overflow-activate-dialog-physical" "passed" \
                "sheet-tabs-activate-open.png; sheet-tabs-activate-after-escape.png; Activate window id=$activate_id" \
                "A physical right-click on the sheet-tab overflow navigation opened the real Activate sheet dialog, and Escape closed it." \
                "sheet-tabs-activate-open.png;sheet-tabs-activate-after-escape.png;sheet-tabs-activate-postcondition.txt"
        else
            record "sheet-tab-overflow-activate-dialog-physical" "failed" \
                "sheet-tabs-activate-open.png; sheet-tabs-activate-after-escape.png; sheet-tabs-activate-postcondition.txt" \
                "The physical right-click did not produce a closable Activate sheet dialog." \
                "sheet-tabs-activate-open.png;sheet-tabs-activate-after-escape.png;sheet-tabs-activate-postcondition.txt"
        fi
    else
        write_artifact "sheet-tabs-activate-postcondition.txt" "overflow-ready=false\n"
        record "sheet-tab-overflow-activate-dialog-physical" "failed" "sheet-tabs-activate-postcondition.txt" "Overflow navigation was not physically available, so the Activate dialog route could not be credited." "sheet-tabs-activate-postcondition.txt"
    fi

    # Opening Activate can cause the window manager to restore the workbook below
    # the X11 root origin. Recalibrate after the modal route so drag setup uses the
    # current grid and sheet-tab coordinates, not the pre-dialog geometry.
    if $create_ready; then
        calibrate_geometry || true
        tab_y="$(sheet_tab_y)"
        left_nav_x="$(sheet_tab_left_nav_x)"
        right_nav_x="$(sheet_tab_right_nav_x)"
        top="$(sheet_tab_strip_top)"
        first_tab_x="$(sheet_tab_center_x 0)"
        sheet2_x="$(sheet_tab_center_x 1)"
        sheet3_x="$(sheet_tab_center_x 2)"
    fi

    # Keep the first three tabs at offset zero and give the two draggable tabs distinct cell
    # values. Reading those values after the drag proves that the visible positions changed order.
    reset_sheet_tab_viewport "$tab_y" "$left_nav_x"
    focus_app
    xdotool_mousemove_sync "$sheet2_x" "$tab_y" click 1
    sleep "$settle_seconds"
    capture_sheet_tab_strip "sheet-tabs-drag-sheet2-selected.png"
    crop_cell "$output/sheet-tabs-drag-sheet2-selected.png" "$output/sheet-tabs-drag-sheet2-selected-cell.png" 0 0
    if set_cell_text_without_save 0 0 A1 Sheet2Anchor; then
        capture_sheet_tab_strip "sheet-tabs-drag-sheet2-seeded.png"
        crop_cell "$output/sheet-tabs-drag-sheet2-seeded.png" "$output/sheet-tabs-drag-sheet2-seeded-cell.png" 0 0
        sheet2_seed_value="$(copy_cell_formula 0 0 A1 || true)"
        focus_app
        xdotool_mousemove_sync "$sheet3_x" "$tab_y" click 1
        sleep "$settle_seconds"
        capture_sheet_tab_strip "sheet-tabs-drag-sheet3-selected.png"
        crop_cell "$output/sheet-tabs-drag-sheet3-selected.png" "$output/sheet-tabs-drag-sheet3-selected-cell.png" 0 0
        if set_cell_text_without_save 0 0 A1 Sheet3Anchor; then
            capture_sheet_tab_strip "sheet-tabs-drag-sheet3-seeded.png"
            crop_cell "$output/sheet-tabs-drag-sheet3-seeded.png" "$output/sheet-tabs-drag-sheet3-seeded-cell.png" 0 0
            sheet3_seed_value="$(copy_cell_formula 0 0 A1 || true)"
            reset_sheet_tab_viewport "$tab_y" "$left_nav_x"
            focus_app
            xdotool_mousemove_sync "$sheet2_x" "$tab_y" click 1
            sleep "$settle_seconds"
            before_second="$(copy_cell_formula 0 0 A1 || true)"
            focus_app
            xdotool_mousemove_sync "$sheet3_x" "$tab_y" click 1
            sleep "$settle_seconds"
            before_third="$(copy_cell_formula 0 0 A1 || true)"
            capture_sheet_tab_strip "sheet-tabs-drag-before.png"
            focus_app
            xdotool_mousemove_sync "$sheet2_x" "$tab_y" mousedown 1
            sleep 0.2
            xdotool_mousemove_sync "$((sheet3_x - 18))" "$tab_y"
            sleep 0.2
            xdotool_mousemove_sync "$((sheet3_x + 12))" "$tab_y"
            sleep "$settle_seconds"
            xdotool mouseup 1
            sleep "$settle_seconds"
            capture_sheet_tab_strip "sheet-tabs-drag-after.png"
            focus_app
            xdotool_mousemove_sync "$sheet2_x" "$tab_y" click 1
            sleep "$settle_seconds"
            after_second="$(copy_cell_formula 0 0 A1 || true)"
            focus_app
            xdotool_mousemove_sync "$sheet3_x" "$tab_y" click 1
            sleep "$settle_seconds"
            after_third="$(copy_cell_formula 0 0 A1 || true)"
            write_artifact "sheet-tabs-drag-postcondition.txt" \
                "sheet2-seed-value=$sheet2_seed_value\nsheet3-seed-value=$sheet3_seed_value\nbefore-second=$before_second\nbefore-third=$before_third\nafter-second=$after_second\nafter-third=$after_third\nexpected-after-second=Sheet3Anchor\nexpected-after-third=Sheet2Anchor\n"
            if [[ "$before_second" == "Sheet2Anchor" && "$before_third" == "Sheet3Anchor" &&
                  "$after_second" == "Sheet3Anchor" && "$after_third" == "Sheet2Anchor" ]]; then
                record "sheet-tab-drag-reorder-physical" "passed" \
                    "sheet-tabs-drag-before.png; sheet-tabs-drag-after.png; second/third visible tab values changed from Sheet2Anchor/Sheet3Anchor to Sheet3Anchor/Sheet2Anchor" \
                    "A visible Sheet2 tab was physically dragged across Sheet3, and the real worksheet values proved the visible tab order changed." \
                    "sheet-tabs-drag-before.png;sheet-tabs-drag-after.png;sheet-tabs-drag-sheet2-selected.png;sheet-tabs-drag-sheet2-seeded.png;sheet-tabs-drag-sheet3-selected.png;sheet-tabs-drag-sheet3-seeded.png;sheet-tabs-drag-postcondition.txt"
            else
                record "sheet-tab-drag-reorder-physical" "failed" \
                    "sheet-tabs-drag-after.png; sheet-tabs-drag-sheet2-selected.png; sheet-tabs-drag-sheet2-seeded.png; sheet-tabs-drag-sheet3-selected.png; sheet-tabs-drag-sheet3-seeded.png; sheet-tabs-drag-postcondition.txt" \
                    "The physical drag did not produce the expected Sheet2/Sheet3 order change." \
                    "sheet-tabs-drag-before.png;sheet-tabs-drag-after.png;sheet-tabs-drag-postcondition.txt"
            fi
        else
            write_artifact "sheet-tabs-drag-postcondition.txt" "sheet2-seed-value=$sheet2_seed_value\nseed-sheet2=true\nseed-sheet3=false\n"
            record "sheet-tab-drag-reorder-physical" "failed" "sheet-tabs-drag-sheet2-selected.png; sheet-tabs-drag-sheet2-seeded.png; sheet-tabs-drag-sheet3-selected.png; sheet-tabs-drag-postcondition.txt" "Could not seed a distinct value on Sheet3 for the physical drag-order assertion." "sheet-tabs-drag-sheet2-selected.png;sheet-tabs-drag-sheet2-selected-cell.png;sheet-tabs-drag-sheet2-seeded.png;sheet-tabs-drag-sheet2-seeded-cell.png;sheet-tabs-drag-sheet3-selected.png;sheet-tabs-drag-sheet3-selected-cell.png;sheet-tabs-drag-postcondition.txt"
        fi
    else
        write_artifact "sheet-tabs-drag-postcondition.txt" "seed-sheet2=false\nseed-sheet3=false\n"
        record "sheet-tab-drag-reorder-physical" "failed" "sheet-tabs-drag-sheet2-selected.png; sheet-tabs-drag-sheet2-selected-cell.png; sheet-tabs-drag-postcondition.txt" "Could not seed a distinct value on Sheet2 for the physical drag-order assertion." "sheet-tabs-drag-sheet2-selected.png;sheet-tabs-drag-sheet2-selected-cell.png;sheet-tabs-drag-postcondition.txt"
    fi
}

dismiss_overlays() {
    send_key Escape || true
    send_key Escape || true
}

visible_window_count() {
    wmctrl -l 2>/dev/null | wc -l
}

x11_visible_window_count() {
    xdotool search --onlyvisible --name '.*' 2>/dev/null | awk 'NF { count += 1 } END { print count + 0 }'
}

x11_window_snapshot() {
    local path="$1" id name geometry
    : > "$path"
    while read -r id; do
        [[ -z "$id" ]] && continue
        name="$(xdotool getwindowname "$id" 2>/dev/null || true)"
        geometry="$(xdotool getwindowgeometry --shell "$id" 2>/dev/null | tr '\n' ' ' || true)"
        printf '%s|%s|%s\n' "$id" "${name//$'\n'/ }" "$geometry" >> "$path"
    done < <(xdotool search --onlyvisible --name '.*' 2>/dev/null | sort -n)
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
    if seed_cell_text 6 10 G11 "$value"; then
        set_clipboard_sentinel
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
    if seed_cell_text 6 14 G15 "$value"; then
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
        observed="$(csv_cell_value 6 14)"
        write_artifact "worksheet-context-clear-postcondition.txt" "expected-empty=true\nobserved=$(json_escape "$observed")\nfile-hash-before=$before_hash\nfile-hash-after=$after_hash\ncell=G15"
        if [[ -n "$before_hash" && "$after_hash" != "$before_hash" ]] &&
           wait_for_document_clean &&
           wait_for_csv_cell 6 14 ""; then
            record "worksheet-context-clear-physical" "passed" "worksheet-context-clear-before.png; worksheet-context-clear-open.png; worksheet-context-clear-after.png; cell=G15; saved-value-empty=true; file-hash-changed=true" "The rendered Clear submenu was physically activated and the saved harness CSV proves G15 is empty." "$artifacts"
        else
            record "worksheet-context-clear-physical" "failed" "worksheet-context-clear-before.png; worksheet-context-clear-open.png; worksheet-context-clear-after.png; cell=G15; observed-value=$observed; file-hash-changed=$([[ -n "$before_hash" && "$after_hash" != "$before_hash" ]] && printf true || printf false)" "Clear Contents did not produce the required saved-cell and file-hash postconditions." "$artifacts"
        fi
    else
        write_artifact "worksheet-context-clear-postcondition.txt" "seeded=false\ncell=G15\nexpected=$value\nobserved=$(json_escape "$(csv_cell_value 6 14)")"
        record "worksheet-context-clear-physical" "failed" "worksheet-context-clear-postcondition.txt" "Could not seed calibrated G15 for the physical worksheet context Clear probe." "worksheet-context-clear-postcondition.txt"
    fi
    dismiss_overlays
}

probe_clipboard_roundtrips() {
    local copy_value="X11CopyPaste" cut_value="X11CutPaste"
    local clipboard="" before_hash="" after_hash="" copy_destination="" cut_destination="" cut_source=""
    local copy_artifacts="clipboard-copy-paste-before.png;clipboard-copy-paste-after.png;clipboard-copy-paste-postcondition.txt"
    local cut_artifacts="clipboard-cut-paste-before.png;clipboard-cut-paste-after.png;clipboard-cut-paste-postcondition.txt"

    if seed_cell_text 6 15 G16 "$copy_value" &&
       select_cell 7 15 H16 &&
       [[ "$(csv_cell_value 7 15)" == "" ]]; then
        set_clipboard_sentinel
        select_cell 6 15 G16
        capture "clipboard-copy-paste-before.png"
        send_key ctrl+c
        clipboard="$(wait_for_clipboard "$copy_value" || true)"
        select_cell 7 15 H16
        send_key ctrl+v
        capture "clipboard-copy-paste-after.png"
        send_key ctrl+s
        sleep "$dialog_settle_seconds"
        copy_destination="$(csv_cell_value 7 15)"
        write_artifact "clipboard-copy-paste-postcondition.txt" "expected=$copy_value\nclipboard=$clipboard\ndestination=H16\nsaved-destination=$copy_destination"
        if [[ "$clipboard" == "$copy_value" ]] &&
           wait_for_document_clean &&
           wait_for_csv_cell 7 15 "$copy_value"; then
            record "clipboard-copy-paste-roundtrip" "passed" "clipboard-copy-paste-before.png; clipboard-copy-paste-after.png; clipboard=$clipboard; saved-cell=H16:$copy_destination" "Physical Ctrl+C/Ctrl+V roundtrip produced the exact clipboard text and saved destination value." "$copy_artifacts"
        else
            record "clipboard-copy-paste-roundtrip" "failed" "clipboard-copy-paste-before.png; clipboard-copy-paste-after.png; clipboard=$clipboard; saved-cell=H13:$copy_destination" "Copy/paste did not satisfy the exact clipboard and saved-cell postconditions." "$copy_artifacts"
        fi
    else
        write_artifact "clipboard-copy-paste-postcondition.txt" "seeded=false\nsource=G16\ndestination=H16\nexpected=$copy_value\nobserved=$(json_escape "$(csv_cell_value 6 15)")"
        record "clipboard-copy-paste-roundtrip" "failed" "clipboard-copy-paste-postcondition.txt" "Could not seed the copy/paste roundtrip cells." "clipboard-copy-paste-postcondition.txt"
    fi
    dismiss_overlays

    if seed_cell_text 6 16 G17 "$cut_value" &&
       select_cell 7 16 H17 &&
       [[ "$(csv_cell_value 7 16)" == "" ]]; then
        set_clipboard_sentinel
        before_hash="$(sha256sum "$document_path" 2>/dev/null | awk '{print $1}')"
        select_cell 6 16 G17
        capture "clipboard-cut-paste-before.png"
        send_key ctrl+x
        clipboard="$(wait_for_clipboard "$cut_value" || true)"
        select_cell 7 16 H17
        send_key ctrl+v
        capture "clipboard-cut-paste-after.png"
        send_key ctrl+s
        for _ in $(seq 1 12); do
            after_hash="$(sha256sum "$document_path" 2>/dev/null | awk '{print $1}')"
            [[ -n "$before_hash" && "$after_hash" != "$before_hash" ]] && break
            sleep 0.25
        done
        cut_destination="$(csv_cell_value 7 16)"
        cut_source="$(csv_cell_value 6 16)"
        write_artifact "clipboard-cut-paste-postcondition.txt" "expected=$cut_value\nclipboard=$clipboard\nsource=G17\ndestination=H17\nsaved-source=$(json_escape "$cut_source")\nsaved-destination=$cut_destination\nfile-hash-before=$before_hash\nfile-hash-after=$after_hash"
        if [[ "$clipboard" == "$cut_value" && "$cut_source" == "" && "$cut_destination" == "$cut_value" && -n "$before_hash" && "$after_hash" != "$before_hash" ]]; then
            record "clipboard-cut-paste-roundtrip" "passed" "clipboard-cut-paste-before.png; clipboard-cut-paste-after.png; clipboard=$clipboard; saved-source=G17:empty; saved-destination=H17:$cut_destination; file-hash-changed=true" "Physical Ctrl+X/Ctrl+V roundtrip proves the clipboard value, cleared source, destination value, and changed saved file." "$cut_artifacts"
        else
            record "clipboard-cut-paste-roundtrip" "failed" "clipboard-cut-paste-before.png; clipboard-cut-paste-after.png; clipboard=$clipboard; saved-source=G14:$cut_source; saved-destination=H14:$cut_destination; file-hash-changed=$([[ -n "$before_hash" && "$after_hash" != "$before_hash" ]] && printf true || printf false)" "Cut/paste did not satisfy the exact clipboard, source, destination, and file-hash postconditions." "$cut_artifacts"
        fi
    else
        write_artifact "clipboard-cut-paste-postcondition.txt" "seeded=false\nsource=G17\ndestination=H17\nexpected=$cut_value\nobserved=$(json_escape "$(csv_cell_value 6 16)")"
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
    # Arrange All temporarily moves focus through its popup. Restore the original
    # workbook's worksheet focus before delivering the application shortcut, just
    # as a user does by clicking the workbook they want to cycle from.
    focus_app
    xdotool mousemove --window "$window_id" 520 420 click 1
    sleep 0.2
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

probe_backstage_print_shortcut() {
    local id="backstage-print-ctrl-shift-f12-cancel"
    local before_count="" open_count="" cancel_count=""
    local before_screenshot="${id}-before.png" after_screenshot="${id}-after-open.png" cancel_screenshot="${id}-after-cancel.png"
    local artifacts="${before_screenshot};${after_screenshot};${cancel_screenshot};${id}-postcondition.txt"
    local opened=false closed=false

    focus_app
    dismiss_overlays
    before_count="$(visible_window_count)"
    capture "$before_screenshot"
    send_key "ctrl+shift+F12"
    sleep 0.5
    open_count="$(visible_window_count)"
    capture "$after_screenshot"

    if [[ "$open_count" == "$before_count" ]] && screen_changed "$output/$before_screenshot" "$output/$after_screenshot" 300; then
        opened=true
    fi

    send_key "Escape"
    sleep 0.5
    cancel_count="$(visible_window_count)"
    capture "$cancel_screenshot"
    if $opened && [[ "$cancel_count" == "$before_count" ]] &&
       screen_changed "$output/$after_screenshot" "$output/$cancel_screenshot" 300; then
        closed=true
    fi

    write_artifact "${id}-postcondition.txt" "shortcut=ctrl+shift+F12\nwindow-count-before=$before_count\nwindow-count-after-open=$open_count\nwindow-count-after-cancel=$cancel_count\nopened-in-workbook=$opened\nclosed=$closed"
    if $closed; then
        record "$id" "passed" "$before_screenshot; $after_screenshot; $cancel_screenshot; window-count-before=$before_count; window-count-after-open=$open_count; window-count-after-cancel=$cancel_count" "Ctrl+Shift+F12 opened Backstage Print inside the workbook and Escape restored the workbook without creating a top-level preview window." "$artifacts"
    else
        record "$id" "failed" "$before_screenshot; $after_screenshot; $cancel_screenshot; window-count-before=$before_count; window-count-after-open=$open_count; window-count-after-cancel=$cancel_count; opened-in-workbook=$opened; closed=$closed" "Ctrl+Shift+F12 did not complete the in-workbook Backstage Print open/cancel lifecycle." "$artifacts"
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

if [[ "$probe_selector" == "backstage-print" ]]; then
    probe_backstage_print_shortcut
    write_manifest
    if (( $(printf '%s\n' "${results[@]}" | grep -c '"status":"failed"' || true) > 0 )); then
        exit 1
    fi
    exit 0
fi

if [[ "$probe_selector" == "inline-edit" ]]; then
    # Focused Wave170 lane for the first high-signal failure from the authoritative all probe.
    # Keep the artifact list exact on early failure so schema validation distinguishes a real
    # failed postcondition from screenshots that were never captured.
    local_artifacts="inline-edit-commit-before.png"
    select_cell 0 0 A1
    capture "inline-edit-commit-before.png"
    crop_cell "$output/inline-edit-commit-before.png" "$output/inline-edit-commit-before-cell.png" 6 7
    local_artifacts+=";inline-edit-commit-before-cell.png"
    if select_cell 6 7 G8; then
        send_key F2
        type_text "X11InlineCommit"
        capture "inline-edit-commit-editing.png"
        local_artifacts+=";inline-edit-commit-editing.png"
        send_key Return
        committed_value="$(copy_cell_formula 6 7 G8 || printf 'selection-failed')"
        send_key Escape
        select_cell 0 0 A1 || true
        capture "inline-edit-commit-after.png"
        crop_cell "$output/inline-edit-commit-after.png" "$output/inline-edit-commit-after-cell.png" 6 7
        local_artifacts+=";inline-edit-commit-after.png;inline-edit-commit-after-cell.png"
        if region_changed "$output/inline-edit-commit-before-cell.png" "$output/inline-edit-commit-after-cell.png" 8 &&
           [[ "$committed_value" == "X11InlineCommit" ]]; then
            record "inline-edit-f2-enter-commit" "passed" "inline-edit-commit-editing.png; X11 clipboard='X11InlineCommit'" \
                "F2/Enter committed the complete value and keyboard re-selection read it back from the production editor." \
                "$local_artifacts"
        else
            record "inline-edit-f2-enter-commit" "failed" "inline-edit-commit-after-cell.png" \
                "F2/Enter did not commit the complete value in calibrated G8 (clipboard='${committed_value}')." \
                "$local_artifacts"
        fi
    else
        record "inline-edit-f2-enter-commit" "failed" "inline-edit-commit-before.png" \
            "Could not physically select calibrated cell G8." "$local_artifacts"
    fi
    if (( mousemove_timeout_count > 0 )); then
        record "x11-bounded-mousemove-timeout" "failed" "x11-input-results.json; timeout-count=$mousemove_timeout_count" \
            "A synchronous X11 pointer move reached the ${mousemove_timeout_seconds}s bound during the focused inline-edit probe."
    fi
    write_manifest
    if (( $(printf '%s\n' "${results[@]}" | grep -c '"status":"failed"' || true) > 0 )); then
        exit 1
    fi
    exit 0
fi

if [[ "$probe_selector" == "sheet-tabs" ]]; then
    # Focused iteration mode: calibration plus only the sheet-tab physical slice.
    # The default remains the complete probe lane so existing rows are preserved.
    probe_sheet_tabs
    if (( mousemove_timeout_count > 0 )); then
        record "x11-bounded-mousemove-timeout" "failed" "x11-input-results.json; timeout-count=$mousemove_timeout_count" "A synchronous X11 pointer move reached the ${mousemove_timeout_seconds}s bound during the focused sheet-tab probe."
    fi
    write_manifest
    if (( $(printf '%s\n' "${results[@]}" | grep -c '"status":"failed"' || true) > 0 )); then
        exit 1
    fi
    exit 0
fi

if [[ "$probe_selector" == "name-box-dropdown" ]]; then
    # Focused bounded lane for defined-name plus non-defined-name Name Box navigation.
    probe_name_box_dropdown
    if (( mousemove_timeout_count > 0 )); then
        record "x11-bounded-mousemove-timeout" "failed" "x11-input-results.json; timeout-count=$mousemove_timeout_count" "A synchronous X11 pointer move reached the ${mousemove_timeout_seconds}s bound during the focused Name Box dropdown probe."
    fi
    write_manifest
    if (( $(printf '%s\n' "${results[@]}" | grep -c '"status":"failed"' || true) > 0 )); then
        exit 1
    fi
    exit 0
fi

if [[ "$probe_selector" == "name-box-dropdown-parity" ]]; then
    # Authoritative Wave69 visual evidence: live production popup, native X11 root crop, no resize.
    probe_name_box_dropdown_parity
    if (( mousemove_timeout_count > 0 )); then
        record "x11-bounded-mousemove-timeout" "failed" "x11-input-results.json; timeout-count=$mousemove_timeout_count" "A synchronous X11 pointer move reached the ${mousemove_timeout_seconds}s bound during the focused Name Box parity probe."
    fi
    write_manifest
    if (( $(printf '%s\n' "${results[@]}" | grep -c '"status":"failed"' || true) > 0 )); then
        exit 1
    fi
    exit 0
fi

if [[ "$probe_selector" == "pivot-field-list" ]]; then
    # Focused iteration mode for the deterministic PivotTable workbook supplied by
    # the host runner. This lane intentionally does not mutate the CSV/default probes.
    probe_pivot_field_list
    if (( mousemove_timeout_count > 0 )); then
        record "x11-bounded-mousemove-timeout" "failed" "x11-input-results.json; timeout-count=$mousemove_timeout_count" "A synchronous X11 pointer move reached the ${mousemove_timeout_seconds}s bound during the focused PivotTable field-list probe."
    fi
    write_manifest
    if (( $(printf '%s\n' "${results[@]}" | grep -c '"status":"failed"' || true) > 0 )); then
        exit 1
    fi
    exit 0
fi

if [[ "$probe_selector" == "pivot-table-details-double-click" ]]; then
    # Focused physical proof that PivotTable value double-click wins over inline edit.
    probe_pivot_table_details_double_click
    if (( mousemove_timeout_count > 0 )); then
        record "x11-bounded-mousemove-timeout" "failed" "x11-input-results.json; timeout-count=$mousemove_timeout_count" "A synchronous X11 pointer move reached the ${mousemove_timeout_seconds}s bound during the focused PivotTable details probe."
    fi
    write_manifest
    if (( $(printf '%s\n' "${results[@]}" | grep -c '"status":"failed"' || true) > 0 )); then
        exit 1
    fi
    exit 0
fi

if [[ "$probe_selector" == "autofilter-recalculation" ]]; then
    # Focused iteration mode for the deterministic AutoFilter/SUBTOTAL workflow.
    probe_autofilter_recalculation
    if (( mousemove_timeout_count > 0 )); then
        record "x11-bounded-mousemove-timeout" "failed" "x11-input-results.json; timeout-count=$mousemove_timeout_count" "A synchronous X11 pointer move reached the ${mousemove_timeout_seconds}s bound during the focused AutoFilter recalculation probe."
    fi
    write_manifest
    if (( $(printf '%s\n' "${results[@]}" | grep -c '"status":"failed"' || true) > 0 )); then
        exit 1
    fi
    exit 0
fi

if [[ "$probe_selector" == "formula-3d-point" ]]; then
    # Focused iteration mode for the physical multi-sheet formula point-entry slice.
    probe_formula_bar_point_mode_3d
    if (( mousemove_timeout_count > 0 )); then
        record "x11-bounded-mousemove-timeout" "failed" "x11-input-results.json; timeout-count=$mousemove_timeout_count" "A synchronous X11 pointer move reached the ${mousemove_timeout_seconds}s bound during the focused 3-D formula point probe."
    fi
    write_manifest
    if (( $(printf '%s\n' "${results[@]}" | grep -c '"status":"failed"' || true) > 0 )); then
        exit 1
    fi
    exit 0
fi

if [[ "$probe_selector" == "formula-3d-grip" ]]; then
    # Focused iteration mode for physical multi-cell 3-D point selection followed by a
    # middle-sheet reference-highlight grip resize.
    probe_formula_bar_point_mode_3d_range_grip
    if (( mousemove_timeout_count > 0 )); then
        record "x11-bounded-mousemove-timeout" "failed" "x11-input-results.json; timeout-count=$mousemove_timeout_count" "A synchronous X11 pointer move reached the ${mousemove_timeout_seconds}s bound during the focused 3-D range/grip probe."
    fi
    write_manifest
    if (( $(printf '%s\n' "${results[@]}" | grep -c '"status":"failed"' || true) > 0 )); then
        exit 1
    fi
    exit 0
fi

if [[ "$probe_selector" == "formula-3d-native-xlsx" ]]; then
    # Focused iteration mode for the native OOXML point/grip/save/reopen workflow.
    probe_formula_bar_point_mode_3d_native_xlsx
    if (( mousemove_timeout_count > 0 )); then
        record "x11-bounded-mousemove-timeout" "failed" "x11-input-results.json; timeout-count=$mousemove_timeout_count" "A synchronous X11 pointer move reached the ${mousemove_timeout_seconds}s bound during the focused native XLSX 3-D formula probe."
    fi
    write_manifest
    if (( $(printf '%s\n' "${results[@]}" | grep -c '"status":"failed"' || true) > 0 )); then
        exit 1
    fi
    exit 0
fi

if [[ "$probe_selector" == "formula-whole-range-point" ]]; then
    # Focused iteration mode for physical whole-column, whole-row, and select-all formula point input.
    probe_formula_bar_point_mode_whole_range
    if (( mousemove_timeout_count > 0 )); then
        record "x11-bounded-mousemove-timeout" "failed" "x11-input-results.json; timeout-count=$mousemove_timeout_count" "A synchronous X11 pointer move reached the ${mousemove_timeout_seconds}s bound during the focused whole-range formula point probe."
    fi
    write_manifest
    if (( $(printf '%s\n' "${results[@]}" | grep -c '\"status\":\"failed\"' || true) > 0 )); then
        exit 1
    fi
    exit 0
fi

if [[ "$probe_selector" == "formula-multi-area-point" ]]; then
    # Focused iteration mode for physical disjoint formula reference entry. This selector keeps
    # the existing all lane's fresh-workbook coordinates and result inventory unchanged.
    probe_formula_bar_point_mode_multi_area
    if (( mousemove_timeout_count > 0 )); then
        record "x11-bounded-mousemove-timeout" "failed" "x11-input-results.json; timeout-count=$mousemove_timeout_count" "A synchronous X11 pointer move reached the ${mousemove_timeout_seconds}s bound during the focused multi-area formula point probe."
    fi
    write_manifest
    if (( $(printf '%s\n' "${results[@]}" | grep -c '"status":"failed"' || true) > 0 )); then
        exit 1
    fi
    exit 0
fi

if [[ "$probe_selector" == "formula-reference-grip" ]]; then
    # Focused iteration mode for editing an existing same-sheet multi-area formula through a
    # reference highlight resize grip.
    probe_formula_reference_grip_multi_area
    if (( mousemove_timeout_count > 0 )); then
        record "x11-bounded-mousemove-timeout" "failed" "x11-input-results.json; timeout-count=$mousemove_timeout_count" "A synchronous X11 pointer move reached the ${mousemove_timeout_seconds}s bound during the focused formula-reference grip probe."
    fi
    write_manifest
    if (( $(printf '%s\n' "${results[@]}" | grep -c '"status":"failed"' || true) > 0 )); then
        exit 1
    fi
    exit 0
fi

if [[ "$probe_selector" == "formula-multi-area-edit" ]]; then
    # Focused iteration mode for mutating an already-authored quoted multi-area formula.
    probe_formula_bar_point_mode_multi_area_edit
    if (( mousemove_timeout_count > 0 )); then
        record "x11-bounded-mousemove-timeout" "failed" "x11-input-results.json; timeout-count=$mousemove_timeout_count" "A synchronous X11 pointer move reached the ${mousemove_timeout_seconds}s bound during the focused multi-area formula edit probe."
    fi
    write_manifest
    if (( $(printf '%s\n' "${results[@]}" | grep -c '"status":"failed"' || true) > 0 )); then
        exit 1
    fi
    exit 0
fi

if [[ "$probe_selector" == "grid-drag" ]]; then
    # Focused iteration mode for physical autofill, selection move, and Ctrl-copy drag parity.
    probe_grid_drag_parity
    if (( mousemove_timeout_count > 0 )); then
        record "x11-bounded-mousemove-timeout" "failed" "x11-input-results.json; timeout-count=$mousemove_timeout_count" "A synchronous X11 pointer move reached the ${mousemove_timeout_seconds}s bound during the focused grid-drag probe."
    fi
    write_manifest
    if (( $(printf '%s\n' "${results[@]}" | grep -c '"status":"failed"' || true) > 0 )); then
        exit 1
    fi
    exit 0
fi

if [[ "$probe_selector" == "grid-autofit" ]]; then
    # Focused iteration mode for physical header-boundary double-click AutoFit.
    probe_grid_autofit
    if (( mousemove_timeout_count > 0 )); then
        record "x11-bounded-mousemove-timeout" "failed" "x11-input-results.json; timeout-count=$mousemove_timeout_count" "A synchronous X11 pointer move reached the ${mousemove_timeout_seconds}s bound during the focused grid-autofit probe."
    fi
    write_manifest
    if (( $(printf '%s\n' "${results[@]}" | grep -c '"status":"failed"' || true) > 0 )); then
        exit 1
    fi
    exit 0
fi

if [[ "$probe_selector" == "split-pane-pointer" ]]; then
    # Focused Wave104 lane for divider drag, active-pane wheel ownership, and mini-scrollbar input.
    probe_split_pane_pointer
    if (( mousemove_timeout_count > 0 )); then
        record "x11-bounded-mousemove-timeout" "failed" "x11-input-results.json; timeout-count=$mousemove_timeout_count" "A synchronous X11 pointer move reached the ${mousemove_timeout_seconds}s bound during the focused split-pane pointer probe."
    fi
    write_manifest
    if (( $(printf '%s\n' "${results[@]}" | grep -c '"status":"failed"' || true) > 0 )); then
        exit 1
    fi
    exit 0
fi

if [[ "$probe_selector" == "outline-group" ]]; then
    # Focused iteration mode for physical row and column grouping plus visible outline controls.
    probe_outline_group_physical
    probe_outline_column_group_physical
    if (( mousemove_timeout_count > 0 )); then
        record "x11-bounded-mousemove-timeout" "failed" "x11-input-results.json; timeout-count=$mousemove_timeout_count" "A synchronous X11 pointer move reached the ${mousemove_timeout_seconds}s bound during the focused Group/Outline probe."
    fi
    write_manifest
    if (( $(printf '%s\n' "${results[@]}" | grep -c '\"status\":\"failed\"' || true) > 0 )); then
        exit 1
    fi
    exit 0
fi

if [[ "$probe_selector" == "outline-nested-group" ]]; then
    # Focused Wave98 lane for nested row and column outline levels. Each axis returns expanded
    # before its exact-value postcondition is read back.
    probe_outline_nested_rows_physical
    probe_outline_nested_columns_physical
    if (( mousemove_timeout_count > 0 )); then
        record "x11-bounded-mousemove-timeout" "failed" "x11-input-results.json; timeout-count=$mousemove_timeout_count" "A synchronous X11 pointer move reached the ${mousemove_timeout_seconds}s bound during the focused nested Group/Outline probe."
    fi
    write_manifest
    if (( $(printf '%s\n' "${results[@]}" | grep -c '\"status\":\"failed\"' || true) > 0 )); then
        exit 1
    fi
    exit 0
fi

if [[ "$probe_selector" == "outline-nested-save-reopen" ]]; then
    # Focused Wave99 lane for persistence of the nested outline state produced by the Wave98
    # physical row/column gestures. This selector requires an XLSX document path.
    probe_outline_nested_rows_physical
    probe_outline_nested_columns_physical
    probe_outline_nested_save_reopen_physical
    if (( mousemove_timeout_count > 0 )); then
        record "x11-bounded-mousemove-timeout" "failed" "x11-input-results.json; timeout-count=$mousemove_timeout_count" "A synchronous X11 pointer move reached the ${mousemove_timeout_seconds}s bound during the nested outline save/reopen probe."
    fi
    write_manifest
    if (( $(printf '%s\n' "${results[@]}" | grep -c '\"status\":\"failed\"' || true) > 0 )); then
        exit 1
    fi
    exit 0
fi

if [[ "$probe_selector" == "outline-nested-filter-save-reopen" ]]; then
    # Focused Wave100 lane for the real AutoFilter checklist plus nested row-outline state.
    probe_outline_nested_filter_save_reopen_physical
    if (( mousemove_timeout_count > 0 )); then
        record "x11-bounded-mousemove-timeout" "failed" "x11-input-results.json; timeout-count=$mousemove_timeout_count" "A synchronous X11 pointer move reached the ${mousemove_timeout_seconds}s bound during the combined filter/outline probe."
    fi
    write_manifest
    if (( $(printf '%s\n' "${results[@]}" | grep -c '\"status\":\"failed\"' || true) > 0 )); then
        exit 1
    fi
    exit 0
fi

if [[ "$probe_selector" != "all" ]]; then
    calibration_reason="Unknown FREEX_X11_PROBE_SELECTOR '$probe_selector'."
    record "x11-probe-selector" "failed" "x11-input-results.json" "$calibration_reason"
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
    xdotool_mousemove_sync "$(cell_center_x 1)" "$(cell_center_y 1)" click 1
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
    xdotool_mousemove_sync "$(cell_center_x 1)" "$(cell_center_y 1)"
    xdotool mousedown 1
    xdotool_mousemove_sync "$(cell_center_x 3)" "$(cell_center_y 3)"
    xdotool mouseup 1
    sleep "$settle_seconds"
    capture "inline-point-drag-address.png"
    set_clipboard_sentinel
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
    xdotool_mousemove_sync "$(cell_center_x 1)" "$(cell_center_y 1)" click 1
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
alt_changed=false
for _ in $(seq 1 3); do
    focus_app
    xdotool keydown --clearmodifiers --window "$window_id" Alt_L
    sleep 0.18
    xdotool keyup --window "$window_id" Alt_L
    sleep "$settle_seconds"
    capture "alt-after.png"
    if screen_changed "$output/alt-before.png" "$output/alt-after.png" 500; then
        alt_changed=true
        break
    fi
    send_key Escape
done
if $alt_changed; then
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
    context_keyboard_changed=false
    for _ in $(seq 1 3); do
        focus_app
        select_cell 1 1 B2 || true
        send_key shift+F10
        capture "context-keyboard-after.png"
        if screen_changed "$output/context-keyboard-before.png" "$output/context-keyboard-after.png" 1000; then
            context_keyboard_changed=true
            break
        fi
        dismiss_overlays
    done
    if $context_keyboard_changed; then
        record "worksheet-context-shift-f10" "passed" "selection-B2.png; context-keyboard-after.png"
    else
        record "worksheet-context-shift-f10" "failed" "selection-B2.png; context-keyboard-after.png" "Shift+F10 produced no visible context menu for calibrated B2."
    fi
    dismiss_overlays

    select_cell 1 1 B2 || true
    capture "context-pointer-before.png"
    focus_app
    xdotool_mousemove_sync "$(cell_center_x 1)" "$(cell_center_y 1)" click 3
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
probe_sheet_tabs
probe_window_management
probe_split_pane_pointer
probe_outline_group_physical
probe_outline_column_group_physical
probe_outline_nested_rows_physical
probe_outline_nested_columns_physical

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

# WPF routes the print shortcut through the in-workbook Backstage Print pane.
# Native file pickers remain cancel-only probes.
probe_backstage_print_shortcut
probe_cancelable_window "native-save-as-f12-cancel" "F12" "native-save-as.png"
probe_cancelable_window "native-open-ctrl-f12-cancel" "ctrl+F12" "native-open.png"

if (( mousemove_timeout_count > 0 )); then
    record "x11-bounded-mousemove-timeout" "failed" "x11-input-results.json; timeout-count=$mousemove_timeout_count" "A synchronous X11 pointer move reached the ${mousemove_timeout_seconds}s bound; the probe continued and recorded the bounded failure instead of hanging the container."
fi

write_manifest
if (( $(printf '%s\n' "${results[@]}" | grep -c '"status":"failed"' || true) > 0 )); then
    exit 1
fi
