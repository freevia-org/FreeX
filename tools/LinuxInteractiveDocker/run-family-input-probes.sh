#!/usr/bin/env bash
set -Eeuo pipefail

export DISPLAY="${DISPLAY:-:99}"

output="${1:-/work/family-validation}"
input_delay_ms="${FAMILY_X11_INPUT_DELAY_MS:-160}"
settle_seconds="${FAMILY_X11_SETTLE_SECONDS:-0.45}"
pointer_timeout_seconds="${FAMILY_X11_POINTER_TIMEOUT_SECONDS:-3}"
clipboard_timeout_seconds="${FAMILY_X11_CLIPBOARD_TIMEOUT_SECONDS:-3}"
text_entry_margin_ms="${FAMILY_X11_TEXT_ENTRY_MARGIN_MS:-5000}"
text_cleanup_timeout_seconds="${FAMILY_X11_TEXT_CLEANUP_TIMEOUT_SECONDS:-1}"
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
        "editor-autocorrect-typing"
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
        "file-open-shortcut-dialog-open"
        "file-open-shortcut-dialog-dismissal"
        "file-save-shortcut-dialog-open"
        "file-save-shortcut-dialog-dismissal"
        "file-save-as-shortcut-dialog-open"
        "file-save-as-shortcut-dialog-dismissal"
        "file-print-shortcut-dialog-open"
        "file-print-shortcut-dialog-dismissal"
        "file-new-shortcut-dirty-prompt-open"
        "file-new-shortcut-cancel-preserves"
        "file-new-shortcut-discard-creates-clean"
        "backstage-print-open"
        "backstage-print-dismissal"
        "backstage-export-open"
        "backstage-export-dismissal"
        "options-open"
        "options-tab-navigation"
        "options-focus"
        "options-close"
    )
else
    required_ids+=(
        "nested-keytip-prefix-deferral"
        "animation-pane-physical-workflow"
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

record_evidence_set() {
    local id="$1" status="$2" note="$3"
    shift 3
    local evidence evidence_json="" separator=""
    for evidence in "$@"; do
        evidence_json+="$separator\"$(json_escape "$evidence")\""
        separator=","
    done
    results+=("{\"id\":\"$(json_escape "$id")\",\"category\":\"physical-x11-smoke\",\"status\":\"$status\",\"evidenceLevel\":\"physical-x11-input\",\"evidence\":[${evidence_json}],\"note\":\"$(json_escape "$note")\"}")
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

release_active_text_keys() {
    local active_id="$1" text_value="$2" character key_name index
    for key_name in ctrl shift alt super meta; do
        timeout --foreground --kill-after=1s "$text_cleanup_timeout_seconds" \
            xdotool keyup --window "$active_id" "$key_name" >/dev/null 2>&1 || true
    done
    for ((index = 0; index < ${#text_value}; index++)); do
        character="${text_value:index:1}"
        case "$character" in
            [[:alnum:]]) key_name="$character" ;;
            -) key_name=minus ;;
            _) key_name=underscore ;;
            ' ') key_name=space ;;
            *) continue ;;
        esac
        timeout --foreground --kill-after=1s "$text_cleanup_timeout_seconds" \
            xdotool keyup --window "$active_id" "$key_name" >/dev/null 2>&1 || true
    done
}

send_active_text() {
    local text_value="$1" active_id text_length text_budget_ms text_timeout_seconds
    active_id="$(xdotool getactivewindow 2>/dev/null || true)"
    if [[ -z "$active_id" ]]; then
        return 1
    fi
    text_length="${#text_value}"
    text_budget_ms=$((text_length * input_delay_ms + text_entry_margin_ms))
    text_timeout_seconds=$(( (text_budget_ms + 999) / 1000 ))
    (( text_timeout_seconds < 1 )) && text_timeout_seconds=1
    release_active_text_keys "$active_id" "$text_value"
    if ! timeout --foreground --kill-after=1s "$text_timeout_seconds" \
        xdotool type --clearmodifiers --delay "$input_delay_ms" --window "$active_id" "$text_value"; then
        release_active_text_keys "$active_id" "$text_value"
        return 1
    fi
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

screen_difference() {
    local before="$1" after="$2" changed
    changed="$(compare -metric AE "$before" "$after" null: 2>&1 || true)"
    if [[ "$changed" =~ ^[0-9]+ ]]; then
        printf '%s' "${BASH_REMATCH[0]}"
    else
        printf 'unknown'
    fi
}

capture_region() {
    local source_name="$1" name="$2" geometry="$3"
    convert "$output/$source_name" -crop "$geometry" +repage "$output/$name"
    track_screenshot "$name"
}

image_color_count() {
    local image_path="$1" geometry="$2" color="$3" count
    count="$(convert "$image_path" -crop "$geometry" +repage -format '%c' histogram:info:- 2>/dev/null |
        awk -v needle="$color" '$0 ~ needle { split($0, fields, ":"); print fields[1]; exit }' |
        tr -d '[:space:]')"
    printf '%s' "${count:-0}"
}

active_window_is_owner() {
    [[ "$(xdotool getactivewindow 2>/dev/null || true)" == "$window_id" ]]
}

window_id_in_list() {
    local wanted="$1"
    shift
    local candidate
    for candidate in "$@"; do
        if [[ "$candidate" == "$wanted" ]]; then
            return 0
        fi
    done
    return 1
}

capture_shortcut_window_state() {
    local name="$1" phase="$2" candidate_id="$3" baseline_count="$4" observed_count="$5"
    local state_visible_ids=()
    local candidate_class_metadata=""
    mapfile -t state_visible_ids < <(xdotool search --onlyvisible --name '.*' 2>/dev/null || true)
    if [[ -n "$candidate_id" ]]; then
        candidate_class_metadata="$(xprop -id "$candidate_id" WM_CLASS 2>/dev/null || true)"
    fi
    {
        printf 'phase=%s\n' "$phase"
        printf 'owner-window-id=%s\n' "$window_id"
        printf 'candidate-window-id=%s\n' "$candidate_id"
        printf 'baseline-window-count=%s\n' "$baseline_count"
        printf 'observed-window-count=%s\n' "$observed_count"
        printf 'active-window=%s\n' "$(xdotool getactivewindow 2>/dev/null || true)"
        printf 'focus-window=%s\n' "$(xdotool getwindowfocus 2>/dev/null || true)"
        printf 'owner-active='; if active_window_is_owner; then printf 'true\n'; else printf 'false\n'; fi
        printf 'owner-focused='; if [[ "$(xdotool getwindowfocus 2>/dev/null || true)" == "$window_id" ]]; then printf 'true\n'; else printf 'false\n'; fi
        printf 'visible-window-ids='; printf '%s ' "${state_visible_ids[@]}"; printf '\n'
        printf 'candidate-title=%s\n' "$(if [[ -n "$candidate_id" ]]; then xdotool getwindowname "$candidate_id" 2>/dev/null || true; fi)"
        printf 'candidate-class-availability=%s\n' "$(if [[ -n "$candidate_class_metadata" ]]; then printf 'available'; else printf 'unavailable-native-window-metadata'; fi)"
        printf 'candidate-class-begin\n'
        printf '%s\n' "$candidate_class_metadata"
        printf 'candidate-class-end\n'
        printf 'wmctrl-list-begin\n'
        wmctrl -l 2>/dev/null || true
        printf 'wmctrl-list-end\n'
    } > "$output/$name"
}

find_new_shortcut_window() {
    local active_id="$1"
    shift
    file_shortcut_window_id=""
    local candidate
    for candidate in "$@"; do
        if ! window_id_in_list "$candidate" "${file_lifecycle_before_ids[@]}"; then
            if [[ "$candidate" == "$active_id" ]]; then
                file_shortcut_window_id="$candidate"
                return 0
            fi
            if [[ -z "$file_shortcut_window_id" ]]; then
                file_shortcut_window_id="$candidate"
            fi
        fi
    done
    [[ -n "$file_shortcut_window_id" ]]
}

