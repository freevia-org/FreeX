#!/usr/bin/env bash
set -euo pipefail

export DISPLAY="${DISPLAY:-:99}"

output="${1:-/work/family-validation}"
input_delay_ms="${FAMILY_X11_INPUT_DELAY_MS:-160}"
settle_seconds="${FAMILY_X11_SETTLE_SECONDS:-0.45}"
app="${FAMILY_APP:?FAMILY_APP is required (FreeW or FreeP)}"
window_pattern="${FAMILY_WINDOW_PATTERN:?FAMILY_WINDOW_PATTERN is required}"
tab_key="${FAMILY_TAB_KEY:?FAMILY_TAB_KEY is required}"
file_key="${FAMILY_FILE_KEY:-F}"
file_surface="${FAMILY_FILE_SURFACE:?FAMILY_FILE_SURFACE is required}"

mkdir -p "$output"

declare -a results=()
declare -a screenshots=()
manifest_written=false
window_id=""
window_title=""

json_escape() {
    local value="$1"
    value="${value//\\/\\\\}"
    value="${value//\"/\\\"}"
    value="${value//$'\n'/\\n}"
    printf '%s' "$value"
}

record() {
    local id="$1" status="$2" evidence="$3" note="${4:-}"
    results+=("{\"id\":\"$(json_escape "$id")\",\"category\":\"physical-x11-smoke\",\"status\":\"$status\",\"evidenceLevel\":\"physical-x11-input\",\"evidence\":[\"$(json_escape "$evidence")\"],\"note\":\"$(json_escape "$note")\"}")
}

track_screenshot() {
    local name="$1"
    screenshots+=("{\"name\":\"$(json_escape "$name")\",\"kind\":\"screenshot\"}")
}

capture() {
    local name="$1"
    scrot -o "$output/$name"
    track_screenshot "$name"
}

write_manifest() {
    local passed=0 failed=0 result first=true screenshot screenshot_first=true
    for result in "${results[@]}"; do
        if [[ "$result" == *'"status":"passed"'* ]]; then
            ((passed += 1))
        else
            ((failed += 1))
        fi
    done

    {
        printf '{\"schemaVersion\":1,\"suite\":\"family-linux-physical-baseline\",\"platform\":\"linux\",\"shell\":\"avalonia\"'
        printf ',\"app\":\"%s\",\"baseline\":true,\"appSurface\":\"%s\"' \
            "$(json_escape "$app")" "$(json_escape "$file_surface")"
        printf ',\"window\":{\"id\":\"%s\",\"title\":\"%s\",\"pattern\":\"%s\",\"visible\":true}' \
            "$(json_escape "$window_id")" "$(json_escape "$window_title")" "$(json_escape "$window_pattern")"
        printf ',\"parameters\":{\"ribbonTabKey\":\"%s\",\"fileKey\":\"%s\",\"fileSurface\":\"%s\"}' \
            "$(json_escape "$tab_key")" "$(json_escape "$file_key")" "$(json_escape "$file_surface")"
        printf ',\"coverage\":{\"scope\":\"deterministic physical X11 smoke baseline\",\"exhaustive\":false,\"exhaustiveFreeXRunner\":\"tools/Run-FreeXLinuxInteractionValidation.ps1\"}'
        printf ',\"contractValidation\":{\"status\":\"pending\",\"validator\":\"tools/Run-FamilyLinuxInteractionValidation.ps1\",\"contractReference\":\"tools/LinuxInteractiveDocker/family-x11-validation.schema.json\"}'
        printf ',\"screenshots\":['
        for screenshot in "${screenshots[@]}"; do
            if $screenshot_first; then screenshot_first=false; else printf ','; fi
            printf '%s' "$screenshot"
        done
        printf ']'
        printf ',\"summary\":{\"passed\":%d,\"failed\":%d,\"total\":%d}' \
            "$passed" "$failed" "$((passed + failed))"
        printf ',\"results\":['
        for result in "${results[@]}"; do
            if $first; then first=false; else printf ','; fi
            printf '%s' "$result"
        done
        printf ']}\n'
    } > "$output/family-x11-results.json"
    manifest_written=true
}

on_error() {
    local exit_code=$?
    trap - ERR
    xdotool mouseup 1 >/dev/null 2>&1 || true
    if ! $manifest_written; then
        local runtime_evidence="$output/probe-runtime-error.txt"
        printf 'Probe aborted unexpectedly at line %s (exit %s).\n' "${BASH_LINENO[0]}" "$exit_code" > "$runtime_evidence"
        record "probe-runtime" "failed" "probe-runtime-error.txt" \
            "Probe aborted unexpectedly at line ${BASH_LINENO[0]} (exit $exit_code)."
        write_manifest
    fi
    exit "$exit_code"
}
trap on_error ERR

