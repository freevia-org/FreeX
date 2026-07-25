#!/usr/bin/env bash
set -Eeuo pipefail

export DISPLAY="${DISPLAY:-:99}"
output="${1:-/work/field-shortcut-validation}"
input_delay_ms="${FIELD_X11_INPUT_DELAY_MS:-180}"
settle_seconds="${FIELD_X11_SETTLE_SECONDS:-0.65}"
pointer_timeout_seconds="${FIELD_X11_POINTER_TIMEOUT_SECONDS:-3}"
document_path="${FIELD_DOCUMENT_PATH:-/documents/field-shortcut-fixture.docx}"

mkdir -p "$output"
declare -a results=()
declare -a screenshots=()
required_ids=(
    "visible-window-discovery"
    "field-code-shortcut-show"
    "field-code-shortcut-hide"
    "field-update-shortcut-persist"
)
window_id=""
window_title=""
manifest_written=false

json_escape() {
    local value="$1"
    value="${value//\\/\\\\}"
    value="${value//\"/\\\"}"
    value="${value//$'\n'/\\n}"
    printf '%s' "$value"
}

record() {
    local id="$1" status="$2" note="$3"
    shift 3
    local evidence evidence_json="" separator=""
    for evidence in "$@"; do
        evidence_json+="$separator\"$(json_escape "$evidence")\""
        separator=","
    done
    results+=("{\"id\":\"$(json_escape "$id")\",\"category\":\"physical-x11-field-shortcut\",\"status\":\"$status\",\"evidenceLevel\":\"physical-x11-input\",\"evidence\":[${evidence_json}],\"note\":\"$(json_escape "$note")\"}")
}

has_result() {
    local wanted="$1" result
    for result in "${results[@]}"; do
        [[ "$result" == *"\"id\":\"$(json_escape "$wanted")\""* ]] && return 0
    done
    return 1
}

track_screenshot() {
    screenshots+=("{\"name\":\"$(json_escape "$1")\",\"kind\":\"screenshot\"}")
}

capture() {
    local name="$1"
    scrot -o "$output/$name"
    track_screenshot "$name"
}

capture_editor_region() {
    local source="$1" name="$2"
    # Stable page/editor band at the harness default 1280x820 desktop size; the crop remains useful
    # evidence at larger sizes and is intentionally separate from the full-screen screenshot.
    convert "$output/$source" -crop 900x520+160+170 +repage "$output/$name"
}

region_hash() {
    sha256sum "$output/$1" | cut -d' ' -f1
}

screen_difference() {
    local before="$1" after="$2" changed
    changed="$(compare -metric AE "$output/$before" "$output/$after" null: 2>&1 || true)"
    if [[ "$changed" =~ ^[0-9]+ ]]; then
        printf '%s' "${BASH_REMATCH[0]}"
    else
        printf 'unknown'
    fi
}

capture_window_state() {
    local name="$1"
    {
        printf 'phase=%s\n' "$name"
        printf 'owner-window-id=%s\n' "$window_id"
        printf 'window-title=%s\n' "$window_title"
        printf 'active-window=%s\n' "$(xdotool getactivewindow 2>/dev/null || true)"
        printf 'focus-window=%s\n' "$(xdotool getwindowfocus 2>/dev/null || true)"
        printf 'owner-active=%s\n' "$(if [[ "$(xdotool getactivewindow 2>/dev/null || true)" == "$window_id" ]]; then printf true; else printf false; fi)"
        printf 'owner-focused=%s\n' "$(if [[ "$(xdotool getwindowfocus 2>/dev/null || true)" == "$window_id" ]]; then printf true; else printf false; fi)"
        wmctrl -l 2>/dev/null || true
    } > "$output/$name-state.txt"
}

owner_has_focus() {
    local name="$1"
    grep -Fxq 'owner-active=true' "$output/$name-state.txt" \
        && grep -Fxq 'owner-focused=true' "$output/$name-state.txt"
}

focus_app() {
    timeout --foreground --kill-after=1s "$pointer_timeout_seconds" xdotool windowactivate --sync "$window_id" 2>/dev/null || true
    timeout --foreground --kill-after=1s "$pointer_timeout_seconds" xdotool windowfocus "$window_id" 2>/dev/null || true
    sleep 0.15
}