run_file_shortcut_window_lifecycle() {
    local id_prefix="$1" key="$2" label="$3"
    local before="${id_prefix}-before.png"
    local open="${id_prefix}-open.png"
    local focused="${id_prefix}-focused.png"
    local dismissed="${id_prefix}-dismissed.png"
    local before_state="${id_prefix}-before-state.txt"
    local open_state="${id_prefix}-open-state.txt"
    local focused_state="${id_prefix}-focused-state.txt"
    local dismissed_state="${id_prefix}-dismissed-state.txt"
    local proof="${id_prefix}-proof.txt"
    local baseline_count open_count dismissed_count
    local active_after_open active_after_focus focus_after_focus
    local candidate_title="" candidate_class=""
    local trigger_ready=true focus_ready=false
    local separate_window=false count_increased=false
    local title_ready=false screen_open_changed=false
    local dismiss_ready=true dialog_removed=false count_restored=false
    local owner_restored=false screen_dismissed_changed=false screen_restored=false

    focus_app
    mapfile -t file_lifecycle_before_ids < <(xdotool search --onlyvisible --name '.*' 2>/dev/null || true)
    baseline_count="$(window_count)"
    capture "$before"
    capture_window_state "$before_state"
    if ! send_active_key "$key"; then
        trigger_ready=false
    fi
    capture "$open"
    mapfile -t file_lifecycle_after_ids < <(xdotool search --onlyvisible --name '.*' 2>/dev/null || true)
    open_count="$(window_count)"
    active_after_open="$(xdotool getactivewindow 2>/dev/null || true)"
    find_new_shortcut_window "$active_after_open" "${file_lifecycle_after_ids[@]}" || true
    if [[ -n "$file_shortcut_window_id" ]]; then
        candidate_title="$(xdotool getwindowname "$file_shortcut_window_id" 2>/dev/null || true)"
        candidate_class="$(xprop -id "$file_shortcut_window_id" WM_CLASS 2>/dev/null || true)"
        [[ -n "$candidate_title" ]] && title_ready=true
        if [[ "$file_shortcut_window_id" != "$window_id" ]] &&
           ! window_id_in_list "$file_shortcut_window_id" "${file_lifecycle_before_ids[@]}"; then
            separate_window=true
        fi
    fi
    if (( open_count > baseline_count )); then
        count_increased=true
    fi
    capture_shortcut_window_state "$open_state" open "$file_shortcut_window_id" "$baseline_count" "$open_count"
    if [[ -n "$file_shortcut_window_id" ]] &&
       timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
           xdotool windowactivate --sync "$file_shortcut_window_id" 2>/dev/null &&
       timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
           xdotool windowfocus "$file_shortcut_window_id" 2>/dev/null; then
        sleep 0.12
    fi
    active_after_focus="$(xdotool getactivewindow 2>/dev/null || true)"
    focus_after_focus="$(xdotool getwindowfocus 2>/dev/null || true)"
    if [[ -n "$file_shortcut_window_id" &&
          "$active_after_focus" == "$file_shortcut_window_id" &&
          "$focus_after_focus" == "$file_shortcut_window_id" ]]; then
        focus_ready=true
    fi
    capture "$focused"
    capture_shortcut_window_state "$focused_state" focused "$file_shortcut_window_id" "$baseline_count" "$open_count"
    if screen_changed "$output/$before" "$output/$open" 200; then
        screen_open_changed=true
    fi
    {
        printf 'label=%s\n' "$label"
        printf 'shortcut=%s\n' "$key"
        printf 'before-screenshot=%s\n' "$before"
        printf 'open-screenshot=%s\n' "$open"
        printf 'focused-screenshot=%s\n' "$focused"
        printf 'dismissed-screenshot=%s\n' "$dismissed"
        printf 'before-state=%s\n' "$before_state"
        printf 'open-state=%s\n' "$open_state"
        printf 'focused-state=%s\n' "$focused_state"
        printf 'dismissed-state=%s\n' "$dismissed_state"
        printf 'owner-window-id=%s\n' "$window_id"
        printf 'candidate-window-id=%s\n' "$file_shortcut_window_id"
        printf 'candidate-title=%s\n' "$candidate_title"
        printf 'candidate-class=%s\n' "$candidate_class"
        printf 'active-on-open=%s\n' "$active_after_open"
        printf 'active-after-focus=%s\n' "$active_after_focus"
        printf 'focus-after-focus=%s\n' "$focus_after_focus"
        printf 'baseline-window-count=%s\n' "$baseline_count"
        printf 'open-window-count=%s\n' "$open_count"
        printf 'trigger-ready=%s\n' "$trigger_ready"
        printf 'separate-window=%s\n' "$separate_window"
        printf 'window-count-increased=%s\n' "$count_increased"
        printf 'title-ready=%s\n' "$title_ready"
        printf 'class-metadata=%s\n' "$(if [[ -n "$candidate_class" ]]; then printf 'available'; else printf 'unavailable-native-window-metadata'; fi)"
        printf 'active-and-focused=%s\n' "$focus_ready"
        printf 'open-screenshot-changed=%s\n' "$screen_open_changed"
    } > "$output/$proof"
    if $trigger_ready && $separate_window && $count_increased && $title_ready &&
       $focus_ready && $screen_open_changed; then
        record_evidence_set "${id_prefix}-open" "passed" \
            "$label opened a newly discovered visible top-level window with title, optional WM_CLASS capture, increased count, active focus, and screenshot transition." \
            "$proof" "$before" "$open" "$focused" "$before_state" "$open_state" "$focused_state"
    else
        record_evidence_set "${id_prefix}-open" "failed" \
            "$label did not prove a separate focused top-level window with title, count, focus, and screenshot evidence." \
            "$proof" "$before" "$open" "$focused" "$before_state" "$open_state" "$focused_state"
    fi

    if ! send_active_key Escape; then
        dismiss_ready=false
    fi
    focus_app
    capture "$dismissed"
    mapfile -t file_lifecycle_dismissed_ids < <(xdotool search --onlyvisible --name '.*' 2>/dev/null || true)
    dismissed_count="$(window_count)"
    capture_shortcut_window_state "$dismissed_state" dismissed "$file_shortcut_window_id" "$baseline_count" "$dismissed_count"
    if [[ -n "$file_shortcut_window_id" ]] &&
       window_id_in_list "$file_shortcut_window_id" "${file_lifecycle_dismissed_ids[@]}"; then
        dialog_removed=false
    else
        dialog_removed=true
    fi
    if [[ "$dismissed_count" -eq "$baseline_count" ]]; then
        count_restored=true
    fi
    if active_window_is_owner &&
       [[ "$(xdotool getwindowfocus 2>/dev/null || true)" == "$window_id" ]]; then
        owner_restored=true
    fi
    if screen_changed "$output/$open" "$output/$dismissed" 100; then
        screen_dismissed_changed=true
    fi
    if screen_matches "$output/$before" "$output/$dismissed" 500; then
        screen_restored=true
    fi
    {
        printf 'dismiss-ready=%s\n' "$dismiss_ready"
        printf 'dialog-removed=%s\n' "$dialog_removed"
        printf 'baseline-window-count=%s\n' "$baseline_count"
        printf 'dismissed-window-count=%s\n' "$dismissed_count"
        printf 'window-count-restored=%s\n' "$count_restored"
        printf 'owner-restored=%s\n' "$owner_restored"
        printf 'dismissed-screenshot-changed=%s\n' "$screen_dismissed_changed"
        printf 'screen-restored-to-before=%s\n' "$screen_restored"
    } >> "$output/$proof"
    if $dismiss_ready && $dialog_removed && $count_restored && $owner_restored &&
       $screen_dismissed_changed && $screen_restored; then
        record_evidence_set "${id_prefix}-dismissal" "passed" \
            "Escape removed the $label top-level window, restored the exact owner focus/count, and returned the screen to its pre-trigger state." \
            "$proof" "$open" "$focused" "$dismissed" "$open_state" "$focused_state" "$dismissed_state"
    else
        record_evidence_set "${id_prefix}-dismissal" "failed" \
            "Escape did not prove removal and exact owner restoration for the $label top-level window." \
            "$proof" "$open" "$focused" "$dismissed" "$open_state" "$focused_state" "$dismissed_state"
    fi
}