mapfile -t visible_windows < <(xdotool search --onlyvisible --name "$window_pattern" 2>/dev/null || true)
if (( ${#visible_windows[@]} == 0 )); then
    discovery_evidence="$output/window-discovery-error.txt"
    printf 'No visible window matched %s.\n' "$window_pattern" > "$discovery_evidence"
    record "visible-window-discovery" "failed" "window-discovery-error.txt" \
        "No visible window matched '$window_pattern'."
    write_manifest
    exit 2
fi
window_id="${visible_windows[${#visible_windows[@]}-1]}"
window_title="$(xdotool getwindowname "$window_id" 2>/dev/null || printf '%s' "$app")"
capture "baseline.png"
record "visible-window-discovery" "passed" "baseline.png" \
    "Discovered visible $app window $window_id ('$window_title')."

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

send_active_key() {
    xdotool key --clearmodifiers --delay "$input_delay_ms" "$@"
    sleep "$settle_seconds"
}

screen_changed() {
    local before="$1" after="$2" minimum="${3:-100}" changed
    changed="$(compare -metric AE "$before" "$after" null: 2>&1 || true)"
    [[ "$changed" =~ ^[0-9]+ ]] && (( ${BASH_REMATCH[0]} >= minimum ))
}

active_window_is_owner() {
    [[ "$(xdotool getactivewindow 2>/dev/null || true)" == "$window_id" ]]
}

run_keytip_cycle() {
    local id_prefix="$1" key="$2"
    local before="${id_prefix}-before.png" visible="${id_prefix}-visible.png" dismissed="${id_prefix}-dismissed.png"

    capture "$before"
    if [[ "$key" == "F10" ]]; then
        send_active_key "$key"
    else
        send_key "$key"
    fi
    capture "$visible"
    if screen_changed "$output/$before" "$output/$visible" 100; then
        record "${id_prefix}-appearance" "passed" "$visible" "Standalone $key exposed ribbon key tips."
    else
        record "${id_prefix}-appearance" "failed" "$visible" "Standalone $key did not produce a visible key-tip transition."
    fi

    send_active_key Escape
    capture "$dismissed"
    if screen_changed "$output/$visible" "$output/$dismissed" 100; then
        record "${id_prefix}-dismissal" "passed" "$dismissed" "Escape dismissed the $key key-tip state."
    else
        record "${id_prefix}-dismissal" "failed" "$dismissed" "Escape did not visibly dismiss the $key key-tip state."
    fi
}

run_keytip_cycle "alt-keytips" Alt_L
run_keytip_cycle "f10-keytips" F10

capture "tab-before.png"
send_key Alt_L
send_key "$tab_key"
capture "tab-switched.png"
if screen_changed "$output/tab-before.png" "$output/tab-switched.png" 100; then
    record "ribbon-tab-keytip-switch" "passed" "tab-switched.png" \
        "Alt followed by key tip '$tab_key' changed the rendered ribbon state."
else
    record "ribbon-tab-keytip-switch" "failed" "tab-switched.png" \
        "Alt followed by key tip '$tab_key' did not change the rendered ribbon state."
fi
send_key Escape

window_count() {
    mapfile -t all_windows < <(wmctrl -l 2>/dev/null || true)
    printf '%d' "${#all_windows[@]}"
}

baseline_window_count="$(window_count)"
capture "file-before.png"
send_key Alt_L
send_key "$file_key"
capture "file-open.png"

if [[ "$file_surface" == "top-level-backstage-window" ]]; then
    active_after_file="$(xdotool getactivewindow 2>/dev/null || true)"
    open_window_count="$(window_count)"
    if [[ "$active_after_file" != "$window_id" ]] &&
       (( open_window_count > baseline_window_count )) &&
       screen_changed "$output/file-before.png" "$output/file-open.png" 200; then
        record "file-surface-open" "passed" "file-open.png" \
            "File key tip opened a separate top-level backstage window ($active_after_file)."
    else
        record "file-surface-open" "failed" "file-open.png" \
            "Configured top-level backstage route did not produce a new active window."
    fi

    send_active_key Escape
    focus_app
    capture "file-dismissed.png"
    dismissed_window_count="$(window_count)"
    if active_window_is_owner &&
       (( dismissed_window_count == baseline_window_count )) &&
       screen_changed "$output/file-open.png" "$output/file-dismissed.png" 200; then
        record "file-surface-dismissal" "passed" "file-dismissed.png" \
            "Escape dismissed the top-level backstage window and returned focus to the app."
    else
        record "file-surface-dismissal" "failed" "file-dismissed.png" \
            "Top-level backstage did not dismiss, restore focus, or restore the window count."
    fi
elif [[ "$file_surface" == "in-window-backstage-overlay" ]]; then
    active_after_file="$(xdotool getactivewindow 2>/dev/null || true)"
    open_window_count="$(window_count)"
    if [[ "$active_after_file" == "$window_id" ]] &&
       (( open_window_count == baseline_window_count )) &&
       screen_changed "$output/file-before.png" "$output/file-open.png" 200; then
        record "file-surface-open" "passed" "file-open.png" \
            "File key tip opened the in-window backstage overlay while retaining the owner window."
    else
        record "file-surface-open" "failed" "file-open.png" \
            "Configured in-window backstage route did not retain the owner window and change the rendered state."
    fi

    send_active_key Escape
    focus_app
    capture "file-dismissed.png"
    dismissed_window_count="$(window_count)"
    if active_window_is_owner &&
       (( dismissed_window_count == baseline_window_count )) &&
       screen_changed "$output/file-open.png" "$output/file-dismissed.png" 200; then
        record "file-surface-dismissal" "passed" "file-dismissed.png" \
            "Escape dismissed the in-window backstage overlay and restored the owner state."
    else
        record "file-surface-dismissal" "failed" "file-dismissed.png" \
            "In-window backstage did not dismiss, restore focus, or preserve the window count."
    fi
else
    invalid_surface_evidence="$output/file-surface-configuration.txt"
    printf 'Unsupported file surface: %s.\n' "$file_surface" > "$invalid_surface_evidence"
    record "file-surface-open" "failed" "file-surface-configuration.txt" \
        "The probe cannot validate an unknown File surface."
    record "file-surface-dismissal" "failed" "file-surface-configuration.txt" \
        "The probe cannot validate an unknown File surface."
fi

write_manifest
failed_count=0
for result in "${results[@]}"; do
    if [[ "$result" == *'"status":"failed"'* ]]; then
        ((failed_count += 1))
    fi
done

if (( failed_count > 0 )); then
    exit 3
fi