send_key() {
    focus_app
    timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
        xdotool key --clearmodifiers --delay "$input_delay_ms" --window "$window_id" "$1"
    sleep "$settle_seconds"
}

write_manifest() {
    local passed=0 failed=0 result first=true screenshot screenshot_first=true
    for result in "${results[@]}"; do
        if [[ "$result" == *'\"status\":\"passed\"'* ]]; then
            ((passed += 1))
        else
            ((failed += 1))
        fi
    done
    {
        printf '{\"schemaVersion\":1,\"suite\":\"freew-linux-field-shortcut-physical\",\"platform\":\"linux\",\"shell\":\"avalonia\",\"app\":\"FreeW\",\"baseline\":false,\"appSurface\":\"document-editor-field-shortcuts\"'
        printf ',\"coverage\":{\"scope\":\"physical Alt+F9/F9 field shortcut lane\",\"exhaustive\":false}'
        printf ',\"window\":{\"id\":\"%s\",\"title\":\"%s\",\"pattern\":\"FreeW\",\"visible\":true}' \
            "$(json_escape "$window_id")" "$(json_escape "$window_title")"
        printf ',\"screenshots\":['
        for screenshot in "${screenshots[@]}"; do
            if $screenshot_first; then screenshot_first=false; else printf ','; fi
            printf '%s' "$screenshot"
        done
        printf ']'
        printf ',\"summary\":{\"passed\":%d,\"failed\":%d,\"total\":%d}' "$passed" "$failed" "$((passed + failed))"
        printf ',\"results\":['
        for result in "${results[@]}"; do
            if $first; then first=false; else printf ','; fi
            printf '%s' "$result"
        done
        printf ']}\n'
    } > "$output/field-shortcut-results.json"
    manifest_written=true
}