run_backstage_pane_lifecycle() {
    local id_prefix="$1" target_down="$2" label="$3"
    local before="${id_prefix}-before.png"
    local backstage_open="${id_prefix}-backstage-open.png"
    local pane_open="${id_prefix}-open.png"
    local dismissed="${id_prefix}-dismissed.png"
    local before_state="${id_prefix}-before-state.txt"
    local backstage_state="${id_prefix}-backstage-state.txt"
    local pane_state="${id_prefix}-open-state.txt"
    local dismissed_state="${id_prefix}-dismissed-state.txt"
    local proof="${id_prefix}-proof.txt"
    local baseline_count open_count dismissed_count
    local backstage_id="" active_after_open active_after_pane
    local trigger_ready=true separate_window=false count_increased=false
    local pane_selected=false pane_changed=false dismiss_ready=true
    local pane_removed=false count_restored=false owner_restored=false
    local dismissed_changed=false screen_restored=false
    local visible_after_dismissal=()
    local step=0

    focus_app
    baseline_count="$(window_count)"
    capture "$before"
    capture_window_state "$before_state"
    if ! send_key Alt_L || ! send_key "$file_key"; then
        trigger_ready=false
    fi
    capture "$backstage_open"
    backstage_id="$(xdotool getactivewindow 2>/dev/null || true)"
    open_count="$(window_count)"
    active_after_open="$backstage_id"
    if [[ -n "$backstage_id" && "$backstage_id" != "$window_id" ]] && (( open_count > baseline_count )); then
        separate_window=true
        count_increased=true
    fi
    capture_shortcut_window_state "$backstage_state" backstage "$backstage_id" "$baseline_count" "$open_count"

    if [[ -n "$backstage_id" ]]; then
        send_active_key Home || true
        for ((step = 0; step < target_down; step++)); do
            send_active_key Down || true
        done
        send_active_key Return || true
    fi
    capture "$pane_open"
    active_after_pane="$(xdotool getactivewindow 2>/dev/null || true)"
    capture_shortcut_window_state "$pane_state" pane "$backstage_id" "$baseline_count" "$(window_count)"
    if [[ -n "$backstage_id" && "$active_after_pane" == "$backstage_id" ]]; then
        pane_selected=true
    fi
    if screen_changed "$backstage_open" "$pane_open" 160; then
        pane_changed=true
    fi
    {
        printf 'label=%s\n' "$label"
        printf 'target-down=%s\n' "$target_down"
        printf 'owner-window-id=%s\n' "$window_id"
        printf 'backstage-window-id=%s\n' "$backstage_id"
        printf 'active-on-open=%s\n' "$active_after_open"
        printf 'active-on-pane=%s\n' "$active_after_pane"
        printf 'baseline-window-count=%s\n' "$baseline_count"
        printf 'open-window-count=%s\n' "$open_count"
        printf 'trigger-ready=%s\n' "$trigger_ready"
        printf 'separate-window=%s\n' "$separate_window"
        printf 'window-count-increased=%s\n' "$count_increased"
        printf 'pane-selected-and-focused=%s\n' "$pane_selected"
        printf 'pane-screenshot-changed=%s\n' "$pane_changed"
    } > "$output/$proof"
    if $trigger_ready && $separate_window && $count_increased && $pane_selected && $pane_changed; then
        record_evidence_set "${id_prefix}-open" "passed" \
            "$label opened the real FreeW Backstage rail and selected its pane through physical keyboard navigation; the owner/window-count/focus transition and pane screenshot are retained." \
            "$proof" "$before" "$backstage_open" "$pane_open" "$before_state" "$backstage_state" "$pane_state"
    else
        record_evidence_set "${id_prefix}-open" "failed" \
            "$label did not prove a separate focused Backstage window, deterministic rail navigation, and pane transition." \
            "$proof" "$before" "$backstage_open" "$pane_open" "$before_state" "$backstage_state" "$pane_state"
    fi

    if ! send_active_key Escape; then
        dismiss_ready=false
    fi
    focus_app
    capture "$dismissed"
    mapfile -t visible_after_dismissal < <(xdotool search --onlyvisible --name '.*' 2>/dev/null || true)
    dismissed_count="$(window_count)"
    capture_shortcut_window_state "$dismissed_state" dismissed "$backstage_id" "$baseline_count" "$dismissed_count"
    if [[ -n "$backstage_id" ]] && window_id_in_list "$backstage_id" "${visible_after_dismissal[@]}"; then
        pane_removed=false
    else
        pane_removed=true
    fi
    if [[ "$dismissed_count" -eq "$baseline_count" ]]; then
        count_restored=true
    fi
    if active_window_is_owner && [[ "$(xdotool getwindowfocus 2>/dev/null || true)" == "$window_id" ]]; then
        owner_restored=true
    fi
    if screen_changed "$pane_open" "$dismissed" 100; then
        dismissed_changed=true
    fi
    if screen_matches "$before" "$dismissed" 500; then
        screen_restored=true
    fi
    {
        printf 'dismiss-ready=%s\n' "$dismiss_ready"
        printf 'backstage-removed=%s\n' "$pane_removed"
        printf 'baseline-window-count=%s\n' "$baseline_count"
        printf 'dismissed-window-count=%s\n' "$dismissed_count"
        printf 'window-count-restored=%s\n' "$count_restored"
        printf 'owner-restored=%s\n' "$owner_restored"
        printf 'dismissed-screenshot-changed=%s\n' "$dismissed_changed"
        printf 'screen-restored-to-before=%s\n' "$screen_restored"
    } >> "$output/$proof"
    if $dismiss_ready && $pane_removed && $count_restored && $owner_restored && $dismissed_changed && $screen_restored; then
        record_evidence_set "${id_prefix}-dismissal" "passed" \
            "Escape dismissed the real $label Backstage pane, restored the owner focus/window count, and returned to the pre-open view." \
            "$proof" "$pane_open" "$dismissed" "$pane_state" "$dismissed_state"
    else
        record_evidence_set "${id_prefix}-dismissal" "failed" \
            "Escape did not prove removal and exact owner restoration for the real $label Backstage pane." \
            "$proof" "$pane_open" "$dismissed" "$pane_state" "$dismissed_state"
    fi
}

run_options_lifecycle() {
    local before="options-before.png" backstage_open="options-backstage-open.png"
    local pane_open="options-backstage-pane.png" dialog_open="options-dialog-open.png"
    local tabbed="options-tab-navigation.png" focused="options-focus.png" closed="options-closed.png"
    local before_state="options-before-state.txt" backstage_state="options-backstage-state.txt"
    local pane_state="options-backstage-pane-state.txt" dialog_state="options-dialog-state.txt"
    local tabbed_state="options-tab-navigation-state.txt" focused_state="options-focus-state.txt"
    local closed_state="options-closed-state.txt" proof="options-physical-workflow-proof.txt"
    local baseline_count backstage_count dialog_count closed_count backstage_id="" options_id=""
    local trigger_ready=true backstage_ready=false pane_ready=false dialog_ready=false
    local tab_ready=false focus_ready=false close_ready=false dialog_removed=false
    local count_restored=false owner_restored=false screen_restored=false
    local backstage_geometry="" backstage_x="" backstage_y="" backstage_width="" backstage_height=""
    local options_click_x="" options_click_y="" options_y_offset=0 active_after_dialog="" active_after_tab="" active_after_focus=""
    local visible_after_close=()

    focus_app
    baseline_count="$(window_count)"
    capture "$before"
    capture_window_state "$before_state"
    if ! send_key Alt_L || ! send_key "$file_key"; then
        trigger_ready=false
    fi
    capture "$backstage_open"
    backstage_id="$(xdotool getactivewindow 2>/dev/null || true)"
    backstage_count="$(window_count)"
    if [[ -n "$backstage_id" && "$backstage_id" != "$window_id" ]] && (( backstage_count > baseline_count )); then
        backstage_ready=true
    fi
    capture_shortcut_window_state "$backstage_state" backstage "$backstage_id" "$baseline_count" "$backstage_count"

    if $backstage_ready; then
        send_active_key End || true
        send_active_key Return || true
    fi
    capture "$pane_open"
    capture_shortcut_window_state "$pane_state" options-pane "$backstage_id" "$baseline_count" "$(window_count)"
    if [[ "$(xdotool getactivewindow 2>/dev/null || true)" == "$backstage_id" ]] && screen_changed "$backstage_open" "$pane_open" 160; then
        pane_ready=true
    fi

    if $pane_ready; then
        backstage_geometry="$(xdotool getwindowgeometry --shell "$backstage_id" 2>/dev/null || true)"
        eval "$backstage_geometry"
        backstage_x="$X"
        backstage_y="$Y"
        backstage_width="$WIDTH"
        backstage_height="$HEIGHT"
        options_click_x=$((backstage_x + 260))
        for options_y_offset in 215 235 255; do
            options_click_y=$((backstage_y + options_y_offset))
            click_pointer 1 "$options_click_x" "$options_click_y" || true
            if (( $(window_count) > baseline_count + 1 )); then
                break
            fi
        done
    fi
    capture "$dialog_open"
    options_id="$(xdotool getactivewindow 2>/dev/null || true)"
    dialog_count="$(window_count)"
    active_after_dialog="$options_id"
    if [[ -n "$options_id" && "$options_id" != "$window_id" && "$options_id" != "$backstage_id" ]] &&
       (( dialog_count > baseline_count + 1 )); then
        dialog_ready=true
    fi
    capture_shortcut_window_state "$dialog_state" options-dialog "$options_id" "$baseline_count" "$dialog_count"
    if $dialog_ready && screen_changed "$pane_open" "$dialog_open" 160; then
        dialog_ready=true
    else
        dialog_ready=false
    fi

    if $dialog_ready; then
        send_active_key ctrl+Tab || true
        send_active_key ctrl+Tab || true
    fi
    capture "$tabbed"
    active_after_tab="$(xdotool getactivewindow 2>/dev/null || true)"
    capture_shortcut_window_state "$tabbed_state" tab-navigation "$options_id" "$baseline_count" "$(window_count)"
    if $dialog_ready && [[ "$active_after_tab" == "$options_id" ]] && screen_changed "$dialog_open" "$tabbed" 100; then
        tab_ready=true
    fi

    if $dialog_ready; then
        send_active_key Tab || true
    fi
    capture "$focused"
    active_after_focus="$(xdotool getactivewindow 2>/dev/null || true)"
    capture_shortcut_window_state "$focused_state" focus "$options_id" "$baseline_count" "$(window_count)"
    if $dialog_ready && [[ "$active_after_focus" == "$options_id" ]] &&
       [[ "$(xdotool getwindowfocus 2>/dev/null || true)" == "$options_id" ]] &&
       screen_changed "$tabbed" "$focused" 60; then
        focus_ready=true
    fi

    if ! send_active_key Escape; then
        close_ready=false
    else
        close_ready=true
    fi
    focus_app
    capture "$closed"
    closed_count="$(window_count)"
    mapfile -t visible_after_close < <(xdotool search --onlyvisible --name '.*' 2>/dev/null || true)
    capture_shortcut_window_state "$closed_state" closed "$options_id" "$baseline_count" "$closed_count"
    if [[ -n "$options_id" ]] && window_id_in_list "$options_id" "${visible_after_close[@]}"; then
        dialog_removed=false
    else
        dialog_removed=true
    fi
    if [[ "$closed_count" -eq "$baseline_count" ]]; then
        count_restored=true
    fi
    if active_window_is_owner && [[ "$(xdotool getwindowfocus 2>/dev/null || true)" == "$window_id" ]]; then
        owner_restored=true
    fi
    if screen_matches "$before" "$closed" 500; then
        screen_restored=true
    fi
    {
        printf 'owner-window-id=%s\n' "$window_id"
        printf 'backstage-window-id=%s\n' "$backstage_id"
        printf 'options-window-id=%s\n' "$options_id"
        printf 'backstage-geometry=%s,%s %sx%s\n' "$backstage_x" "$backstage_y" "$backstage_width" "$backstage_height"
        printf 'options-click=%s,%s\n' "$options_click_x" "$options_click_y"
        printf 'trigger-ready=%s\n' "$trigger_ready"
        printf 'backstage-open=%s\n' "$backstage_ready"
        printf 'options-pane-open=%s\n' "$pane_ready"
        printf 'options-dialog-open=%s\n' "$dialog_ready"
        printf 'tab-navigation=%s\n' "$tab_ready"
        printf 'focus-retained=%s\n' "$focus_ready"
        printf 'close-key-ready=%s\n' "$close_ready"
        printf 'dialog-removed=%s\n' "$dialog_removed"
        printf 'window-count-restored=%s\n' "$count_restored"
        printf 'owner-restored=%s\n' "$owner_restored"
        printf 'screen-restored-to-before=%s\n' "$screen_restored"
    } > "$output/$proof"
    if $trigger_ready && $backstage_ready && $pane_ready && $dialog_ready; then
        record_evidence_set "options-open" "passed" \
            "Physical File navigation opened the real Backstage Options pane and its Edit options action opened a focused top-level Options dialog." \
            "$proof" "$before" "$backstage_open" "$pane_open" "$dialog_open" "$before_state" "$backstage_state" "$pane_state" "$dialog_state"
    else
        record_evidence_set "options-open" "failed" \
            "Physical File navigation did not prove the real Backstage Options pane and focused Options dialog." \
            "$proof" "$before" "$backstage_open" "$pane_open" "$dialog_open" "$before_state" "$backstage_state" "$pane_state" "$dialog_state"
    fi
    if $tab_ready; then
        record "options-tab-navigation" "passed" "$proof" \
            "Ctrl+Tab physical input changed the real Options dialog tab while retaining the dialog as the active window."
    else
        record "options-tab-navigation" "failed" "$proof" \
            "Physical Ctrl+Tab input did not prove a real Options tab transition with dialog focus retained."
    fi
    if $focus_ready; then
        record "options-focus" "passed" "$proof" \
            "A physical Tab input changed the real Options dialog focus state while active focus remained on the dialog window."
    else
        record "options-focus" "failed" "$proof" \
            "Physical Tab input did not prove focus retention and a visible focus transition in the Options dialog."
    fi
    if $close_ready && $dialog_removed && $count_restored && $owner_restored && $screen_restored; then
        record "options-close" "passed" "$proof" \
            "Escape physically closed Options and restored the owner window, window count, focus, and pre-open screen."
    else
        record "options-close" "failed" "$proof" \
            "Escape did not prove Options removal and exact owner restoration."
    fi
}

