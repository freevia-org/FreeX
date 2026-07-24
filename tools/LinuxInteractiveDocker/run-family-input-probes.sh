#!/usr/bin/env bash
set -Eeuo pipefail

export DISPLAY="${DISPLAY:-:99}"

output="${1:-/work/family-validation}"
input_delay_ms="${FAMILY_X11_INPUT_DELAY_MS:-160}"
settle_seconds="${FAMILY_X11_SETTLE_SECONDS:-0.45}"
pointer_timeout_seconds="${FAMILY_X11_POINTER_TIMEOUT_SECONDS:-3}"
clipboard_timeout_seconds="${FAMILY_X11_CLIPBOARD_TIMEOUT_SECONDS:-3}"
app="${FAMILY_APP:?FAMILY_APP is required (FreeW or FreeP)}"
window_pattern="${FAMILY_WINDOW_PATTERN:?FAMILY_WINDOW_PATTERN is required}"
tab_key="${FAMILY_TAB_KEY:?FAMILY_TAB_KEY is required}"
file_key="${FAMILY_FILE_KEY:-F}"
file_surface="${FAMILY_FILE_SURFACE:?FAMILY_FILE_SURFACE is required}"

mkdir -p "$output"

declare -a results=()
declare -a screenshots=()
required_ids=(
    "visible-window-discovery"
    "alt-keytips-appearance"
    "alt-keytips-dismissal"
    "f10-keytips-appearance"
    "f10-keytips-dismissal"
    "ribbon-tab-keytip-switch"
    "file-surface-open"
    "file-surface-dismissal"
)
if [[ "$app" == "FreeW" ]]; then
    required_ids+=(
        "editor-sentinel-copy"
        "editor-undo-restores-clipboard"
        "editor-redo-restores-clipboard"
        "editor-cut-undo-restores"
        "editor-paste-text-only"
        "editor-find-open"
        "editor-find-dismissal"
        "editor-replace-open"
        "editor-replace-dismissal"
        "editor-reveal-formatting-open"
        "editor-reveal-formatting-dismissal"
        "editor-thesaurus-open"
        "editor-thesaurus-dismissal"
        "editor-keyboard-context-open"
        "editor-keyboard-context-dismissal"
        "editor-pointer-context-open"
        "editor-pointer-context-dismissal"
    )
else
    required_ids+=(
        "slide-pane-new-slide-create"
        "slide-pane-new-slide-undo"
        "slide-pane-new-slide-redo"
        "slide-pane-keyboard-context-open"
        "slide-pane-keyboard-context-dismissal"
        "slide-pane-pointer-context-open"
        "slide-pane-pointer-context-dismissal"
        "slide-pane-pointer-select-second"
        "slide-pane-keyboard-up-first"
        "slide-pane-duplicate-create"
        "slide-pane-duplicate-undo"
        "slide-pane-duplicate-redo"
        "slide-pane-delete-selected"
        "slide-pane-delete-undo"
    )
fi
manifest_written=false
clipboard_owner_pid=""
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

has_result() {
    local wanted_id="$1" result
    for result in "${results[@]}"; do
        if [[ "$result" == *"\"id\":\"$(json_escape "$wanted_id")\""* ]]; then
            return 0
        fi
    done
    return 1
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
    printf 'Probe command failed at line %s (exit %s).\n' "${BASH_LINENO[0]}" "$exit_code" > "$output/probe-runtime-error.txt"
    return "$exit_code"
}