on_exit() {
    local exit_code=$?
    trap - ERR EXIT
    if [[ "$exit_code" -ne 0 ]]; then
        printf 'Probe command failed at line %s (exit %s).\n' "${BASH_LINENO[0]:-unknown}" "$exit_code" > "$output/probe-runtime-error.txt"
        local required_id
        for required_id in "${required_ids[@]}"; do
            if ! has_result "$required_id"; then
                record "$required_id" failed "Probe exited before collecting this required row (exit $exit_code)." probe-runtime-error.txt
            fi
        done
        if (( ${#screenshots[@]} == 0 )); then
            scrot -o "$output/probe-failure.png" >/dev/null 2>&1 || true
            if [[ -f "$output/probe-failure.png" ]]; then track_screenshot probe-failure.png; fi
        fi
        write_manifest
    fi
    exit "$exit_code"
}
trap on_exit EXIT

mapfile -t visible_windows < <(xdotool search --onlyvisible --name 'FreeW' 2>/dev/null || true)
if (( ${#visible_windows[@]} == 0 )); then
    printf 'No visible FreeW window was discovered.\n' > "$output/window-discovery-error.txt"
    record visible-window-discovery failed "No visible FreeW window matched the physical probe." window-discovery-error.txt
    write_manifest
    exit 2
fi

window_id="${visible_windows[${#visible_windows[@]}-1]}"
window_title="$(xdotool getwindowname "$window_id" 2>/dev/null || printf FreeW)"
capture baseline.png
capture_editor_region baseline.png baseline-editor-region.png
capture_window_state baseline
printf 'window-id=%s\nwindow-title=%s\n' "$window_id" "$window_title" > "$output/baseline-window-proof.txt"
record visible-window-discovery passed "Discovered the real visible FreeW Avalonia window and captured focus state." baseline.png baseline-editor-region.png baseline-state.txt baseline-window-proof.txt

initial_hash="$(region_hash baseline-editor-region.png)"
send_key alt+F9
capture field-code.png
capture_editor_region field-code.png field-code-editor-region.png
capture_window_state field-code
code_hash="$(region_hash field-code-editor-region.png)"
show_delta="$(screen_difference baseline-editor-region.png field-code-editor-region.png)"
printf 'initial-region-sha256=%s\ncode-region-sha256=%s\neditor-difference=%s\nkey-dispatch=xdotool alt+F9\n' \
    "$initial_hash" "$code_hash" "$show_delta" > "$output/field-code-show-proof.txt"
printf 'owner-active=%s\nowner-focused=%s\n' \
    "$(grep -F 'owner-active=' "$output/field-code-state.txt" | tail -n 1)" \
    "$(grep -F 'owner-focused=' "$output/field-code-state.txt" | tail -n 1)" >> "$output/field-code-show-proof.txt"
if [[ "$show_delta" =~ ^[0-9]+ ]] && (( show_delta > 100 )) && owner_has_focus field-code; then
    record field-code-shortcut-show passed "Real Alt+F9 changed the editor region while the FreeW window retained focus." baseline.png field-code.png baseline-editor-region.png field-code-editor-region.png baseline-state.txt field-code-state.txt field-code-show-proof.txt
else
    record field-code-shortcut-show failed "Real Alt+F9 did not produce a measurable editor-region transition." baseline.png field-code.png baseline-editor-region.png field-code-editor-region.png field-code-show-proof.txt
fi

send_key alt+F9
capture field-code-restored.png
capture_editor_region field-code-restored.png field-code-restored-editor-region.png
capture_window_state field-code-restored
restored_hash="$(region_hash field-code-restored-editor-region.png)"
hide_delta="$(screen_difference field-code-editor-region.png field-code-restored-editor-region.png)"
roundtrip_delta="$(screen_difference baseline-editor-region.png field-code-restored-editor-region.png)"
printf 'code-region-sha256=%s\nrestored-region-sha256=%s\ncode-to-restored-difference=%s\ninitial-to-restored-difference=%s\nkey-dispatch=xdotool alt+F9\n' \
    "$code_hash" "$restored_hash" "$hide_delta" "$roundtrip_delta" > "$output/field-code-hide-proof.txt"
printf 'owner-active=%s\nowner-focused=%s\n' \
    "$(grep -F 'owner-active=' "$output/field-code-restored-state.txt" | tail -n 1)" \
    "$(grep -F 'owner-focused=' "$output/field-code-restored-state.txt" | tail -n 1)" >> "$output/field-code-hide-proof.txt"
if [[ "$hide_delta" =~ ^[0-9]+ ]] && (( hide_delta > 100 )) && [[ "$roundtrip_delta" =~ ^[0-9]+ ]] && (( roundtrip_delta < 5000 )) && owner_has_focus field-code-restored; then
    record field-code-shortcut-hide passed "A second real Alt+F9 restored the result view and returned the editor region close to its initial state." field-code.png field-code-restored.png field-code-editor-region.png field-code-restored-editor-region.png field-code-state.txt field-code-restored-state.txt field-code-hide-proof.txt
else
    record field-code-shortcut-hide failed "The second real Alt+F9 did not prove a round-trip visual restoration." field-code.png field-code-restored.png field-code-editor-region.png field-code-restored-editor-region.png field-code-hide-proof.txt
fi

before_sha="$(sha256sum "$document_path" | cut -d' ' -f1)"
printf 'document-before-save=%s\n' "$before_sha" > "$output/field-update-before-save.txt"
send_key F9
capture field-update-after-f9.png
capture_window_state field-update-after-f9
send_key ctrl+s
capture field-update-after-save.png
capture_window_state field-update-after-save
after_sha="$(sha256sum "$document_path" | cut -d' ' -f1)"
printf 'key-dispatch=xdotool F9 then ctrl+s\ndocument-path=%s\ndocument-before-save=%s\ndocument-after-save=%s\nfile-changed=%s\nstructured-inspection=performed-by-host-validator\n' \
    "$document_path" "$before_sha" "$after_sha" "$(if [[ "$before_sha" != "$after_sha" ]]; then printf true; else printf false; fi)" > "$output/field-update-shortcut-state.txt"
if [[ -f "$document_path" && "$before_sha" != "$after_sha" ]]; then
    record field-update-shortcut-persist passed "Real F9 followed by Ctrl+S changed the harness-owned DOCX; host validation must prove the exact persisted TITLE cache." field-update-before-save.txt field-update-after-f9.png field-update-after-save.png field-update-after-f9-state.txt field-update-after-save-state.txt field-update-shortcut-state.txt
else
    record field-update-shortcut-persist failed "F9/Ctrl+S did not change the harness-owned DOCX." field-update-before-save.txt field-update-after-f9.png field-update-after-save.png field-update-shortcut-state.txt
fi

write_manifest