run_dirty_new_prompt_probe() {
    local expected="$output/editor-expected-sentinel.txt"
    local cancel_clipboard="$output/file-new-shortcut-cancel-clipboard.txt"
    local cancel_clipboard_error="$output/file-new-shortcut-cancel-clipboard-error.txt"
    local marker_source="$output/file-new-shortcut-empty-marker.txt"
    local empty_clipboard="$output/file-new-shortcut-empty-clipboard.txt"
    local empty_clipboard_error="$output/file-new-shortcut-empty-clipboard-error.txt"
    local marker="FreeW-new-empty-document-clipboard-marker-r1"
    local prompt_before="file-new-shortcut-prompt-before.png"
    local prompt_open="file-new-shortcut-prompt-open.png"
    local prompt_focused="file-new-shortcut-prompt-focused.png"
    local prompt_cancelled="file-new-shortcut-prompt-cancelled.png"
    local discard_before="file-new-shortcut-discard-before.png"
    local discard_open="file-new-shortcut-discard-open.png"
    local discard_focused="file-new-shortcut-discard-focused.png"
    local discard_after="file-new-shortcut-discard-after.png"
    local prompt_before_state="file-new-shortcut-prompt-before-state.txt"
    local prompt_open_state="file-new-shortcut-prompt-open-state.txt"
    local prompt_focused_state="file-new-shortcut-prompt-focused-state.txt"
    local prompt_cancelled_state="file-new-shortcut-prompt-cancelled-state.txt"
    local discard_before_state="file-new-shortcut-discard-before-state.txt"
    local discard_open_state="file-new-shortcut-discard-open-state.txt"
    local discard_focused_state="file-new-shortcut-discard-focused-state.txt"
    local discard_after_state="file-new-shortcut-discard-after-state.txt"
    local open_proof="file-new-shortcut-dirty-prompt-open-proof.txt"
    local cancel_proof="file-new-shortcut-cancel-preserves-proof.txt"
    local discard_proof="file-new-shortcut-discard-creates-clean-proof.txt"
    local prompt_baseline_count prompt_open_count prompt_cancelled_count
    local discard_baseline_count discard_open_count discard_after_count
    local prompt_active prompt_active_after_focus prompt_focus_after_focus
    local discard_active discard_active_after_focus discard_focus_after_focus
    local prompt_title="" prompt_class="" discard_title="" discard_class=""
    local prompt_trigger_ready=true prompt_focus_ready=false
    local prompt_separate=false prompt_count_increased=false prompt_screen_changed=false
    local prompt_removed=false prompt_count_restored=false prompt_owner_restored=false
    local prompt_cancel_screen_changed=false prompt_title_dirty=false
    local cancel_clipboard_ready=false cancel_clipboard_exact=false
    local discard_trigger_ready=true discard_focus_ready=false
    local discard_separate=false discard_count_increased=false discard_screen_changed=false
    local discard_removed=false discard_count_restored=false discard_owner_restored=false
    local discard_decision_sent=false marker_owner=false empty_clipboard_ready=false
    local empty_clipboard_exact=false discard_title_clean=false

    printf '%s' "$marker" > "$marker_source"
    focus_app
    send_editor_key ctrl+a || true
    send_editor_key ctrl+c || true
    focus_app
    mapfile -t file_lifecycle_before_ids < <(xdotool search --onlyvisible --name '.*' 2>/dev/null || true)
    prompt_baseline_count="$(window_count)"
    capture "$prompt_before"
    capture_shortcut_window_state "$prompt_before_state" before "" "$prompt_baseline_count" "$prompt_baseline_count"
    if ! send_active_key ctrl+n; then
        prompt_trigger_ready=false
    fi
    capture "$prompt_open"
    mapfile -t file_lifecycle_after_ids < <(xdotool search --onlyvisible --name '.*' 2>/dev/null || true)
    prompt_open_count="$(window_count)"
    prompt_active="$(xdotool getactivewindow 2>/dev/null || true)"
    find_new_shortcut_window "$prompt_active" "${file_lifecycle_after_ids[@]}" || true
    if [[ -n "$file_shortcut_window_id" ]]; then
        prompt_title="$(xdotool getwindowname "$file_shortcut_window_id" 2>/dev/null || true)"
        prompt_class="$(xprop -id "$file_shortcut_window_id" WM_CLASS 2>/dev/null || true)"
        if [[ "$file_shortcut_window_id" != "$window_id" ]] &&
           ! window_id_in_list "$file_shortcut_window_id" "${file_lifecycle_before_ids[@]}"; then
            prompt_separate=true
        fi
    fi
    if (( prompt_open_count > prompt_baseline_count )); then
        prompt_count_increased=true
    fi
    capture_shortcut_window_state "$prompt_open_state" open "$file_shortcut_window_id" \
        "$prompt_baseline_count" "$prompt_open_count"
    if [[ -n "$file_shortcut_window_id" ]] &&
       timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
           xdotool windowactivate --sync "$file_shortcut_window_id" 2>/dev/null &&
       timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
           xdotool windowfocus "$file_shortcut_window_id" 2>/dev/null; then
        sleep 0.12
    fi
    prompt_active_after_focus="$(xdotool getactivewindow 2>/dev/null || true)"
    prompt_focus_after_focus="$(xdotool getwindowfocus 2>/dev/null || true)"
    if [[ -n "$file_shortcut_window_id" &&
          "$prompt_active_after_focus" == "$file_shortcut_window_id" &&
          "$prompt_focus_after_focus" == "$file_shortcut_window_id" ]]; then
        prompt_focus_ready=true
    fi
    capture "$prompt_focused"
    capture_shortcut_window_state "$prompt_focused_state" focused "$file_shortcut_window_id" \
        "$prompt_baseline_count" "$prompt_open_count"
    if screen_changed "$output/$prompt_before" "$output/$prompt_open" 200; then
        prompt_screen_changed=true
    fi
    {
        printf 'expected-sentinel-file=%s\n' "$expected"
        printf 'shortcut=ctrl+n\n'
        printf 'before-screenshot=%s\n' "$prompt_before"
        printf 'open-screenshot=%s\n' "$prompt_open"
        printf 'focused-screenshot=%s\n' "$prompt_focused"
        printf 'before-state=%s\n' "$prompt_before_state"
        printf 'open-state=%s\n' "$prompt_open_state"
        printf 'focused-state=%s\n' "$prompt_focused_state"
        printf 'candidate-window-id=%s\n' "$file_shortcut_window_id"
        printf 'candidate-title=%s\n' "$prompt_title"
        printf 'candidate-class=%s\n' "$prompt_class"
        printf 'active-on-open=%s\n' "$prompt_active"
        printf 'active-after-focus=%s\n' "$prompt_active_after_focus"
        printf 'focus-after-focus=%s\n' "$prompt_focus_after_focus"
        printf 'baseline-window-count=%s\n' "$prompt_baseline_count"
        printf 'open-window-count=%s\n' "$prompt_open_count"
        printf 'trigger-ready=%s\n' "$prompt_trigger_ready"
        printf 'separate-window=%s\n' "$prompt_separate"
        printf 'window-count-increased=%s\n' "$prompt_count_increased"
        printf 'active-and-focused=%s\n' "$prompt_focus_ready"
        printf 'open-screenshot-changed=%s\n' "$prompt_screen_changed"
    } > "$output/$open_proof"
    if $prompt_trigger_ready && $prompt_separate && $prompt_count_increased &&
       [[ -n "$prompt_title" ]] &&
       $prompt_focus_ready && $prompt_screen_changed; then
        record_evidence_set "file-new-shortcut-dirty-prompt-open" "passed" \
            "Ctrl+N on the dirty sentinel opened a separate real Save Changes top-level window with title, optional WM_CLASS capture, increased count, active focus, and screenshot transition." \
            "$open_proof" "$prompt_before" "$prompt_open" "$prompt_focused" \
            "$prompt_before_state" "$prompt_open_state" "$prompt_focused_state"
    else
        record_evidence_set "file-new-shortcut-dirty-prompt-open" "failed" \
            "Ctrl+N did not prove the real dirty Save Changes top-level window with the required physical evidence." \
            "$open_proof" "$prompt_before" "$prompt_open" "$prompt_focused" \
            "$prompt_before_state" "$prompt_open_state" "$prompt_focused_state"
    fi

    if ! send_active_key Escape; then
        prompt_trigger_ready=false
    fi
    focus_app
    capture "$prompt_cancelled"
    mapfile -t file_lifecycle_cancelled_ids < <(xdotool search --onlyvisible --name '.*' 2>/dev/null || true)
    prompt_cancelled_count="$(window_count)"
    capture_shortcut_window_state "$prompt_cancelled_state" cancelled "$file_shortcut_window_id" \
        "$prompt_baseline_count" "$prompt_cancelled_count"
    if [[ -n "$file_shortcut_window_id" ]] &&
       window_id_in_list "$file_shortcut_window_id" "${file_lifecycle_cancelled_ids[@]}"; then
        prompt_removed=false
    else
        prompt_removed=true
    fi
    if [[ "$prompt_cancelled_count" -eq "$prompt_baseline_count" ]]; then
        prompt_count_restored=true
    fi
    if active_window_is_owner &&
       [[ "$(xdotool getwindowfocus 2>/dev/null || true)" == "$window_id" ]]; then
        prompt_owner_restored=true
    fi
    if screen_changed "$output/$prompt_open" "$output/$prompt_cancelled" 100; then
        prompt_cancel_screen_changed=true
    fi
    send_editor_key ctrl+a || true
    send_editor_key ctrl+c || true
    if read_clipboard_bounded "$cancel_clipboard" "$cancel_clipboard_error"; then
        cancel_clipboard_ready=true
    fi
    if $cancel_clipboard_ready && cmp -s "$expected" "$cancel_clipboard"; then
        cancel_clipboard_exact=true
    fi
    if [[ "$(xdotool getwindowname "$window_id" 2>/dev/null || true)" == *"*"* ]]; then
        prompt_title_dirty=true
    fi
    {
        printf 'open-proof=%s\n' "$open_proof"
        printf 'cancel-screenshot=%s\n' "$prompt_cancelled"
        printf 'cancel-state=%s\n' "$prompt_cancelled_state"
        printf 'cancel-clipboard=%s\n' "$cancel_clipboard"
        printf 'cancel-clipboard-error=%s\n' "$cancel_clipboard_error"
        printf 'expected-sentinel=%s\n' "$expected"
        printf 'cancel-clipboard-ready=%s\n' "$cancel_clipboard_ready"
        printf 'cancel-clipboard-exact=%s\n' "$cancel_clipboard_exact"
        printf 'prompt-removed=%s\n' "$prompt_removed"
        printf 'window-count-restored=%s\n' "$prompt_count_restored"
        printf 'owner-restored=%s\n' "$prompt_owner_restored"
        printf 'cancel-screenshot-changed=%s\n' "$prompt_cancel_screen_changed"
        printf 'owner-title-still-dirty=%s\n' "$prompt_title_dirty"
        printf 'observed-cancel-clipboard='; if $cancel_clipboard_ready; then cat "$cancel_clipboard"; fi; printf '\n'
    } > "$output/$cancel_proof"
    if $prompt_trigger_ready && $prompt_removed && $prompt_count_restored && $prompt_owner_restored &&
       $prompt_cancel_screen_changed && $cancel_clipboard_exact && $prompt_title_dirty; then
        record_evidence_set "file-new-shortcut-cancel-preserves" "passed" \
            "Escape cancelled the dirty New prompt, restored the owner, kept the dirty title, and preserved the exact sentinel through select-all/copy." \
            "$cancel_proof" "$prompt_before" "$prompt_open" "$prompt_cancelled" \
            "$prompt_open_state" "$prompt_cancelled_state" "editor-expected-sentinel.txt" "file-new-shortcut-cancel-clipboard.txt"
    else
        record_evidence_set "file-new-shortcut-cancel-preserves" "failed" \
            "Dirty New cancellation did not prove exact sentinel preservation and owner restoration." \
            "$cancel_proof" "$prompt_before" "$prompt_open" "$prompt_cancelled" \
            "$prompt_open_state" "$prompt_cancelled_state" "editor-expected-sentinel.txt" "file-new-shortcut-cancel-clipboard.txt"
    fi

    focus_app
    mapfile -t file_lifecycle_before_ids < <(xdotool search --onlyvisible --name '.*' 2>/dev/null || true)
    discard_baseline_count="$(window_count)"
    capture "$discard_before"
    capture_shortcut_window_state "$discard_before_state" before "" "$discard_baseline_count" "$discard_baseline_count"
    if ! send_active_key ctrl+n; then
        discard_trigger_ready=false
    fi
    capture "$discard_open"
    mapfile -t file_lifecycle_after_ids < <(xdotool search --onlyvisible --name '.*' 2>/dev/null || true)
    discard_open_count="$(window_count)"
    discard_active="$(xdotool getactivewindow 2>/dev/null || true)"
    find_new_shortcut_window "$discard_active" "${file_lifecycle_after_ids[@]}" || true
    if [[ -n "$file_shortcut_window_id" ]]; then
        discard_title="$(xdotool getwindowname "$file_shortcut_window_id" 2>/dev/null || true)"
        discard_class="$(xprop -id "$file_shortcut_window_id" WM_CLASS 2>/dev/null || true)"
        if [[ "$file_shortcut_window_id" != "$window_id" ]] &&
           ! window_id_in_list "$file_shortcut_window_id" "${file_lifecycle_before_ids[@]}"; then
            discard_separate=true
        fi
    fi
    if (( discard_open_count > discard_baseline_count )); then
        discard_count_increased=true
    fi
    capture_shortcut_window_state "$discard_open_state" open "$file_shortcut_window_id" \
        "$discard_baseline_count" "$discard_open_count"
    if [[ -n "$file_shortcut_window_id" ]] &&
       timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
           xdotool windowactivate --sync "$file_shortcut_window_id" 2>/dev/null &&
       timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
           xdotool windowfocus "$file_shortcut_window_id" 2>/dev/null; then
        sleep 0.12
    fi
    discard_active_after_focus="$(xdotool getactivewindow 2>/dev/null || true)"
    discard_focus_after_focus="$(xdotool getwindowfocus 2>/dev/null || true)"
    if [[ -n "$file_shortcut_window_id" &&
          "$discard_active_after_focus" == "$file_shortcut_window_id" &&
          "$discard_focus_after_focus" == "$file_shortcut_window_id" ]]; then
        discard_focus_ready=true
    fi
    if start_clipboard_owner "$marker_source" "$output/file-new-shortcut-empty-marker-owner-error.txt"; then
        marker_owner=true
    fi
    if ! send_active_key Tab; then
        discard_trigger_ready=false
    fi
    capture "$discard_focused"
    capture_shortcut_window_state "$discard_focused_state" focused "$file_shortcut_window_id" \
        "$discard_baseline_count" "$discard_open_count"
    if ! send_active_key Return; then
        discard_trigger_ready=false
    else
        discard_decision_sent=true
    fi
    focus_app
    capture "$discard_after"
    mapfile -t file_lifecycle_discarded_ids < <(xdotool search --onlyvisible --name '.*' 2>/dev/null || true)
    discard_after_count="$(window_count)"
    capture_shortcut_window_state "$discard_after_state" after "$file_shortcut_window_id" \
        "$discard_baseline_count" "$discard_after_count"
    if [[ -n "$file_shortcut_window_id" ]] &&
       window_id_in_list "$file_shortcut_window_id" "${file_lifecycle_discarded_ids[@]}"; then
        discard_removed=false
    else
        discard_removed=true
    fi
    if [[ "$discard_after_count" -eq "$discard_baseline_count" ]]; then
        discard_count_restored=true
    fi
    if active_window_is_owner &&
       [[ "$(xdotool getwindowfocus 2>/dev/null || true)" == "$window_id" ]]; then
        discard_owner_restored=true
    fi
    if screen_changed "$output/$discard_before" "$output/$discard_after" 200; then
        discard_screen_changed=true
    fi
    send_editor_key ctrl+a || true
    send_editor_key ctrl+c || true
    if read_clipboard_bounded "$empty_clipboard" "$empty_clipboard_error"; then
        empty_clipboard_ready=true
    fi
    if $empty_clipboard_ready && cmp -s "$marker_source" "$empty_clipboard"; then
        empty_clipboard_exact=true
    fi
    if [[ "$(xdotool getwindowname "$window_id" 2>/dev/null || true)" != *"*"* &&
          "$(xdotool getwindowname "$window_id" 2>/dev/null || true)" == *"FreeW"* ]]; then
        discard_title_clean=true
    fi
    if $marker_owner; then
        stop_clipboard_owner
    fi
    {
        printf 'discard-before=%s\n' "$discard_before"
        printf 'discard-open=%s\n' "$discard_open"
        printf 'discard-focused-after-tab=%s\n' "$discard_focused"
        printf 'discard-after=%s\n' "$discard_after"
        printf 'discard-before-state=%s\n' "$discard_before_state"
        printf 'discard-open-state=%s\n' "$discard_open_state"
        printf 'discard-focused-state=%s\n' "$discard_focused_state"
        printf 'discard-after-state=%s\n' "$discard_after_state"
        printf 'candidate-window-id=%s\n' "$file_shortcut_window_id"
        printf 'candidate-title=%s\n' "$discard_title"
        printf 'candidate-class=%s\n' "$discard_class"
        printf 'active-on-open=%s\n' "$discard_active"
        printf 'active-after-focus=%s\n' "$discard_active_after_focus"
        printf 'focus-after-focus=%s\n' "$discard_focus_after_focus"
        printf 'baseline-window-count=%s\n' "$discard_baseline_count"
        printf 'open-window-count=%s\n' "$discard_open_count"
        printf 'after-window-count=%s\n' "$discard_after_count"
        printf 'trigger-ready=%s\n' "$discard_trigger_ready"
        printf 'keyboard-navigation=Tab-then-Return\n'
        printf 'dont-save-decision-sent=%s\n' "$discard_decision_sent"
        printf 'separate-window=%s\n' "$discard_separate"
        printf 'window-count-increased=%s\n' "$discard_count_increased"
        printf 'active-and-focused=%s\n' "$discard_focus_ready"
        printf 'prompt-removed=%s\n' "$discard_removed"
        printf 'window-count-restored=%s\n' "$discard_count_restored"
        printf 'owner-restored=%s\n' "$discard_owner_restored"
        printf 'discard-screenshot-changed=%s\n' "$discard_screen_changed"
        printf 'empty-marker=%s\n' "$marker"
        printf 'empty-clipboard=%s\n' "$empty_clipboard"
        printf 'empty-clipboard-ready=%s\n' "$empty_clipboard_ready"
        printf 'empty-clipboard-exact=%s\n' "$empty_clipboard_exact"
        printf 'clean-title=%s\n' "$discard_title_clean"
        printf 'observed-empty-clipboard='; if $empty_clipboard_ready; then cat "$empty_clipboard"; fi; printf '\n'
    } > "$output/$discard_proof"
    if $discard_trigger_ready && $discard_decision_sent && $discard_separate &&
       $discard_count_increased && $discard_focus_ready && $discard_removed &&
       $discard_count_restored && $discard_owner_restored && $discard_screen_changed &&
       $empty_clipboard_exact && $discard_title_clean; then
        record_evidence_set "file-new-shortcut-discard-creates-clean" "passed" \
            "Ctrl+N was repeated, Don't save was selected by physical Tab/Return navigation, and the removed prompt left an owner-focused clean empty document proven by an unchanged exact clipboard marker." \
            "$discard_proof" "$discard_before" "$discard_open" "$discard_focused" "$discard_after" \
            "$discard_open_state" "$discard_focused_state" "$discard_after_state" "file-new-shortcut-empty-marker.txt" "file-new-shortcut-empty-clipboard.txt"
    else
        record_evidence_set "file-new-shortcut-discard-creates-clean" "failed" \
            "The physical Don't save path did not prove prompt removal, owner restoration, clean title, and an empty document." \
            "$discard_proof" "$discard_before" "$discard_open" "$discard_focused" "$discard_after" \
            "$discard_open_state" "$discard_focused_state" "$discard_after_state" "file-new-shortcut-empty-marker.txt" "file-new-shortcut-empty-clipboard.txt"
    fi
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

if [[ "$app" == "FreeP" ]]; then
    capture "nested-keytip-before-target.png"
    send_key Alt_L
    send_key n
    send_key t
    send_key x
    capture "nested-keytip-target-selected.png"

    send_key Alt_L
    send_key a
    send_key n
    send_key b
    capture "nested-keytip-prefix-b.png"
    prefix_deferred=false
    if screen_changed \
        "$output/nested-keytip-target-selected.png" \
        "$output/nested-keytip-prefix-b.png" 100; then
        prefix_deferred=true
    fi

    send_key i
    capture "nested-keytip-blinds-menu.png"
    longer_tip_opened=false
    if screen_changed \
        "$output/nested-keytip-prefix-b.png" \
        "$output/nested-keytip-blinds-menu.png" 160; then
        longer_tip_opened=true
    fi

    send_active_key Escape
    focus_app
    capture "nested-keytip-dismissed.png"
    menu_dismissed=false
    if screen_changed \
        "$output/nested-keytip-blinds-menu.png" \
        "$output/nested-keytip-dismissed.png" 160; then
        menu_dismissed=true
    fi

    send_key Escape
    capture "nested-keytip-neutral.png"
    keytips_dismissed=false
    if screen_changed \
        "$output/nested-keytip-dismissed.png" \
        "$output/nested-keytip-neutral.png" 100; then
        keytips_dismissed=true
    fi

    if $prefix_deferred && $longer_tip_opened && $menu_dismissed && $keytips_dismissed; then
        record_evidence_set "nested-keytip-prefix-deferral" "passed" \
            "Physical Alt,N,T,X selected an inserted text box; Alt,A,N,B kept the longer BI sequence alive, I opened the Blinds menu, and two Escape presses dismissed the menu and key-tip mode." \
            "nested-keytip-before-target.png" "nested-keytip-target-selected.png" \
            "nested-keytip-prefix-b.png" "nested-keytip-blinds-menu.png" \
            "nested-keytip-dismissed.png" "nested-keytip-neutral.png"
    else
        record_evidence_set "nested-keytip-prefix-deferral" "failed" \
            "The FreeP physical key-tip route did not prove B prefix deferral, BI menu opening, menu dismissal, and return to a neutral key-tip state." \
            "nested-keytip-before-target.png" "nested-keytip-target-selected.png" \
            "nested-keytip-prefix-b.png" "nested-keytip-blinds-menu.png" \
            "nested-keytip-dismissed.png" "nested-keytip-neutral.png"
    fi
fi

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

# FreeW-only clean-document file shortcut lifecycle evidence. Run this before
# the sentinel edit below so the picker and direct-print rows observe a clean,
# untitled document and Ctrl+S is proven to delegate to Save As.
if [[ "$app" == "FreeW" ]]; then
    run_file_shortcut_window_lifecycle \
        "file-open-shortcut-dialog" ctrl+o "Ctrl+O Open"
    run_file_shortcut_window_lifecycle \
        "file-save-shortcut-dialog" ctrl+s "Ctrl+S Save As for clean Untitled"
    run_file_shortcut_window_lifecycle \
        "file-save-as-shortcut-dialog" ctrl+shift+s "Ctrl+Shift+S Save As"
    run_file_shortcut_window_lifecycle \
        "file-print-shortcut-dialog" ctrl+p "Ctrl+P Print"
    run_backstage_pane_lifecycle "backstage-print" 10 "Print"
    run_backstage_pane_lifecycle "backstage-export" 11 "Export"
    run_options_lifecycle
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
    cut_document_ae="$(screen_difference "$output/editor-before-cut-document.png" "$output/editor-after-cut-document.png")"
    cut_undo_ae="$(screen_difference "$output/editor-after-cut-document.png" "$output/editor-after-cut-undo-document.png")"
    cut_restore_ae="$(screen_difference "$output/editor-before-cut-document.png" "$output/editor-after-cut-undo-document.png")"
    cut_restore_threshold=500
    cut_document_transition=false
    cut_undo_transition=false
    cut_document_restored=false
    if [[ "$cut_document_ae" =~ ^[0-9]+$ ]] && (( cut_document_ae >= 100 )); then
        cut_document_transition=true
    fi
    if [[ "$cut_undo_ae" =~ ^[0-9]+$ ]] && (( cut_undo_ae >= 100 )); then
        cut_undo_transition=true
    fi
    if [[ "$cut_restore_ae" =~ ^[0-9]+$ ]] && (( cut_restore_ae <= cut_restore_threshold )); then
        cut_document_restored=true
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
        printf 'cut-document-ae=%s\n' "$cut_document_ae"
        printf 'undo-document-ae=%s\n' "$cut_undo_ae"
        printf 'undo-document-restored-ae=%s\n' "$cut_restore_ae"
        printf 'undo-document-restoration-threshold-ae=%s\n' "$cut_restore_threshold"
        printf 'cut-document-transition=%s\n' "$cut_document_transition"
        printf 'undo-document-transition=%s\n' "$cut_undo_transition"
        printf 'undo-document-restored=%s\n' "$cut_document_restored"
    } > "$cut_proof"
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
        local before="${id_prefix}-before.png" open="${id_prefix}-open.png" focused="${id_prefix}-focused.png"
        local typed="${id_prefix}-typed.png" entered="${id_prefix}-entered.png"
        local dismissed="${id_prefix}-dismissed.png"
        local before_state="${id_prefix}-before-state.txt" open_state="${id_prefix}-open-state.txt"
        local focused_state="${id_prefix}-focused-state.txt" dismissed_state="${id_prefix}-dismissed-state.txt"
        local expected="${id_prefix}-expected.txt" clipboard="${id_prefix}-clipboard.txt"
        local proof="${id_prefix}-proof.txt"
        local trigger_ready=true typed_ready=false clipboard_ready=false entry_ready=false focus_ready=false
        local baseline_count open_count dismissed_count active_window_id active_after_focus="" dialog_window=""
        printf '%s' "$marker" > "$output/$expected"

        focus_app
        click_pointer 1 "$editor_x" "$editor_y"
        baseline_count="$(window_count)"
        capture "$before"
        capture_window_state "$before_state"
        if ! send_active_key "$key"; then
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
        if [[ -n "$dialog_window" ]]; then
            if timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
                xdotool windowactivate --sync "$dialog_window" 2>/dev/null &&
               timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
                xdotool windowfocus "$dialog_window" 2>/dev/null; then
                sleep 0.12
                active_after_focus="$(xdotool getactivewindow 2>/dev/null || true)"
                if [[ "$active_after_focus" == "$dialog_window" ]]; then
                    focus_ready=true
                fi
            fi
        fi
        capture "$focused"
        capture_window_state "$focused_state"

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
            printf 'focused-screenshot=%s\n' "$focused"
            printf 'typed-screenshot=%s\n' "$typed"
            printf 'entered-screenshot=%s\n' "$entered"
            printf 'active-window=%s\n' "$active_window_id"
            printf 'active-after-focus=%s\n' "$active_after_focus"
            printf 'find-replace-window=%s\n' "$dialog_window"
            printf 'baseline-window-count=%s\n' "$baseline_count"
            printf 'open-window-count=%s\n' "$open_count"
            printf 'trigger-ready=%s\n' "$trigger_ready"
            printf 'dialog-focus-ready=%s\n' "$focus_ready"
            printf 'typed-ready=%s\n' "$typed_ready"
            printf 'clipboard-ready=%s\n' "$clipboard_ready"
            printf 'clipboard-exact='; if $clipboard_ready && cmp -s "$output/$expected" "$output/$clipboard"; then printf 'true\n'; else printf 'false\n'; fi
            printf 'route-entry-transition=%s\n' "$entry_ready"
            printf 'separate-window='; if [[ -n "$dialog_window" && "$dialog_window" != "$window_id" && "$open_count" -gt "$baseline_count" ]]; then printf 'true\n'; else printf 'false\n'; fi
            printf 'open-screenshot-changed='; if screen_changed "$output/$before" "$output/$open" 200; then printf 'true\n'; else printf 'false\n'; fi
        } > "$output/$proof"
        if $trigger_ready && $focus_ready && $typed_ready && $clipboard_ready && $entry_ready &&
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
            printf 'focused-state=%s\n' "$focused_state"
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
    run_dirty_new_prompt_probe

    autocorrect_input="$output/editor-autocorrect-input.txt"
    autocorrect_expected="$output/editor-autocorrect-expected.txt"
    autocorrect_observed="$output/editor-autocorrect-observed.txt"
    autocorrect_error="$output/editor-autocorrect-error.txt"
    autocorrect_proof="$output/editor-autocorrect-proof.txt"
    printf '%s' 'I teh ' > "$autocorrect_input"
    printf '%s' 'I the ' > "$autocorrect_expected"
    click_pointer 1 "$editor_x" "$editor_y"
    send_editor_key ctrl+a
    send_editor_key Delete
    autocorrect_typed=false
    if send_active_text 'I teh '; then
        autocorrect_typed=true
    fi
    capture "editor-autocorrect-typed.png"
    send_editor_key ctrl+a
    send_editor_key ctrl+c
    if read_clipboard_bounded "$autocorrect_observed" "$autocorrect_error"; then
        autocorrect_clipboard_ready=true
    else
        autocorrect_clipboard_ready=false
    fi
    {
        printf 'input='; cat "$autocorrect_input"; printf '\n'
        printf 'expected='; cat "$autocorrect_expected"; printf '\n'
        printf 'observed='; if $autocorrect_clipboard_ready; then cat "$autocorrect_observed"; fi; printf '\n'
        printf 'typed=%s\n' "$autocorrect_typed"
        if $autocorrect_clipboard_ready && cmp -s "$autocorrect_expected" "$autocorrect_observed"; then
            printf 'exact-match=true\n'
        else
            printf 'exact-match=false\n'
        fi
    } > "$autocorrect_proof"
    if $autocorrect_typed && $autocorrect_clipboard_ready &&
       cmp -s "$autocorrect_expected" "$autocorrect_observed"; then
        record_evidence_set "editor-autocorrect-typing" "passed" \
            "Physical X11 typing applied the shared AutoCorrect replacement and produced exact clipboard text." \
            "editor-autocorrect-proof.txt" "editor-autocorrect-input.txt" \
            "editor-autocorrect-expected.txt" "editor-autocorrect-observed.txt" \
            "editor-autocorrect-typed.png"
    else
        record_evidence_set "editor-autocorrect-typing" "failed" \
            "Physical X11 typing did not produce the WPF-authoritative AutoCorrect text." \
            "editor-autocorrect-proof.txt" "editor-autocorrect-input.txt" \
            "editor-autocorrect-expected.txt" "editor-autocorrect-observed.txt" \
            "editor-autocorrect-error.txt" "editor-autocorrect-typed.png"
    fi
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

    # FreeP-only physical Animation Pane evidence. The app receives an explicit
    # harness seed so this route starts with one real shape animation while all
    # pane state changes below are driven through the ribbon and X11 pointer.
    focus_app
    click_pointer 1 "$slide_thumbnail_x" "$slide_thumbnail_y"
    send_active_key Escape || true
    geometry="$(xdotool getwindowgeometry --shell "$window_id" 2>/dev/null || true)"
    eval "$geometry"
    pane_width=250
    # scrot -o captures the focused window, so image crops use window-local
    # coordinates while xdotool input continues to use root-screen coordinates.
    pane_top_offset=174
    pane_bottom_offset=67
    pane_top=$pane_top_offset
    pane_height=$(( HEIGHT - pane_top_offset - pane_bottom_offset ))
    pane_geometry="${pane_width}x${pane_height}+$((WIDTH - pane_width))+$pane_top_offset"
    # Target the order/name portion of the row rather than its trigger ComboBox;
    # this keeps the pane's row-selection handler as the pointer target.
    pane_row_x=$(( X + WIDTH - pane_width + 55 ))
    # scrot -o retains the full X11 root-screen coordinates (the screenshots are
    # 1280x820), while xdotool also expects root-screen coordinates. The pane's
    # row therefore uses its calibrated screenshot position directly; adding the
    # decorated window Y would place the click 38px below the rendered row.
    pane_row_y=$(( pane_top_offset + 58 ))
    # The 1280px family harness renders Advanced Animation immediately after the
    # Timing group. Target the command's blank/icon column so child label content
    # cannot consume the press without bubbling to the split button.
    pane_group_x=$(( X + WIDTH - 192 ))
    pane_group_y=$(( Y + 90 ))
    pane_menu_x=$(( X + WIDTH - 192 ))
    # The one-item flyout is rendered below the Advanced Animation command. scrot's
    # focused-window image starts at app-local zero, so its visible center is
    # the app-relative offset 150 (root-screen Y + 150 for xdotool here).
    pane_menu_y=$(( Y + 150 ))
    pane_header_geometry="${pane_width}x34+$((WIDTH - pane_width))+$pane_top_offset"
    pane_row_geometry="${pane_width}x54+$((WIDTH - pane_width))+$((pane_top_offset + 34))"
    {
        printf 'window-geometry=%s\n' "$geometry"
        printf 'pane-geometry=%s\n' "$pane_geometry"
        printf 'pane-row-point=%s,%s\n' "$pane_row_x" "$pane_row_y"
        printf 'pane-group-point=%s,%s\n' "$pane_group_x" "$pane_group_y"
        printf 'pane-menu-point=%s,%s\n' "$pane_menu_x" "$pane_menu_y"
        printf 'pane-header-geometry=%s\n' "$pane_header_geometry"
        printf 'pane-row-geometry=%s\n' "$pane_row_geometry"
        printf 'seed=FREEP_PHYSICAL_ANIMATION_PANE_SEED=1\n'
        printf 'open-route=physical pointer click on Advanced Animation command then flyout item\n'
        printf 'interaction=pointer row selection plus ribbon close/reopen\n'
    } > "$output/animation-pane-calibration.txt"

    capture "animation-pane-before.png"
    capture_region "animation-pane-before.png" "animation-pane-before-region.png" "$pane_geometry"
    click_pointer 1 "$pane_group_x" "$pane_group_y"
    capture "animation-pane-command-menu-open.png"
    click_pointer 1 "$pane_menu_x" "$pane_menu_y"
    capture "animation-pane-open.png"
    capture_region "animation-pane-open.png" "animation-pane-open-region.png" "$pane_geometry"
    pane_opened=false
    pane_header_pixels="$(image_color_count "$output/animation-pane-open.png" "$pane_header_geometry" '#B7472A')"
    if (( pane_header_pixels >= 500 )); then
        pane_opened=true
    fi
    pane_row_visible=false
    pane_row_pixels="$(image_color_count "$output/animation-pane-open.png" "$pane_row_geometry" '#FAFAFA')"
    if (( pane_row_pixels >= 500 )); then
        pane_row_visible=true
    fi

    click_pointer 1 "$pane_row_x" "$pane_row_y"
    capture "animation-pane-row-selected.png"
    capture_region "animation-pane-row-selected.png" "animation-pane-row-selected-region.png" "$pane_geometry"
    row_selected=false
    selected_row_pixels="$(image_color_count "$output/animation-pane-row-selected.png" "$pane_row_geometry" '#FFE0D6')"
    if (( selected_row_pixels >= 500 )); then
        row_selected=true
    fi

    click_pointer 1 "$pane_group_x" "$pane_group_y"
    click_pointer 1 "$pane_menu_x" "$pane_menu_y"
    capture "animation-pane-closed.png"
    capture_region "animation-pane-closed.png" "animation-pane-closed-region.png" "$pane_geometry"
    pane_closed=false
    closed_header_pixels="$(image_color_count "$output/animation-pane-closed.png" "$pane_header_geometry" '#B7472A')"
    closed_row_pixels="$(image_color_count "$output/animation-pane-closed.png" "$pane_row_geometry" '#FFE0D6')"
    if (( closed_header_pixels == 0 && closed_row_pixels == 0 )); then
        pane_closed=true
    fi

    click_pointer 1 "$pane_group_x" "$pane_group_y"
    click_pointer 1 "$pane_menu_x" "$pane_menu_y"
    capture "animation-pane-reopened.png"
    capture_region "animation-pane-reopened.png" "animation-pane-reopened-region.png" "$pane_geometry"
    pane_reopened=false
    reopened_header_pixels="$(image_color_count "$output/animation-pane-reopened.png" "$pane_header_geometry" '#B7472A')"
    reopened_row_pixels="$(image_color_count "$output/animation-pane-reopened.png" "$pane_row_geometry" '#FAFAFA')"
    reopened_selected_row_pixels="$(image_color_count "$output/animation-pane-reopened.png" "$pane_row_geometry" '#FFE0D6')"
    if (( reopened_header_pixels >= 500 && (reopened_row_pixels >= 500 || reopened_selected_row_pixels >= 500) )); then
        pane_reopened=true
    fi
    {
        printf 'pane-opened=%s\n' "$pane_opened"
        printf 'pane-row-visible=%s\n' "$pane_row_visible"
        printf 'row-selected=%s\n' "$row_selected"
        printf 'pane-closed=%s\n' "$pane_closed"
        printf 'pane-reopened=%s\n' "$pane_reopened"
        printf 'pane-header-pixels=%s\n' "$pane_header_pixels"
        printf 'pane-row-pixels=%s\n' "$pane_row_pixels"
        printf 'selected-row-pixels=%s\n' "$selected_row_pixels"
        printf 'closed-header-pixels=%s\n' "$closed_header_pixels"
        printf 'closed-selected-row-pixels=%s\n' "$closed_row_pixels"
        printf 'reopened-header-pixels=%s\n' "$reopened_header_pixels"
        printf 'reopened-row-pixels=%s\n' "$reopened_row_pixels"
        printf 'reopened-selected-row-pixels=%s\n' "$reopened_selected_row_pixels"
        printf 'before-region=animation-pane-before-region.png\n'
        printf 'open-region=animation-pane-open-region.png\n'
        printf 'row-selected-region=animation-pane-row-selected-region.png\n'
        printf 'closed-region=animation-pane-closed-region.png\n'
        printf 'reopened-region=animation-pane-reopened-region.png\n'
        printf 'observable-physical-workflow=%s\n' "$($pane_opened && $pane_row_visible && $row_selected && $pane_closed && $pane_reopened && echo true || echo false)"
    } > "$output/animation-pane-physical-workflow-proof.txt"
    if $pane_opened && $pane_row_visible && $row_selected && $pane_closed && $pane_reopened; then
        record_evidence_set "animation-pane-physical-workflow" "passed" \
            "The seeded real FreeP animation pane opened through physical clicks on the Advanced Animation command and its flyout item, exposed its animation row, changed semantic selection pixels after a physical row click, then closed and reopened through the same route with the row still visible." \
            "animation-pane-calibration.txt" "animation-pane-physical-workflow-proof.txt" \
            "animation-pane-command-menu-open.png" \
            "animation-pane-before.png" "animation-pane-open.png" "animation-pane-row-selected.png" \
            "animation-pane-closed.png" "animation-pane-reopened.png" \
            "animation-pane-open-region.png" "animation-pane-row-selected-region.png" \
            "animation-pane-closed-region.png" "animation-pane-reopened-region.png"
    else
        record_evidence_set "animation-pane-physical-workflow" "failed" \
            "The FreeP physical Animation Pane route did not prove open, visible seeded row, selection, close, and reopen postconditions." \
            "animation-pane-calibration.txt" "animation-pane-physical-workflow-proof.txt" \
            "animation-pane-command-menu-open.png" \
            "animation-pane-before.png" "animation-pane-open.png" "animation-pane-row-selected.png" \
            "animation-pane-closed.png" "animation-pane-reopened.png" \
            "animation-pane-open-region.png" "animation-pane-row-selected-region.png" \
            "animation-pane-closed-region.png" "animation-pane-reopened-region.png"
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