on_exit() {
    local exit_code=$?
    trap - ERR EXIT
    if [[ -n "$clipboard_owner_pid" ]]; then
        stop_clipboard_owner
    fi
    timeout --foreground --kill-after=1s "$pointer_timeout_seconds" xdotool mouseup 1 >/dev/null 2>&1 || true
    timeout --foreground --kill-after=1s "$pointer_timeout_seconds" xdotool mouseup 3 >/dev/null 2>&1 || true
    if [[ "$exit_code" -ne 0 ]]; then
        local runtime_evidence="$output/probe-runtime-error.txt"
        if [[ ! -s "$runtime_evidence" ]]; then
            printf 'Probe exited unexpectedly (exit %s).\n' "$exit_code" > "$runtime_evidence"
        fi
        local required_id
        for required_id in "${required_ids[@]}"; do
            if ! has_result "$required_id"; then
                record "$required_id" "failed" "probe-runtime-error.txt" \
                    "Probe exited before collecting this required row (exit $exit_code)."
            fi
        done
        if (( ${#screenshots[@]} == 0 )); then
            if timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
                scrot -o "$output/probe-failure.png" >/dev/null 2>&1; then
                track_screenshot "probe-failure.png"
            fi
        fi
        write_manifest
    fi
    exit "$exit_code"
}
trap on_error ERR
trap on_exit EXIT

move_pointer() {
    local x="$1" y="$2"
    timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
        xdotool mousemove "$x" "$y"
    sleep 0.08
}

click_pointer() {
    local button="$1" x="$2" y="$3"
    move_pointer "$x" "$y"
    timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
        xdotool click --clearmodifiers "$button"
    sleep "$settle_seconds"
}

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
    timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
        xdotool windowactivate --sync "$window_id" 2>/dev/null || true
    timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
        xdotool windowfocus "$window_id" 2>/dev/null || true
    sleep 0.12
}

send_key() {
    focus_app
    timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
        xdotool key --clearmodifiers --delay "$input_delay_ms" --window "$window_id" "$@"
    sleep "$settle_seconds"
}

send_active_key() {
    timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
        xdotool key --clearmodifiers --delay "$input_delay_ms" "$@"
    sleep "$settle_seconds"
}

send_active_text() {
    local text_value="$1" active_id
    active_id="$(xdotool getactivewindow 2>/dev/null || true)"
    if [[ -z "$active_id" ]]; then
        return 1
    fi
    timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
        xdotool type --clearmodifiers --delay "$input_delay_ms" --window "$active_id" "$text_value"
    sleep "$settle_seconds"
}

send_editor_key() {
    timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
        xdotool key --clearmodifiers --delay "$input_delay_ms" --window "$window_id" "$@"
    sleep "$settle_seconds"
}

capture_window_state() {
    local name="$1"
    {
        printf 'active-window=%s\n' "$(xdotool getactivewindow 2>/dev/null || true)"
        printf 'focus-window=%s\n' "$(xdotool getwindowfocus 2>/dev/null || true)"
        wmctrl -l 2>/dev/null || true
    } > "$output/$name"
}

read_clipboard_bounded() {
    local destination="$1" error_destination="$2"
    timeout --foreground --kill-after=1s "$clipboard_timeout_seconds" \
        xclip -selection clipboard -o > "$destination" 2> "$error_destination"
}

start_clipboard_owner() {
    local source="$1" error_destination="$2"
    local before after launcher_pid candidate
    before="$(pgrep -x xclip 2>/dev/null || true)"
    xclip -silent -selection clipboard -in < "$source" > /dev/null 2> "$error_destination" &
    launcher_pid=$!
    sleep 0.12
    after="$(pgrep -x xclip 2>/dev/null || true)"
    clipboard_owner_pid=""
    for candidate in $after; do
        if ! printf '%s\n' "$before" | grep -Fxq "$candidate"; then
            clipboard_owner_pid="$candidate"
            break
        fi
    done
    wait "$launcher_pid" >/dev/null 2>&1 || true
    if [[ -z "$clipboard_owner_pid" ]] || ! kill -0 "$clipboard_owner_pid" >/dev/null 2>&1; then
        clipboard_owner_pid=""
        return 1
    fi
}

stop_clipboard_owner() {
    if [[ -z "$clipboard_owner_pid" ]]; then
        return 0
    fi
    local pid="$clipboard_owner_pid"
    local deadline=$((SECONDS + clipboard_timeout_seconds))
    while kill -0 "$pid" >/dev/null 2>&1 && (( SECONDS < deadline )); do
        sleep 0.08
    done
    if kill -0 "$pid" >/dev/null 2>&1; then
        kill "$pid" >/dev/null 2>&1 || true
    fi
    wait "$pid" >/dev/null 2>&1 || true
    clipboard_owner_pid=""
}

screen_matches() {
    local before="$1" after="$2" maximum="${3:-200}" changed
    changed="$(compare -metric AE "$before" "$after" null: 2>&1 || true)"
    [[ "$changed" =~ ^[0-9]+ ]] && (( ${BASH_REMATCH[0]} <= maximum ))
}

screen_changed() {
    local before="$1" after="$2" minimum="${3:-100}" changed
    changed="$(compare -metric AE "$before" "$after" null: 2>&1 || true)"
    [[ "$changed" =~ ^[0-9]+ ]] && (( ${BASH_REMATCH[0]} >= minimum ))
}

capture_region() {
    local source_name="$1" name="$2" geometry="$3"
    convert "$output/$source_name" -crop "$geometry" +repage "$output/$name"
    track_screenshot "$name"
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

# FreeW-only physical editing evidence. FreeP deliberately retains its exact
# twenty-two-row family contract; these rows exercise the real FreeW DocumentView,
# dialog, pane, clipboard, and context-menu paths without pretending the suite is
# exhaustive.
if [[ "$app" == "FreeW" ]]; then
    sentinel="FreeW-physical-editor-sentinel-r2"
    geometry="$(xdotool getwindowgeometry --shell "$window_id" 2>/dev/null || true)"
    eval "$geometry"
    editor_x=$((X + WIDTH / 2))
    editor_y=$((Y + 350))

    baseline_clipboard="$output/editor-baseline-clipboard.txt"
    sentinel_clipboard="$output/editor-sentinel-clipboard.txt"
    undo_clipboard="$output/editor-undo-clipboard.txt"
    redo_clipboard="$output/editor-redo-clipboard.txt"
    expected_sentinel="$output/editor-expected-sentinel.txt"
    printf '%s' "$sentinel" > "$expected_sentinel"

    click_pointer 1 "$editor_x" "$editor_y"
    capture "editor-focused.png"
    send_editor_key ctrl+a
    send_editor_key ctrl+c
    if read_clipboard_bounded "$baseline_clipboard" "$output/editor-baseline-clipboard-error.txt"; then
        baseline_clipboard_ready=true
    else
        baseline_clipboard_ready=false
    fi

    atomic_paste_ready=false
    if start_clipboard_owner "$expected_sentinel" "$output/editor-clipboard-owner-error.txt"; then
        if send_editor_key ctrl+v; then
            atomic_paste_ready=true
        fi
        stop_clipboard_owner
    fi
    sleep "$settle_seconds"
    capture "editor-sentinel-typed.png"
    send_editor_key ctrl+a
    send_editor_key ctrl+c
    if read_clipboard_bounded "$sentinel_clipboard" "$output/editor-sentinel-clipboard-error.txt"; then
        sentinel_clipboard_ready=true
    else
        sentinel_clipboard_ready=false
    fi
    sentinel_proof="$output/editor-sentinel-copy-proof.txt"
    {
        printf 'expected=%s\n' "$sentinel"
        printf 'atomic-paste=%s\n' "$atomic_paste_ready"
        printf 'observed='; if $sentinel_clipboard_ready; then cat "$sentinel_clipboard"; fi; printf '\n'
        if $sentinel_clipboard_ready && cmp -s "$expected_sentinel" "$sentinel_clipboard"; then
            printf 'exact-match=true\n'
        else
            printf 'exact-match=false\n'
        fi
    } > "$sentinel_proof"
    if $atomic_paste_ready && $sentinel_clipboard_ready && cmp -s "$expected_sentinel" "$sentinel_clipboard"; then
        record "editor-sentinel-copy" "passed" "editor-sentinel-copy-proof.txt" \
            "Clicked the real FreeW editor, typed the sentinel, selected all, copied it, and matched the exact X11 clipboard text."
    else
        record "editor-sentinel-copy" "failed" "editor-sentinel-copy-proof.txt" \
            "The real editor sentinel was not reproduced exactly on the X11 clipboard."
    fi

    send_editor_key ctrl+z
    capture "editor-after-undo.png"
    send_editor_key ctrl+a
    send_editor_key ctrl+c
    if read_clipboard_bounded "$undo_clipboard" "$output/editor-undo-clipboard-error.txt"; then
        undo_clipboard_ready=true
    else
        undo_clipboard_ready=false
    fi
    undo_proof="$output/editor-undo-restore-proof.txt"
    {
        printf 'expected-clipboard=baseline-editor-selection\n'
        printf 'baseline-sha256='; if $baseline_clipboard_ready; then sha256sum "$baseline_clipboard" | cut -d ' ' -f1; fi; printf '\n'
        printf 'undo-sha256='; if $undo_clipboard_ready; then sha256sum "$undo_clipboard" | cut -d ' ' -f1; fi; printf '\n'
        if $baseline_clipboard_ready && $undo_clipboard_ready && cmp -s "$baseline_clipboard" "$undo_clipboard"; then
            printf 'exact-match=true\n'
        else
            printf 'exact-match=false\n'
        fi
    } > "$undo_proof"
    if $baseline_clipboard_ready && $undo_clipboard_ready && cmp -s "$baseline_clipboard" "$undo_clipboard"; then
        record "editor-undo-restores-clipboard" "passed" "editor-undo-restore-proof.txt" \
            "Ctrl+Z restored the exact pre-edit document selection and clipboard state."
    else
        record "editor-undo-restores-clipboard" "failed" "editor-undo-restore-proof.txt" \
            "Ctrl+Z did not restore the exact pre-edit clipboard state."
    fi

    send_editor_key ctrl+y
    capture "editor-after-redo.png"
    send_editor_key ctrl+a
    send_editor_key ctrl+c
    if read_clipboard_bounded "$redo_clipboard" "$output/editor-redo-clipboard-error.txt"; then
        redo_clipboard_ready=true
    else
        redo_clipboard_ready=false
    fi
    redo_proof="$output/editor-redo-restore-proof.txt"
    {
        printf 'expected=%s\n' "$sentinel"
        printf 'observed='; if $redo_clipboard_ready; then cat "$redo_clipboard"; fi; printf '\n'
        if $redo_clipboard_ready && cmp -s "$expected_sentinel" "$redo_clipboard"; then
            printf 'exact-match=true\n'
        else
            printf 'exact-match=false\n'
        fi
    } > "$redo_proof"
    if $redo_clipboard_ready && cmp -s "$expected_sentinel" "$redo_clipboard"; then
        record "editor-redo-restores-clipboard" "passed" "editor-redo-restore-proof.txt" \
            "Ctrl+Y restored the exact sentinel document state and clipboard text."
    else
        record "editor-redo-restores-clipboard" "failed" "editor-redo-restore-proof.txt" \
            "Ctrl+Y did not restore the exact sentinel clipboard state."
    fi

    cut_clipboard="$output/editor-cut-clipboard.txt"
    cut_restore_clipboard="$output/editor-cut-restore-clipboard.txt"
    cut_expected="$output/editor-cut-expected.txt"
    cut_proof="$output/editor-cut-undo-restore-proof.txt"
    printf '%s' "$sentinel" > "$cut_expected"
    send_editor_key ctrl+a
    capture "editor-before-cut.png"
    editor_proof_top=$((Y + HEIGHT * 18 / 100))
    editor_proof_height=$((HEIGHT * 55 / 100))
    editor_proof_geometry="${WIDTH}x${editor_proof_height}+${X}+${editor_proof_top}"
    capture_region "editor-before-cut.png" "editor-before-cut-document.png" "$editor_proof_geometry"
    cut_sent=false
    if send_editor_key ctrl+x; then
        cut_sent=true
    fi
    sleep "$settle_seconds"
    capture "editor-after-cut.png"
    capture_region "editor-after-cut.png" "editor-after-cut-document.png" "$editor_proof_geometry"
    if read_clipboard_bounded "$cut_clipboard" "$output/editor-cut-clipboard-error.txt"; then
        cut_clipboard_ready=true
    else
        cut_clipboard_ready=false
    fi
    send_editor_key ctrl+z
    send_editor_key ctrl+a
    capture "editor-after-cut-undo.png"
    capture_region "editor-after-cut-undo.png" "editor-after-cut-undo-document.png" "$editor_proof_geometry"
    send_editor_key ctrl+c
    if read_clipboard_bounded "$cut_restore_clipboard" "$output/editor-cut-restore-clipboard-error.txt"; then
        cut_restore_ready=true
    else
        cut_restore_ready=false
    fi
    {
        printf 'cut-sent=%s\n' "$cut_sent"
        printf 'document-proof-geometry=%s\n' "$editor_proof_geometry"
        printf 'before-cut-document=editor-before-cut-document.png\n'
        printf 'after-cut-document=editor-after-cut-document.png\n'
        printf 'after-cut-undo-document=editor-after-cut-undo-document.png\n'
        printf 'expected=%s\n' "$sentinel"
        printf 'cut-observed='; if $cut_clipboard_ready; then cat "$cut_clipboard"; fi; printf '\n'
        printf 'restored-observed='; if $cut_restore_ready; then cat "$cut_restore_clipboard"; fi; printf '\n'
        printf 'cut-exact-match='; if $cut_clipboard_ready && cmp -s "$cut_expected" "$cut_clipboard"; then printf 'true\n'; else printf 'false\n'; fi
        printf 'restore-exact-match='; if $cut_restore_ready && cmp -s "$cut_expected" "$cut_restore_clipboard"; then printf 'true\n'; else printf 'false\n'; fi
        printf 'cut-document-transition='; if screen_changed "$output/editor-before-cut-document.png" "$output/editor-after-cut-document.png" 100; then printf 'true\n'; else printf 'false\n'; fi
        printf 'undo-document-transition='; if screen_changed "$output/editor-after-cut-document.png" "$output/editor-after-cut-undo-document.png" 100; then printf 'true\n'; else printf 'false\n'; fi
        printf 'undo-document-restored='; if screen_matches "$output/editor-before-cut-document.png" "$output/editor-after-cut-undo-document.png" 500; then printf 'true\n'; else printf 'false\n'; fi
    } > "$cut_proof"
    cut_document_transition=false
    cut_undo_transition=false
    cut_document_restored=false
    if screen_changed "$output/editor-before-cut-document.png" "$output/editor-after-cut-document.png" 100; then
        cut_document_transition=true
    fi
    if screen_changed "$output/editor-after-cut-document.png" "$output/editor-after-cut-undo-document.png" 100; then
        cut_undo_transition=true
    fi
    if screen_matches "$output/editor-before-cut-document.png" "$output/editor-after-cut-undo-document.png" 500; then
        cut_document_restored=true
    fi
    if $cut_sent && $cut_clipboard_ready && $cut_restore_ready &&
       cmp -s "$cut_expected" "$cut_clipboard" && cmp -s "$cut_expected" "$cut_restore_clipboard" &&
       $cut_document_transition && $cut_undo_transition && $cut_document_restored; then
        record "editor-cut-undo-restores" "passed" "editor-cut-undo-restore-proof.txt" \
            "Ctrl+X copied the exact selected text to the X11 clipboard and Ctrl+Z restored the exact selection content."
    else
        record "editor-cut-undo-restores" "failed" "editor-cut-undo-restore-proof.txt" \
            "The physical Ctrl+X plus Ctrl+Z sequence did not preserve exact clipboard and restored text evidence."
    fi

    paste_text_only="$output/editor-paste-text-only-source.txt"
    paste_text_only_result="$output/editor-paste-text-only-result.txt"
    paste_text_only_restore="$output/editor-paste-text-only-restore.txt"
    paste_text_only_expected="$output/editor-paste-text-only-expected.txt"
    paste_text_only_proof="$output/editor-paste-text-only-proof.txt"
    paste_text_only_value="FreeW-physical-paste-text-only-r2"
    printf '%s' "$paste_text_only_value" > "$paste_text_only"
    printf '%s' "$paste_text_only_value" > "$paste_text_only_expected"
    paste_text_only_sent=false
    paste_text_only_owner=false
    send_editor_key ctrl+a
    if start_clipboard_owner "$paste_text_only" "$output/editor-paste-text-only-owner-error.txt"; then
        paste_text_only_owner=true
        if send_editor_key ctrl+shift+v; then
            paste_text_only_sent=true
        fi
        stop_clipboard_owner
    fi
    capture "editor-after-paste-text-only.png"
    send_editor_key ctrl+a
    send_editor_key ctrl+c
    if read_clipboard_bounded "$paste_text_only_result" "$output/editor-paste-text-only-result-error.txt"; then
        paste_text_only_result_ready=true
    else
        paste_text_only_result_ready=false
    fi
    send_editor_key ctrl+z
    capture "editor-after-paste-text-only-undo.png"
    send_editor_key ctrl+a
    send_editor_key ctrl+c
    if read_clipboard_bounded "$paste_text_only_restore" "$output/editor-paste-text-only-restore-error.txt"; then
        paste_text_only_restore_ready=true
    else
        paste_text_only_restore_ready=false
    fi
    {
        printf 'clipboard-owner-started=%s\n' "$paste_text_only_owner"
        printf 'ctrl-shift-v-sent=%s\n' "$paste_text_only_sent"
        printf 'expected=%s\n' "$paste_text_only_value"
        printf 'result-observed='; if $paste_text_only_result_ready; then cat "$paste_text_only_result"; fi; printf '\n'
        printf 'restore-observed='; if $paste_text_only_restore_ready; then cat "$paste_text_only_restore"; fi; printf '\n'
        printf 'result-exact-match='; if $paste_text_only_result_ready && cmp -s "$paste_text_only_expected" "$paste_text_only_result"; then printf 'true\n'; else printf 'false\n'; fi
        printf 'restore-exact-sentinel='; if $paste_text_only_restore_ready && cmp -s "$cut_expected" "$paste_text_only_restore"; then printf 'true\n'; else printf 'false\n'; fi
        printf 'semantic-distinction=plain-text-only-X11-clipboard-cannot-prove-rich-format-stripping\n'
    } > "$paste_text_only_proof"
    if $paste_text_only_owner && $paste_text_only_sent && $paste_text_only_result_ready &&
       $paste_text_only_restore_ready && cmp -s "$paste_text_only_expected" "$paste_text_only_result" &&
       cmp -s "$cut_expected" "$paste_text_only_restore"; then
        record "editor-paste-text-only" "passed" "editor-paste-text-only-proof.txt" \
            "Ctrl+Shift+V consumed an exact plain-text X11 clipboard and undo restored the sentinel; rich-format stripping is intentionally not claimed."
    else
        record "editor-paste-text-only" "failed" "editor-paste-text-only-proof.txt" \
            "The physical Ctrl+Shift+V route did not produce exact plain-text result and restore evidence."
    fi

    run_find_replace_route() {
        local id_prefix="$1" key="$2" marker="$3" route_label="$4"
        local before="${id_prefix}-before.png" open="${id_prefix}-open.png"
        local typed="${id_prefix}-typed.png" entered="${id_prefix}-entered.png"
        local dismissed="${id_prefix}-dismissed.png"
        local before_state="${id_prefix}-before-state.txt" open_state="${id_prefix}-open-state.txt"
        local dismissed_state="${id_prefix}-dismissed-state.txt"
        local expected="${id_prefix}-expected.txt" clipboard="${id_prefix}-clipboard.txt"
        local proof="${id_prefix}-proof.txt"
        local trigger_ready=true typed_ready=false clipboard_ready=false entry_ready=false
        local baseline_count open_count dismissed_count active_window_id dialog_window
        printf '%s' "$marker" > "$output/$expected"

        focus_app
        click_pointer 1 "$editor_x" "$editor_y"
        baseline_count="$(window_count)"
        capture "$before"
        capture_window_state "$before_state"
        if ! send_editor_key "$key"; then
            trigger_ready=false
        fi
        capture "$open"
        capture_window_state "$open_state"
        active_window_id="$(xdotool getactivewindow 2>/dev/null || true)"
        dialog_window="$(xdotool search --onlyvisible --name 'Find & Replace' 2>/dev/null | while read -r candidate; do
            if [[ "$candidate" != "$window_id" ]]; then
                printf '%s\n' "$candidate"
            fi
        done | tail -n 1)"
        open_count="$(window_count)"

        if send_active_text "$marker"; then
            typed_ready=true
        fi
        capture "$typed"
        send_active_key ctrl+a || true
        send_active_key ctrl+c || true
        if read_clipboard_bounded "$output/$clipboard" "$output/${id_prefix}-clipboard-error.txt"; then
            clipboard_ready=true
        fi

        if [[ "$route_label" == "Find" ]]; then
            send_active_key Return || true
        else
            send_active_key shift+Tab || true
            send_active_text "${marker}-find" || true
            send_active_key Return || true
        fi
        capture "$entered"
        if screen_changed "$output/$typed" "$output/$entered" 40; then
            entry_ready=true
        fi

        {
            printf 'route=%s\n' "$route_label"
            printf 'shortcut=%s\n' "$key"
            printf 'marker=%s\n' "$marker"
            printf 'before-screenshot=%s\n' "$before"
            printf 'open-screenshot=%s\n' "$open"
            printf 'typed-screenshot=%s\n' "$typed"
            printf 'entered-screenshot=%s\n' "$entered"
            printf 'active-window=%s\n' "$active_window_id"
            printf 'find-replace-window=%s\n' "$dialog_window"
            printf 'baseline-window-count=%s\n' "$baseline_count"
            printf 'open-window-count=%s\n' "$open_count"
            printf 'trigger-ready=%s\n' "$trigger_ready"
            printf 'typed-ready=%s\n' "$typed_ready"
            printf 'clipboard-ready=%s\n' "$clipboard_ready"
            printf 'clipboard-exact='; if $clipboard_ready && cmp -s "$output/$expected" "$output/$clipboard"; then printf 'true\n'; else printf 'false\n'; fi
            printf 'route-entry-transition=%s\n' "$entry_ready"
            printf 'separate-window='; if [[ -n "$dialog_window" && "$dialog_window" != "$window_id" && "$open_count" -gt "$baseline_count" ]]; then printf 'true\n'; else printf 'false\n'; fi
            printf 'open-screenshot-changed='; if screen_changed "$output/$before" "$output/$open" 200; then printf 'true\n'; else printf 'false\n'; fi
        } > "$output/$proof"
        if $trigger_ready && $typed_ready && $clipboard_ready && $entry_ready &&
           cmp -s "$output/$expected" "$output/$clipboard" &&
           [[ -n "$dialog_window" && "$dialog_window" != "$window_id" ]] &&
           (( open_count > baseline_count )) && screen_changed "$output/$before" "$output/$open" 200; then
            record "${id_prefix}-open" "passed" "$proof" \
                "$route_label shortcut opened the real Find & Replace window, typed an exact route marker into its initial field, and produced route-specific Enter evidence."
        else
            record "${id_prefix}-open" "failed" "$proof" \
                "$route_label shortcut did not produce a separately evidenced initial-field route."
        fi

        send_active_key Escape || true
        focus_app
        capture "$dismissed"
        capture_window_state "$dismissed_state"
        dismissed_count="$(window_count)"
        {
            printf 'before-screenshot=%s\n' "$before"
            printf 'open-screenshot=%s\n' "$open"
            printf 'dismissed-screenshot=%s\n' "$dismissed"
            printf 'before-state=%s\n' "$before_state"
            printf 'open-state=%s\n' "$open_state"
            printf 'dismissed-state=%s\n' "$dismissed_state"
            printf 'baseline-window-count=%s\n' "$baseline_count"
            printf 'dismissed-window-count=%s\n' "$dismissed_count"
            printf 'returns-to-owner='; if active_window_is_owner && [[ "$dismissed_count" -eq "$baseline_count" ]]; then printf 'true\n'; else printf 'false\n'; fi
            printf 'dismissed-returns-to-before='; if screen_matches "$output/$before" "$output/$dismissed" 200; then printf 'true\n'; else printf 'false\n'; fi
        } >> "$output/$proof"
        if active_window_is_owner && [[ "$dismissed_count" -eq "$baseline_count" ]] &&
           screen_matches "$output/$before" "$output/$dismissed" 200; then
            record "${id_prefix}-dismissal" "passed" "$proof" \
                "Escape dismissed the $route_label Find & Replace route and restored the original FreeW owner window."
        else
            record "${id_prefix}-dismissal" "failed" "$proof" \
                "Escape did not restore the original FreeW owner window after the $route_label route."
        fi
    }

    run_side_pane_toggle_probe() {
        local id_prefix="$1" key="$2" label="$3"
        local before="${id_prefix}-before.png" open="${id_prefix}-open.png" dismissed="${id_prefix}-dismissed.png"
        local before_pane="${id_prefix}-before-pane.png" open_pane="${id_prefix}-open-pane.png" dismissed_pane="${id_prefix}-dismissed-pane.png"
        local proof="${id_prefix}-proof.txt" before_state="${id_prefix}-before-state.txt" open_state="${id_prefix}-open-state.txt"
        local dismissed_state="${id_prefix}-dismissed-state.txt" trigger_ready=true open_ready=false dismissed_ready=false

        focus_app
        click_pointer 1 "$editor_x" "$editor_y"
        capture "$before"
        capture_region "$before" "$before_pane" "$pane_geometry"
        capture_window_state "$before_state"
        if ! send_editor_key "$key"; then
            trigger_ready=false
        fi
        capture "$open"
        capture_region "$open" "$open_pane" "$pane_geometry"
        capture_window_state "$open_state"
        if screen_changed "$output/$before_pane" "$output/$open_pane" 100; then
            open_ready=true
        fi

        send_editor_key "$key" || true
        capture "$dismissed"
        capture_region "$dismissed" "$dismissed_pane" "$pane_geometry"
        capture_window_state "$dismissed_state"
        if screen_matches "$output/$before_pane" "$output/$dismissed_pane" 200; then
            dismissed_ready=true
        fi
        {
            printf 'label=%s\n' "$label"
            printf 'shortcut=%s\n' "$key"
            printf 'pane-geometry=%s\n' "$pane_geometry"
            printf 'before-pane=%s\n' "$before_pane"
            printf 'open-pane=%s\n' "$open_pane"
            printf 'dismissed-pane=%s\n' "$dismissed_pane"
            printf 'trigger-ready=%s\n' "$trigger_ready"
            printf 'open-pane-transition=%s\n' "$open_ready"
            printf 'dismissed-pane-restored=%s\n' "$dismissed_ready"
            printf 'before-state=%s\n' "$before_state"
            printf 'open-state=%s\n' "$open_state"
            printf 'dismissed-state=%s\n' "$dismissed_state"
        } > "$output/$proof"
        if $trigger_ready && $open_ready; then
            record "${id_prefix}-open" "passed" "$proof" \
                "$label shortcut opened the real right-side pane; the calibrated pane crop changed under physical X11 input."
        else
            record "${id_prefix}-open" "failed" "$proof" \
                "$label shortcut did not produce a gated right-side pane transition."
        fi
        if $trigger_ready && $dismissed_ready; then
            record "${id_prefix}-dismissal" "passed" "$proof" \
                "A second physical $label shortcut hid the pane and restored the calibrated pre-open crop."
        else
            record "${id_prefix}-dismissal" "failed" "$proof" \
                "A second physical $label shortcut did not restore the calibrated pre-open pane crop."
        fi
    }

    pane_width=260
    [[ "$WIDTH" -lt 900 ]] && pane_width=220
    pane_top=$((Y + 150))
    pane_height=$((HEIGHT - 280))
    [[ "$pane_height" -lt 180 ]] && pane_height=180
    pane_geometry="${pane_width}x${pane_height}+$((X + WIDTH - pane_width))+${pane_top}"

    run_find_replace_route "editor-find" ctrl+f "FreeW-physical-find-route-r2" Find
    run_find_replace_route "editor-replace" ctrl+h "FreeW-physical-replace-route-r2" Replace
    run_side_pane_toggle_probe "editor-reveal-formatting" shift+F1 "Reveal Formatting"
    run_side_pane_toggle_probe "editor-thesaurus" shift+F7 "Thesaurus"

    run_editor_context_probe() {
        local id_prefix="$1" trigger="$2" before="$3" open="$4" dismissed="$5"
        local before_state="${id_prefix}-before-state.txt" open_state="${id_prefix}-open-state.txt"
        local open_proof="${id_prefix}-open-proof.txt" dismissal_proof="${id_prefix}-dismissal-proof.txt"
        local trigger_ready=true
        if [[ "$trigger" == "pointer" ]]; then
            if ! click_pointer 1 "$editor_x" "$editor_y"; then
                trigger_ready=false
            fi
        fi
        capture "$before"
        capture_window_state "$before_state"
        if [[ "$trigger" == "keyboard" ]]; then
            if ! send_editor_key shift+F10; then
                trigger_ready=false
            fi
        else
            if ! click_pointer 3 "$editor_x" "$editor_y"; then
                trigger_ready=false
            fi
        fi
        capture "$open"
        capture_window_state "$open_state"
        {
            printf 'before-screenshot=%s\n' "$before"
            printf 'open-screenshot=%s\n' "$open"
            printf 'dismissed-screenshot=%s\n' "$dismissed"
            printf 'before-window-state=%s\n' "$before_state"
            printf 'open-window-state=%s\n' "$open_state"
            printf 'trigger-ready=%s\n' "$trigger_ready"
            printf 'open-state-changed='; if ! cmp -s "$output/$before_state" "$output/$open_state"; then printf 'true\n'; else printf 'false\n'; fi
            printf 'open-screenshot-changed='; if screen_changed "$output/$before" "$output/$open" 200; then printf 'true\n'; else printf 'false\n'; fi
        } > "$output/$open_proof"
        if $trigger_ready && screen_changed "$output/$before" "$output/$open" 200; then
            record "${id_prefix}-open" "passed" "$open_proof" \
                "The real FreeW editor context menu opened through $trigger input; before/open/dismissed screenshots and window-state evidence are retained."
        else
            record "${id_prefix}-open" "failed" "$open_proof" \
                "The real FreeW editor context menu did not produce a visible state transition."
        fi

        send_active_key Escape || true
        focus_app
        capture "$dismissed"
        capture_window_state "${id_prefix}-dismissed-state.txt"
        {
            printf 'before-screenshot=%s\n' "$before"
            printf 'open-screenshot=%s\n' "$open"
            printf 'dismissed-screenshot=%s\n' "$dismissed"
            printf 'before-window-state=%s\n' "$before_state"
            printf 'open-window-state=%s\n' "$open_state"
            printf 'dismissed-window-state=%s\n' "${id_prefix}-dismissed-state.txt"
            printf 'dismissed-returns-to-before='; if screen_matches "$output/$before" "$output/$dismissed" 200; then printf 'true\n'; else printf 'false\n'; fi
            printf 'popup-state-disappeared='; if ! cmp -s "$output/$open_state" "$output/${id_prefix}-dismissed-state.txt"; then printf 'true\n'; else printf 'false\n'; fi
        } > "$output/$dismissal_proof"
        if screen_matches "$output/$before" "$output/$dismissed" 200; then
            record "${id_prefix}-dismissal" "passed" "$dismissal_proof" \
                "Escape dismissed the real FreeW editor context menu and returned to the pre-open view; all three screenshots and window-state artifacts are retained."
        else
            record "${id_prefix}-dismissal" "failed" "$dismissal_proof" \
                "Escape did not return to the pre-open editor view; dismissal evidence is retained."
        fi
    }

    run_editor_context_probe "editor-keyboard-context" keyboard \
        "editor-keyboard-context-before.png" "editor-keyboard-context-open.png" "editor-keyboard-context-dismissed.png"
    run_editor_context_probe "editor-pointer-context" pointer \
        "editor-pointer-context-before.png" "editor-pointer-context-open.png" "editor-pointer-context-dismissed.png"
else
    # FreeP-only physical slide-pane evidence. Geometry is derived from the real
    # window bounds and the baseline screenshot, then retained in a calibration
    # artifact so the row cannot receive managed-only credit.
    geometry="$(xdotool getwindowgeometry --shell "$window_id" 2>/dev/null || true)"
    eval "$geometry"
    baseline_dimensions="$(identify -format '%wx%h' "$output/baseline.png" 2>/dev/null || true)"
    baseline_width="${baseline_dimensions%x*}"
    baseline_height="${baseline_dimensions#*x}"
    if [[ -z "$baseline_width" || -z "$baseline_height" || "$baseline_width" -le 0 || "$baseline_height" -le 0 ]]; then
        baseline_width="$WIDTH"
        baseline_height="$HEIGHT"
    fi
    slide_pane_width=$(( WIDTH * 14 / 100 ))
    [[ "$slide_pane_width" -gt 180 ]] && slide_pane_width=180
    [[ "$slide_pane_width" -lt 140 ]] && slide_pane_width=140
    slide_thumbnail_x=$(( X + slide_pane_width / 2 ))
    slide_thumbnail_y=$(( Y + HEIGHT * 34 / 100 ))
    second_slide_thumbnail_y=$(( slide_thumbnail_y + HEIGHT * 17 / 100 ))
    new_slide_x=$(( X + slide_pane_width / 2 ))
    # The baseline's button band ends above the status bar; keep the click
    # centered in that band rather than using the window's bottom edge.
    new_slide_y=$(( Y + HEIGHT - 66 ))
    main_view_x=$(( X + slide_pane_width + (WIDTH - slide_pane_width) / 2 ))
    main_view_y=$(( Y + HEIGHT * 55 / 100 ))
    # Exclude the bottom button, notes, and status bar from exact-state crops;
    # those controls legitimately change hover/focus chrome during keyboard input.
    slide_pane_stable_top=$(( Y + HEIGHT * 17 / 100 ))
    slide_pane_stable_height=$(( HEIGHT * 50 / 100 ))
    slide_pane_geometry="${slide_pane_width}x${slide_pane_stable_height}+${X}+${slide_pane_stable_top}"
    main_view_geometry="$((WIDTH - slide_pane_width))x$((HEIGHT * 68 / 100))+$((X + slide_pane_width))+$((Y + HEIGHT * 20 / 100))"
    status_height=26
    status_geometry="${baseline_width}x${status_height}+0+$((baseline_height - status_height))"
    {
        printf 'window-geometry=%s\n' "$geometry"
        printf 'baseline-dimensions=%s\n' "$baseline_dimensions"
        printf 'slide-pane-geometry=%s\n' "$slide_pane_geometry"
        printf 'slide-pane-stable-band=thumbnail-area-below-ribbon-above-button-and-status\n'
        printf 'main-view-geometry=%s\n' "$main_view_geometry"
        printf 'thumbnail-point=%s,%s\n' "$slide_thumbnail_x" "$slide_thumbnail_y"
        printf 'second-thumbnail-point=%s,%s\n' "$slide_thumbnail_x" "$second_slide_thumbnail_y"
        printf 'status-geometry=%s\n' "$status_geometry"
        printf 'new-slide-point=%s,%s\n' "$new_slide_x" "$new_slide_y"
        printf 'baseline-calibration=window-geometry-plus-baseline-screenshot\n'
    } > "$output/slide-pane-calibration.txt"

    # The preceding Alt/key-tip probes can leave badges painted until the
    # focused shell receives Escape. Establish and retain two identical frames
    # before taking the actual state baseline; this keeps exact comparisons
    # about slide state rather than transient key-tip chrome.
    send_active_key Escape || true
    capture "slide-pane-prestate-1.png"
    send_active_key Escape || true
    capture "slide-pane-prestate-2.png"
    prestate_stable=false
    if screen_matches "$output/slide-pane-prestate-1.png" "$output/slide-pane-prestate-2.png" 200; then
        prestate_stable=true
    else
        send_active_key Escape || true
        capture "slide-pane-prestate-3.png"
        if screen_matches "$output/slide-pane-prestate-2.png" "$output/slide-pane-prestate-3.png" 200; then
            prestate_stable=true
        fi
    fi
    capture "slide-pane-before.png"
    capture_region "slide-pane-before.png" "slide-pane-before-region.png" "$slide_pane_geometry"
    capture_region "slide-pane-before.png" "slide-main-before-region.png" "$main_view_geometry"
    new_slide_ready=true
    if ! click_pointer 1 "$new_slide_x" "$new_slide_y"; then
        new_slide_ready=false
    fi
    capture "slide-pane-created.png"
    capture_region "slide-pane-created.png" "slide-pane-created-region.png" "$slide_pane_geometry"
    capture_region "slide-pane-created.png" "slide-main-created-region.png" "$main_view_geometry"
    created_changed=false
    pane_changed=false
    main_view_changed=false
    if screen_changed "$output/slide-pane-before-region.png" "$output/slide-pane-created-region.png" 200; then
        pane_changed=true
    fi
    if screen_changed "$output/slide-main-before-region.png" "$output/slide-main-created-region.png" 200; then
        main_view_changed=true
    fi
    if $pane_changed; then
        created_changed=true
    fi
    {
        printf 'new-slide-ready=%s\n' "$new_slide_ready"
        printf 'prestate-stable=%s\n' "$prestate_stable"
        printf 'slide-pane-before=slide-pane-before-region.png\n'
        printf 'slide-pane-created=slide-pane-created-region.png\n'
        printf 'main-view-before=slide-main-before-region.png\n'
        printf 'main-view-created=slide-main-created-region.png\n'
        printf 'slide-pane-state-proven=%s\n' "$created_changed"
        printf 'main-view-contextual-evidence=true\n'
        printf 'main-view-changed=%s\n' "$main_view_changed"
        printf 'thumbnail-evidence=slide-pane-created-region.png\n'
    } > "$output/slide-pane-new-slide-create-proof.txt"
    if $new_slide_ready && $prestate_stable && $created_changed; then
        record "slide-pane-new-slide-create" "passed" "slide-pane-new-slide-create-proof.txt" \
            "Clicked the real FreeP bottom New Slide affordance and proved changed thumbnail-pane evidence; the calibrated main-view frame is retained as contextual evidence."
    else
        record "slide-pane-new-slide-create" "failed" "slide-pane-new-slide-create-proof.txt" \
            "The real New Slide input did not produce calibrated thumbnail-pane state evidence."
    fi

    send_key ctrl+z
    capture "slide-pane-after-undo.png"
    capture_region "slide-pane-after-undo.png" "slide-pane-undo-region.png" "$slide_pane_geometry"
    capture_region "slide-pane-after-undo.png" "slide-main-undo-region.png" "$main_view_geometry"
    undo_restored=false
    if screen_matches "$output/slide-pane-before-region.png" "$output/slide-pane-undo-region.png" 200 &&
       screen_matches "$output/slide-main-before-region.png" "$output/slide-main-undo-region.png" 200; then
        undo_restored=true
    fi
    {
        printf 'pre-create-pane=slide-pane-before-region.png\n'
        printf 'undo-pane=slide-pane-undo-region.png\n'
        printf 'pre-create-main=slide-main-before-region.png\n'
        printf 'undo-main=slide-main-undo-region.png\n'
        printf 'exact-calibrated-pre-create-state=%s\n' "$undo_restored"
    } > "$output/slide-pane-new-slide-undo-proof.txt"
    if $undo_restored; then
        record "slide-pane-new-slide-undo" "passed" "slide-pane-new-slide-undo-proof.txt" \
            "Ctrl+Z restored the calibrated pre-create thumbnail-pane state; the main-view frame is retained as contextual evidence."
    else
        record "slide-pane-new-slide-undo" "failed" "slide-pane-new-slide-undo-proof.txt" \
            "Ctrl+Z did not restore the calibrated pre-create thumbnail-pane state and contextual main-view frame."
    fi

    redo_gate_open=false
    if $created_changed && $undo_restored; then
        redo_gate_open=true
        send_key ctrl+y
    fi
    capture "slide-pane-after-redo.png"
    capture_region "slide-pane-after-redo.png" "slide-pane-redo-region.png" "$slide_pane_geometry"
    capture_region "slide-pane-after-redo.png" "slide-main-redo-region.png" "$main_view_geometry"
    redo_restored=false
    if $created_changed && $undo_restored &&
       screen_matches "$output/slide-pane-created-region.png" "$output/slide-pane-redo-region.png" 200 &&
       screen_matches "$output/slide-main-created-region.png" "$output/slide-main-redo-region.png" 200; then
        redo_restored=true
    fi
    {
        printf 'created-pane=slide-pane-created-region.png\n'
        printf 'redo-pane=slide-pane-redo-region.png\n'
        printf 'created-main=slide-main-created-region.png\n'
        printf 'redo-main=slide-main-redo-region.png\n'
        printf 'create-proven=%s\n' "$created_changed"
        printf 'undo-proven=%s\n' "$undo_restored"
        printf 'redo-gated-on-create-and-undo=%s\n' "$redo_gate_open"
        printf 'exact-calibrated-created-state=%s\n' "$redo_restored"
    } > "$output/slide-pane-new-slide-redo-proof.txt"
    if $redo_restored; then
        record "slide-pane-new-slide-redo" "passed" "slide-pane-new-slide-redo-proof.txt" \
            "Ctrl+Y restored the calibrated created thumbnail-pane state; the main-view frame is retained as contextual evidence."
    else
        record "slide-pane-new-slide-redo" "failed" "slide-pane-new-slide-redo-proof.txt" \
            "Ctrl+Y did not restore the calibrated created thumbnail-pane state and contextual main-view frame."
    fi

    slide_context_opened=false
    slide_context_dismissed=false
    run_slide_context_probe() {
        local id_prefix="$1" trigger="$2" before="$3" open="$4" dismissed="$5"
        local before_state="${id_prefix}-before-state.txt" open_state="${id_prefix}-open-state.txt"
        local open_proof="${id_prefix}-open-proof.txt" dismissal_proof="${id_prefix}-dismissal-proof.txt"
        local trigger_ready=true
        slide_context_opened=false
        slide_context_dismissed=false
        focus_app
        if ! click_pointer 1 "$slide_thumbnail_x" "$slide_thumbnail_y"; then
            trigger_ready=false
        fi
        capture "$before"
        capture_window_state "$before_state"
        if [[ "$trigger" == "keyboard" ]]; then
            if ! send_key shift+F10; then
                trigger_ready=false
            fi
        else
            if ! click_pointer 3 "$slide_thumbnail_x" "$slide_thumbnail_y"; then
                trigger_ready=false
            fi
        fi
        capture "$open"
        capture_window_state "$open_state"
        {
            printf 'thumbnail-point=%s,%s\n' "$slide_thumbnail_x" "$slide_thumbnail_y"
            printf 'before-screenshot=%s\n' "$before"
            printf 'open-screenshot=%s\n' "$open"
            printf 'dismissed-screenshot=%s\n' "$dismissed"
            printf 'before-window-state=%s\n' "$before_state"
            printf 'open-window-state=%s\n' "$open_state"
            printf 'trigger-ready=%s\n' "$trigger_ready"
            printf 'open-window-state-changed='; if ! cmp -s "$output/$before_state" "$output/$open_state"; then printf 'true\n'; else printf 'false\n'; fi
            printf 'open-screenshot-changed='; if screen_changed "$output/$before" "$output/$open" 200; then printf 'true\n'; else printf 'false\n'; fi
        } > "$output/$open_proof"
        if $trigger_ready && screen_changed "$output/$before" "$output/$open" 200; then
            slide_context_opened=true
            record "${id_prefix}-open" "passed" "$open_proof" \
                "The real FreeP slide thumbnail context menu opened through $trigger input; screenshot and window-state evidence are retained."
        else
            record "${id_prefix}-open" "failed" "$open_proof" \
                "The real FreeP slide thumbnail context menu did not produce a visible state transition."
        fi
        send_active_key Escape || true
        focus_app
        capture "$dismissed"
        capture_window_state "${id_prefix}-dismissed-state.txt"
        {
            printf 'before-screenshot=%s\n' "$before"
            printf 'open-screenshot=%s\n' "$open"
            printf 'dismissed-screenshot=%s\n' "$dismissed"
            printf 'dismissed-window-state=%s\n' "${id_prefix}-dismissed-state.txt"
            printf 'dismissed-returns-to-before='; if screen_matches "$output/$before" "$output/$dismissed" 200; then printf 'true\n'; else printf 'false\n'; fi
        } > "$output/$dismissal_proof"
        if screen_matches "$output/$before" "$output/$dismissed" 200; then
            slide_context_dismissed=true
            record "${id_prefix}-dismissal" "passed" "$dismissal_proof" \
                "Escape dismissed the real FreeP slide thumbnail context menu and returned to the pre-open view."
        else
            record "${id_prefix}-dismissal" "failed" "$dismissal_proof" \
                "Escape did not return to the pre-open slide-pane view."
        fi
    }

    run_slide_context_probe "slide-pane-keyboard-context" keyboard \
        "slide-pane-keyboard-context-before.png" "slide-pane-keyboard-context-open.png" "slide-pane-keyboard-context-dismissed.png"
    keyboard_context_opened="$slide_context_opened"
    keyboard_context_dismissed="$slide_context_dismissed"
    run_slide_context_probe "slide-pane-pointer-context" pointer \
        "slide-pane-pointer-context-before.png" "slide-pane-pointer-context-open.png" "slide-pane-pointer-context-dismissed.png"
    pointer_context_opened="$slide_context_opened"
    pointer_context_dismissed="$slide_context_dismissed"

    capture_slide_navigation_state() {
        local prefix="$1"
        capture "${prefix}.png"
        capture_region "${prefix}.png" "${prefix}-thumbnails.png" "$slide_pane_geometry"
        capture_region "${prefix}.png" "${prefix}-status.png" "$status_geometry"
    }

    navigation_start_gate=false
    if $redo_restored &&
       $keyboard_context_opened && $keyboard_context_dismissed &&
       $pointer_context_opened && $pointer_context_dismissed; then
        navigation_start_gate=true
    fi
    move_pointer "$main_view_x" "$main_view_y"
    capture_slide_navigation_state "slide-navigation-two-first"

    pointer_select_ready=false
    if $navigation_start_gate &&
       click_pointer 1 "$slide_thumbnail_x" "$second_slide_thumbnail_y"; then
        pointer_select_ready=true
    fi
    move_pointer "$main_view_x" "$main_view_y"
    capture_slide_navigation_state "slide-navigation-two-second"
    pointer_select_proven=false
    if $navigation_start_gate && $pointer_select_ready &&
       screen_changed "$output/slide-navigation-two-first-thumbnails.png" "$output/slide-navigation-two-second-thumbnails.png" 200 &&
       screen_changed "$output/slide-navigation-two-first-status.png" "$output/slide-navigation-two-second-status.png" 5; then
        pointer_select_proven=true
    fi
    {
        printf 'prior-sequence-proven=%s\n' "$navigation_start_gate"
        printf 'pointer-input-ready=%s\n' "$pointer_select_ready"
        printf 'second-thumbnail-point=%s,%s\n' "$slide_thumbnail_x" "$second_slide_thumbnail_y"
        printf 'before-thumbnails=slide-navigation-two-first-thumbnails.png\n'
        printf 'selected-thumbnails=slide-navigation-two-second-thumbnails.png\n'
        printf 'before-status=slide-navigation-two-first-status.png\n'
        printf 'selected-status=slide-navigation-two-second-status.png\n'
        printf 'thumbnail-and-status-transition-proven=%s\n' "$pointer_select_proven"
    } > "$output/slide-pane-pointer-select-second-proof.txt"
    if $pointer_select_proven; then
        record "slide-pane-pointer-select-second" "passed" "slide-pane-pointer-select-second-proof.txt" \
            "Pointer input selected the real second FreeP thumbnail; calibrated thumbnail-pane and status crops prove the selection transition."
    else
        record "slide-pane-pointer-select-second" "failed" "slide-pane-pointer-select-second-proof.txt" \
            "Pointer selection of the second FreeP thumbnail was not proven from the gated calibrated crops."
    fi

    keyboard_up_sent=false
    if $pointer_select_proven && send_key Up; then
        keyboard_up_sent=true
    fi
    capture_slide_navigation_state "slide-navigation-up-first"
    keyboard_up_proven=false
    if $pointer_select_proven && $keyboard_up_sent &&
       screen_matches "$output/slide-navigation-two-first-thumbnails.png" "$output/slide-navigation-up-first-thumbnails.png" 200 &&
       screen_matches "$output/slide-navigation-two-first-status.png" "$output/slide-navigation-up-first-status.png" 30; then
        keyboard_up_proven=true
    fi
    {
        printf 'pointer-select-proven=%s\n' "$pointer_select_proven"
        printf 'keyboard-up-sent=%s\n' "$keyboard_up_sent"
        printf 'expected-thumbnails=slide-navigation-two-first-thumbnails.png\n'
        printf 'actual-thumbnails=slide-navigation-up-first-thumbnails.png\n'
        printf 'expected-status=slide-navigation-two-first-status.png\n'
        printf 'actual-status=slide-navigation-up-first-status.png\n'
        printf 'exact-first-slide-state-proven=%s\n' "$keyboard_up_proven"
    } > "$output/slide-pane-keyboard-up-first-proof.txt"
    if $keyboard_up_proven; then
        record "slide-pane-keyboard-up-first" "passed" "slide-pane-keyboard-up-first-proof.txt" \
            "Up moved the focused real slide pane from slide 2 to slide 1 and restored the calibrated first-slide state."
    else
        record "slide-pane-keyboard-up-first" "failed" "slide-pane-keyboard-up-first-proof.txt" \
            "The gated Up input did not restore the calibrated first-slide thumbnail and status state."
    fi

    duplicate_sent=false
    if $keyboard_up_proven && send_key ctrl+d; then
        duplicate_sent=true
    fi
    capture_slide_navigation_state "slide-navigation-duplicated-three"
    duplicate_proven=false
    if $keyboard_up_proven && $duplicate_sent &&
       screen_changed "$output/slide-navigation-up-first-thumbnails.png" "$output/slide-navigation-duplicated-three-thumbnails.png" 200 &&
       screen_changed "$output/slide-navigation-up-first-status.png" "$output/slide-navigation-duplicated-three-status.png" 5; then
        duplicate_proven=true
    fi
    {
        printf 'keyboard-up-proven=%s\n' "$keyboard_up_proven"
        printf 'ctrl-d-sent=%s\n' "$duplicate_sent"
        printf 'before-thumbnails=slide-navigation-up-first-thumbnails.png\n'
        printf 'duplicated-thumbnails=slide-navigation-duplicated-three-thumbnails.png\n'
        printf 'before-status=slide-navigation-up-first-status.png\n'
        printf 'duplicated-status=slide-navigation-duplicated-three-status.png\n'
        printf 'three-slide-state-proven=%s\n' "$duplicate_proven"
    } > "$output/slide-pane-duplicate-create-proof.txt"
    if $duplicate_proven; then
        record "slide-pane-duplicate-create" "passed" "slide-pane-duplicate-create-proof.txt" \
            "Ctrl+D on the focused slide pane created a third slide, proven by calibrated thumbnail-pane and status transitions."
    else
        record "slide-pane-duplicate-create" "failed" "slide-pane-duplicate-create-proof.txt" \
            "The gated focused Ctrl+D input did not prove a three-slide state."
    fi

    duplicate_undo_sent=false
    if $duplicate_proven && send_key ctrl+z; then
        duplicate_undo_sent=true
    fi
    capture_slide_navigation_state "slide-navigation-duplicate-undo-two"
    duplicate_undo_proven=false
    if $duplicate_proven && $duplicate_undo_sent &&
       screen_matches "$output/slide-navigation-up-first-thumbnails.png" "$output/slide-navigation-duplicate-undo-two-thumbnails.png" 200 &&
       screen_matches "$output/slide-navigation-up-first-status.png" "$output/slide-navigation-duplicate-undo-two-status.png" 30; then
        duplicate_undo_proven=true
    fi
    {
        printf 'duplicate-proven=%s\n' "$duplicate_proven"
        printf 'ctrl-z-sent=%s\n' "$duplicate_undo_sent"
        printf 'expected-two-thumbnails=slide-navigation-up-first-thumbnails.png\n'
        printf 'undo-thumbnails=slide-navigation-duplicate-undo-two-thumbnails.png\n'
        printf 'expected-two-status=slide-navigation-up-first-status.png\n'
        printf 'undo-status=slide-navigation-duplicate-undo-two-status.png\n'
        printf 'exact-two-slide-state-proven=%s\n' "$duplicate_undo_proven"
    } > "$output/slide-pane-duplicate-undo-proof.txt"
    if $duplicate_undo_proven; then
        record "slide-pane-duplicate-undo" "passed" "slide-pane-duplicate-undo-proof.txt" \
            "Ctrl+Z removed the duplicate and restored the exact calibrated two-slide first-selected state."
    else
        record "slide-pane-duplicate-undo" "failed" "slide-pane-duplicate-undo-proof.txt" \
            "The gated duplicate undo did not restore the calibrated two-slide state."
    fi

    duplicate_redo_sent=false
    if $duplicate_undo_proven && send_key ctrl+y; then
        duplicate_redo_sent=true
    fi
    capture_slide_navigation_state "slide-navigation-duplicate-redo-three"
    duplicate_redo_proven=false
    if $duplicate_undo_proven && $duplicate_redo_sent &&
       screen_matches "$output/slide-navigation-duplicated-three-thumbnails.png" "$output/slide-navigation-duplicate-redo-three-thumbnails.png" 200 &&
       screen_matches "$output/slide-navigation-duplicated-three-status.png" "$output/slide-navigation-duplicate-redo-three-status.png" 30; then
        duplicate_redo_proven=true
    fi
    {
        printf 'duplicate-undo-proven=%s\n' "$duplicate_undo_proven"
        printf 'ctrl-y-sent=%s\n' "$duplicate_redo_sent"
        printf 'expected-three-thumbnails=slide-navigation-duplicated-three-thumbnails.png\n'
        printf 'redo-thumbnails=slide-navigation-duplicate-redo-three-thumbnails.png\n'
        printf 'expected-three-status=slide-navigation-duplicated-three-status.png\n'
        printf 'redo-status=slide-navigation-duplicate-redo-three-status.png\n'
        printf 'exact-three-slide-state-proven=%s\n' "$duplicate_redo_proven"
    } > "$output/slide-pane-duplicate-redo-proof.txt"
    if $duplicate_redo_proven; then
        record "slide-pane-duplicate-redo" "passed" "slide-pane-duplicate-redo-proof.txt" \
            "Ctrl+Y restored the exact calibrated three-slide duplicated state after the proven undo."
    else
        record "slide-pane-duplicate-redo" "failed" "slide-pane-duplicate-redo-proof.txt" \
            "The gated duplicate redo did not restore the calibrated three-slide state."
    fi

    delete_sent=false
    if $duplicate_redo_proven && send_key Delete; then
        delete_sent=true
    fi
    capture_slide_navigation_state "slide-navigation-delete-two"
    delete_proven=false
    if $duplicate_redo_proven && $delete_sent &&
       screen_changed "$output/slide-navigation-duplicate-redo-three-thumbnails.png" "$output/slide-navigation-delete-two-thumbnails.png" 200 &&
       screen_changed "$output/slide-navigation-duplicate-redo-three-status.png" "$output/slide-navigation-delete-two-status.png" 5; then
        delete_proven=true
    fi
    {
        printf 'duplicate-redo-proven=%s\n' "$duplicate_redo_proven"
        printf 'delete-sent=%s\n' "$delete_sent"
        printf 'before-thumbnails=slide-navigation-duplicate-redo-three-thumbnails.png\n'
        printf 'deleted-thumbnails=slide-navigation-delete-two-thumbnails.png\n'
        printf 'before-status=slide-navigation-duplicate-redo-three-status.png\n'
        printf 'deleted-status=slide-navigation-delete-two-status.png\n'
        printf 'two-slide-delete-state-proven=%s\n' "$delete_proven"
    } > "$output/slide-pane-delete-selected-proof.txt"
    if $delete_proven; then
        record "slide-pane-delete-selected" "passed" "slide-pane-delete-selected-proof.txt" \
            "Delete on the focused slide pane removed the selected duplicate and produced a calibrated two-slide state."
    else
        record "slide-pane-delete-selected" "failed" "slide-pane-delete-selected-proof.txt" \
            "The gated focused Delete input did not prove the expected two-slide state."
    fi

    delete_undo_sent=false
    if $delete_proven && send_key ctrl+z; then
        delete_undo_sent=true
    fi
    capture_slide_navigation_state "slide-navigation-delete-undo-three"
    delete_undo_proven=false
    if $delete_proven && $delete_undo_sent &&
       screen_matches "$output/slide-navigation-duplicate-redo-three-thumbnails.png" "$output/slide-navigation-delete-undo-three-thumbnails.png" 200 &&
       screen_matches "$output/slide-navigation-duplicate-redo-three-status.png" "$output/slide-navigation-delete-undo-three-status.png" 30; then
        delete_undo_proven=true
    fi
    {
        printf 'delete-proven=%s\n' "$delete_proven"
        printf 'ctrl-z-sent=%s\n' "$delete_undo_sent"
        printf 'expected-three-thumbnails=slide-navigation-duplicate-redo-three-thumbnails.png\n'
        printf 'undo-delete-thumbnails=slide-navigation-delete-undo-three-thumbnails.png\n'
        printf 'expected-three-status=slide-navigation-duplicate-redo-three-status.png\n'
        printf 'undo-delete-status=slide-navigation-delete-undo-three-status.png\n'
        printf 'exact-three-slide-state-proven=%s\n' "$delete_undo_proven"
    } > "$output/slide-pane-delete-undo-proof.txt"
    if $delete_undo_proven; then
        record "slide-pane-delete-undo" "passed" "slide-pane-delete-undo-proof.txt" \
            "Ctrl+Z undid the selected-slide deletion and restored the exact calibrated three-slide state."
    else
        record "slide-pane-delete-undo" "failed" "slide-pane-delete-undo-proof.txt" \
            "The gated delete undo did not restore the calibrated three-slide state."
    fi
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
