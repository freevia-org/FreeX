#!/usr/bin/env bash
set -Eeuo pipefail

export DISPLAY="${DISPLAY:-:99}"

output="${1:-/work/fps}"
input_delay_ms="${FREEP_X11_INPUT_DELAY_MS:-160}"
settle_seconds="${FREEP_X11_SETTLE_SECONDS:-0.45}"
clipboard_timeout_seconds="${FREEP_X11_CLIPBOARD_TIMEOUT_SECONDS:-3}"
pointer_timeout_seconds="${FREEP_X11_POINTER_TIMEOUT_SECONDS:-3}"
document_path="${FREEP_DOCUMENT_PATH:?FREEP_DOCUMENT_PATH is required}"
expected_document_name="${FREEP_EXPECTED_DOCUMENT_NAME:?FREEP_EXPECTED_DOCUMENT_NAME is required}"
window_pattern="${FREEP_EXPECTED_WINDOW_PATTERN:-FreeP}"
screen_width="${FREEP_SCREEN_WIDTH:-1280}"
screen_height="${FREEP_SCREEN_HEIGHT:-820}"
screen_dpi="${FREEP_SCREEN_DPI:-96}"
records="$output/result-records.jsonl"
screenshots_file="$output/screenshot-names.txt"

required_ids=(
    "visible-window-discovery"
    "file-new-shortcut-lifecycle"
    "file-open-shortcut-lifecycle"
    "file-save-shortcut-current-path"
    "file-save-as-shortcut-lifecycle"
    "print-shortcut-backstage-lifecycle"
    "slideshow-from-beginning-lifecycle"
    "slideshow-from-current-lifecycle"
    "find-shortcut-lifecycle"
    "replace-shortcut-lifecycle"
)

mkdir -p "$output"
: > "$records"
: > "$screenshots_file"

record() {
    local id="$1" status="$2" note="$3"
    shift 3
    python3 - "$records" "$id" "$status" "$note" "$@" <<'PY'
import json
import sys

path, result_id, status, note, *evidence = sys.argv[1:]
row = {
    "id": result_id,
    "category": "physical-x11-file-slideshow-shortcut",
    "status": status,
    "evidenceLevel": "physical-x11-input",
    "evidence": evidence,
    "note": note,
}
with open(path, "a", encoding="utf-8") as handle:
    handle.write(json.dumps(row, ensure_ascii=False) + "\n")
PY
}

track_screenshot() { printf '%s\n' "$1" >> "$screenshots_file"; }

capture() {
    local name="$1"
    scrot -o "$output/$name"
    track_screenshot "$name"
}

capture_region() {
    local source_name="$1" name="$2" geometry="$3"
    convert "$output/$source_name" -crop "$geometry" +repage "$output/$name"
    track_screenshot "$name"
}

capture_stage() {
    local source_name="$1" name="$2"
    if [[ -n "${candidate_window_id:-}" ]] && command -v import >/dev/null 2>&1 &&
       import -window "$candidate_window_id" "$output/$name" 2>/dev/null; then
        track_screenshot "$name"
        return
    fi
    convert "$output/$source_name" -crop "${baseline_width}x${baseline_height}+0+0" +repage "$output/$name"
    track_screenshot "$name"
}

capture_window_state() {
    local name="$1"
    {
        printf 'owner-window-id=%s\n' "$owner_id"
        printf 'owner-window-title=%s\n' "$(xdotool getwindowname "$owner_id" 2>/dev/null || true)"
        printf 'active-window=%s\n' "$(xdotool getactivewindow 2>/dev/null || true)"
        printf 'focus-window=%s\n' "$(xdotool getwindowfocus 2>/dev/null || true)"
        printf 'owner-active='; [[ "$(xdotool getactivewindow 2>/dev/null || true)" == "$owner_id" ]] && printf 'true\n' || printf 'false\n'
        printf 'owner-focused='; [[ "$(xdotool getwindowfocus 2>/dev/null || true)" == "$owner_id" ]] && printf 'true\n' || printf 'false\n'
        printf 'visible-window-ids='; xdotool search --onlyvisible --name '.*' 2>/dev/null | tr '\n' ' '; printf '\n'
        printf 'wmctrl-list-begin\n'; wmctrl -l 2>/dev/null || true; printf 'wmctrl-list-end\n'
    } > "$output/$name"
}

window_ids() { xdotool search --onlyvisible --name '.*' 2>/dev/null || true; }

window_count() {
    mapfile -t current_windows < <(window_ids)
    printf '%s' "${#current_windows[@]}"
}

contains_id() {
    local wanted="$1"
    shift
    local candidate
    for candidate in "$@"; do [[ "$candidate" == "$wanted" ]] && return 0; done
    return 1
}

find_new_window() {
    local active="$1"
    shift
    local candidate
    candidate_window_id=""
    for candidate in "$@"; do
        if ! contains_id "$candidate" "${before_window_ids[@]}"; then
            if [[ "$candidate" == "$active" ]]; then
                candidate_window_id="$candidate"
                return 0
            fi
            [[ -z "$candidate_window_id" ]] && candidate_window_id="$candidate"
        fi
    done
    [[ -n "$candidate_window_id" ]]
}

focus_owner() {
    timeout --foreground --kill-after=1s "$pointer_timeout_seconds" xdotool windowactivate --sync "$owner_id" >/dev/null 2>&1 || true
    timeout --foreground --kill-after=1s "$pointer_timeout_seconds" xdotool windowfocus "$owner_id" >/dev/null 2>&1 || true
    sleep 0.12
}

send_owner_key() {
    xdotool key --clearmodifiers --delay "$input_delay_ms" --window "$owner_id" "$@"
    sleep "$settle_seconds"
}

send_active_key() {
    xdotool key --clearmodifiers --delay "$input_delay_ms" "$@"
    sleep "$settle_seconds"
}

send_active_text() {
    local text="$1" active
    active="$(xdotool getactivewindow 2>/dev/null || true)"
    [[ -n "$active" ]] || return 1
    # Send through the current X11 focus chain so a focused Avalonia TextBox,
    # rather than only its top-level window, receives the text.
    xdotool type --clearmodifiers --delay "$input_delay_ms" "$text"
    sleep "$settle_seconds"
}

read_clipboard() {
    local destination="$1" error_destination="$2"
    timeout --foreground --kill-after=1s "$clipboard_timeout_seconds" xclip -selection clipboard -o > "$destination" 2> "$error_destination"
}

screen_changed() {
    local before="$1" after="$2" minimum="${3:-100}" metric
    metric="$(compare -metric AE "$output/$before" "$output/$after" null: 2>&1 || true)"
    [[ "$metric" =~ ^([0-9]+) ]] && (( BASH_REMATCH[1] >= minimum ))
}

screen_matches() {
    local before="$1" after="$2" maximum="${3:-200}" metric
    metric="$(compare -metric AE "$output/$before" "$output/$after" null: 2>&1 || true)"
    [[ "$metric" =~ ^([0-9]+) ]] && (( BASH_REMATCH[1] <= maximum ))
}

screen_difference() {
    local before="$1" after="$2" metric
    metric="$(compare -metric AE "$output/$before" "$output/$after" null: 2>&1 || true)"
    if [[ "$metric" =~ ^([0-9]+) ]]; then printf '%s' "${BASH_REMATCH[1]}"; else printf 'unknown'; fi
}

screen_nonblank() {
    local image_name="$1" minimum_mean="${2:-0.02}" mean
    mean="$(convert "$output/$image_name" -colorspace Gray -format '%[fx:mean]' info: 2>/dev/null || true)"
    awk -v value="$mean" -v minimum="$minimum_mean" 'BEGIN { exit !(value ~ /^[0-9.]+$/ && value > minimum) }'
}

hash_file() { sha256sum "$1" | awk '{print $1}'; }

active_owner_now() {
    [[ "$(xdotool getactivewindow 2>/dev/null || true)" == "$owner_id" &&
       "$(xdotool getwindowfocus 2>/dev/null || true)" == "$owner_id" ]]
}

on_error() {
    local exit_code=$?
    printf 'Probe command failed at line %s (exit %s).\n' "${BASH_LINENO[0]}" "$exit_code" > "$output/probe-runtime-error.txt"
    exit "$exit_code"
}
trap on_error ERR

baseline_dimensions="$(identify -format '%wx%h' "$output/../screenshots/initial.png" 2>/dev/null || true)"
if [[ "$baseline_dimensions" =~ ^([0-9]+)x([0-9]+)$ ]]; then
    baseline_width="${BASH_REMATCH[1]}"; baseline_height="${BASH_REMATCH[2]}"
else
    baseline_width="$screen_width"; baseline_height="$screen_height"
fi

# Establish the owner and calibrated geometry before the lifecycle helpers run.
mapfile -t visible_owner_ids < <(xdotool search --onlyvisible --name "$window_pattern" 2>/dev/null || true)
if (( ${#visible_owner_ids[@]} == 0 )); then
    printf 'No visible FreeP window matched %s.\n' "$window_pattern" > "$output/window-discovery-error.txt"
    exit 1
fi
owner_id="${visible_owner_ids[${#visible_owner_ids[@]}-1]}"
owner_title="$(xdotool getwindowname "$owner_id" 2>/dev/null || true)"
geometry="$(xdotool getwindowgeometry --shell "$owner_id" 2>/dev/null || true)"
eval "$geometry"
slide_pane_width=$(( WIDTH * 14 / 100 )); (( slide_pane_width > 180 )) && slide_pane_width=180; (( slide_pane_width < 140 )) && slide_pane_width=140
slide_thumbnail_x=$(( X + slide_pane_width / 2 ))
slide_thumbnail_y=$(( Y + HEIGHT * 34 / 100 ))
second_slide_thumbnail_y=$(( slide_thumbnail_y + HEIGHT * 17 / 100 ))
new_slide_x=$slide_thumbnail_x; new_slide_y=$(( Y + HEIGHT - 66 ))
slide_pane_geometry="${slide_pane_width}x$(( HEIGHT * 50 / 100 ))+${X}+$(( Y + HEIGHT * 17 / 100 ))"
status_geometry="${baseline_width}x26+0+$(( baseline_height - 26 ))"

capture "bootstrap-baseline.png"
capture_window_state "bootstrap-owner-state.txt"
{
    printf 'owner-window-id=%s\n' "$owner_id"
    printf 'owner-window-title=%s\n' "$owner_title"
    printf 'expected-fixture-filename=%s\n' "$expected_document_name"
    printf 'fixture-filename-in-title='; [[ "$owner_title" == *"$expected_document_name"* ]] && printf 'true\n' || printf 'false\n'
    printf 'freep-in-title='; [[ "$owner_title" == *FreeP* || "$owner_title" == *Freep* ]] && printf 'true\n' || printf 'false\n'
    printf 'window-geometry=%s\n' "$geometry"
    printf 'slide-pane-geometry=%s\n' "$slide_pane_geometry"
    printf 'status-geometry=%s\n' "$status_geometry"
} > "$output/bootstrap-visible-owner-proof.txt"

run_top_level_lifecycle() {
    local id="$1" shortcut="$2" label="$3" title_fragment="$4"
    local prefix="$id" before_count open_count dismissed_count active_after
    local candidate_title="" candidate_class="" new=false active_candidate=false title_ok=false open_changed=false dismissed=false native_restored=false
    focus_owner
    mapfile -t before_window_ids < <(window_ids)
    before_count="${#before_window_ids[@]}"
    capture "$prefix-before.png"
    capture_window_state "$prefix-before-state.txt"
    send_owner_key "$shortcut"
    capture "$prefix-open.png"
    mapfile -t open_window_ids < <(window_ids)
    open_count="${#open_window_ids[@]}"
    active_after="$(xdotool getactivewindow 2>/dev/null || true)"
    find_new_window "$active_after" "${open_window_ids[@]}" || true
    if [[ -n "$candidate_window_id" ]]; then
        candidate_title="$(xdotool getwindowname "$candidate_window_id" 2>/dev/null || true)"
        candidate_class="$(xprop -id "$candidate_window_id" WM_CLASS 2>/dev/null || true)"
        ! contains_id "$candidate_window_id" "${before_window_ids[@]}" && new=true
        [[ "$active_after" == "$candidate_window_id" && "$(xdotool getwindowfocus 2>/dev/null || true)" == "$candidate_window_id" ]] && active_candidate=true
        [[ "$candidate_title" == *"$title_fragment"* ]] && title_ok=true
    fi
    capture_window_state "$prefix-open-state.txt"
    screen_changed "$prefix-before.png" "$prefix-open.png" 200 && open_changed=true
    send_active_key Escape
    sleep 0.3
    capture "$prefix-dismissed.png"
    mapfile -t dismissed_window_ids < <(window_ids)
    dismissed_count="${#dismissed_window_ids[@]}"
    ! contains_id "${candidate_window_id:-}" "${dismissed_window_ids[@]}" && dismissed=true
    active_owner_now && native_restored=true
    capture_window_state "$prefix-dismissed-state.txt"
    {
        printf 'label=%s\n' "$label"
        printf 'shortcut=%s\n' "$shortcut"
        printf 'before-screenshot=%s-before.png\n' "$prefix"
        printf 'open-screenshot=%s-open.png\n' "$prefix"
        printf 'dismissed-screenshot=%s-dismissed.png\n' "$prefix"
        printf 'owner-window-id=%s\n' "$owner_id"
        printf 'candidate-window-id=%s\n' "${candidate_window_id:-}"
        printf 'candidate-title=%s\n' "$candidate_title"
        printf 'candidate-class=%s\n' "$candidate_class"
        printf 'before-window-count=%s\n' "$before_count"
        printf 'open-window-count=%s\n' "$open_count"
        printf 'dismissed-window-count=%s\n' "$dismissed_count"
        printf 'new-top-level-window=%s\n' "$new"
        printf 'intended-title-fragment=%s\n' "$title_ok"
        printf 'open-screen-transition=%s\n' "$open_changed"
        printf 'dismissed=%s\n' "$dismissed"
        printf 'native-owner-focus-restored=%s\n' "$native_restored"
        printf 'candidate-wm-class-begin\n%s\ncandidate-wm-class-end\n' "$candidate_class"
    } > "$output/$prefix-proof.txt"
    if $new && $active_candidate && $open_changed && $dismissed && $native_restored; then
        record "$id" "passed" "$label opened a new active/focused native X11 surface, and Escape removed it with exact owner restoration; portal/window-manager titles and child-window counts are retained as evidence only." "$prefix-proof.txt" "$prefix-before.png" "$prefix-open.png" "$prefix-dismissed.png" "$prefix-before-state.txt" "$prefix-open-state.txt" "$prefix-dismissed-state.txt"
    else
        record "$id" "failed" "$label did not prove a new active/focused native X11 surface, visible transition, dismissal, and owner focus lifecycle." "$prefix-proof.txt" "$prefix-before.png" "$prefix-open.png" "$prefix-dismissed.png" "$prefix-before-state.txt" "$prefix-open-state.txt" "$prefix-dismissed-state.txt"
    fi
    focus_owner
}

run_top_level_lifecycle "file-open-shortcut-lifecycle" ctrl+o "Ctrl+O Open" "Open"
run_top_level_lifecycle "file-save-as-shortcut-lifecycle" ctrl+shift+s "Ctrl+Shift+S Save As" "Save"

focus_owner
before_count="$(window_count)"
capture "print-shortcut-before.png"
capture_window_state "print-shortcut-before-state.txt"
send_owner_key ctrl+p
capture "print-shortcut-open.png"
capture_window_state "print-shortcut-open-state.txt"
open_count="$(window_count)"
print_owner_active=false; active_owner_now && print_owner_active=true
print_overlay_changed=false; screen_changed print-shortcut-before.png print-shortcut-open.png 200 && print_overlay_changed=true
send_active_key Escape
sleep 0.25
capture "print-shortcut-dismissed.png"
capture_window_state "print-shortcut-dismissed-state.txt"
dismissed_count="$(window_count)"
print_owner_restored=false; active_owner_now && print_owner_restored=true
{
    printf 'shortcut=ctrl+p\n'
    printf 'surface=FreePBackstageOverlay / in-window Print backstage\n'
    printf 'owner-window-id=%s\n' "$owner_id"
    printf 'before-window-count=%s\n' "$before_count"
    printf 'open-window-count=%s\n' "$open_count"
    printf 'dismissed-window-count=%s\n' "$dismissed_count"
    printf 'active-owner-during-print=%s\n' "$print_owner_active"
    printf 'backstage-screen-transition=%s\n' "$print_overlay_changed"
    printf 'native-owner-focus-restored=%s\n' "$print_owner_restored"
} > "$output/print-shortcut-backstage-proof.txt"
if $print_owner_active && $print_overlay_changed && $print_owner_restored &&
   (( open_count == before_count )) && (( dismissed_count == before_count )); then
    record "print-shortcut-backstage-lifecycle" "passed" "Ctrl+P changed the in-window FreeP Print backstage while retaining the owner window; Escape restored exact owner focus." print-shortcut-backstage-proof.txt print-shortcut-before.png print-shortcut-open.png print-shortcut-dismissed.png print-shortcut-before-state.txt print-shortcut-open-state.txt print-shortcut-dismissed-state.txt
else
    record "print-shortcut-backstage-lifecycle" "failed" "Ctrl+P did not prove the in-window Print backstage transition and exact owner restoration." print-shortcut-backstage-proof.txt print-shortcut-before.png print-shortcut-open.png print-shortcut-dismissed.png print-shortcut-before-state.txt print-shortcut-open-state.txt print-shortcut-dismissed-state.txt
fi
focus_owner

run_find_replace_lifecycle() {
    local id="$1" shortcut="$2" expected_title="$3" sentinel="$4"
    local prefix="$id" before_count open_count dismissed_count active_after
    local candidate_title="" candidate_class="" new=false title_ok=false focus_ok=false typed=false clipboard_ready=false exact=false
    local dismissed=false native_restored=false open_changed=false
    printf '%s' "$sentinel" > "$output/$prefix-expected.txt"
    focus_owner
    mapfile -t before_window_ids < <(window_ids)
    before_count="${#before_window_ids[@]}"
    capture "$prefix-before.png"
    capture_window_state "$prefix-before-state.txt"
    send_owner_key "$shortcut"
    capture "$prefix-open.png"
    mapfile -t open_window_ids < <(window_ids)
    open_count="${#open_window_ids[@]}"
    active_after="$(xdotool getactivewindow 2>/dev/null || true)"
    find_new_window "$active_after" "${open_window_ids[@]}" || true
    if [[ -n "$candidate_window_id" ]]; then
        candidate_title="$(xdotool getwindowname "$candidate_window_id" 2>/dev/null || true)"
        candidate_class="$(xprop -id "$candidate_window_id" WM_CLASS 2>/dev/null || true)"
        ! contains_id "$candidate_window_id" "${before_window_ids[@]}" && new=true
        [[ "$candidate_title" == "$expected_title" ]] && title_ok=true
        # Avalonia's Opened handler focuses the textbox. Do not move focus to the
        # top-level X11 window: that steals focus from the textbox and makes typing
        # land nowhere visible while still returning success from xdotool.
        [[ "$(xdotool getactivewindow 2>/dev/null || true)" == "$candidate_window_id" && "$(xdotool getwindowfocus 2>/dev/null || true)" == "$candidate_window_id" ]] && focus_ok=true
    fi
    capture_window_state "$prefix-open-state.txt"
    capture "$prefix-focused.png"

    type_sentinel() {
        local active
        active="$(xdotool getactivewindow 2>/dev/null || true)"
        [[ -n "$active" ]] || return 1
        xdotool key --clearmodifiers --delay "$input_delay_ms" ctrl+a
        sleep "$settle_seconds"
        xdotool type --clearmodifiers --delay "$input_delay_ms" "$sentinel"
        sleep "$settle_seconds"
    }

    type_sentinel && typed=true || true
    capture "$prefix-typed.png"
    for _ in 1 2 3; do
        active_after="$(xdotool getactivewindow 2>/dev/null || true)"
        [[ -n "$active_after" ]] || break
        xdotool key --clearmodifiers --delay "$input_delay_ms" ctrl+a
        sleep "$settle_seconds"
        xdotool key --clearmodifiers --delay "$input_delay_ms" ctrl+c
        sleep "$settle_seconds"
        if read_clipboard "$output/$prefix-clipboard.txt" "$output/$prefix-clipboard-error.txt"; then
            clipboard_ready=true
            if cmp -s "$output/$prefix-expected.txt" "$output/$prefix-clipboard.txt"; then
                exact=true
                break
            fi
        fi
        type_sentinel && typed=true || true
    done
    capture_window_state "$prefix-focused-state.txt"
    if [[ -n "${candidate_window_id:-}" ]]; then
        xdotool key --clearmodifiers --delay "$input_delay_ms" --window "$candidate_window_id" Escape || true
        sleep "$settle_seconds"
        if contains_id "$candidate_window_id" $(window_ids); then
            xdotool key --clearmodifiers --delay "$input_delay_ms" --window "$candidate_window_id" Escape || true
            sleep "$settle_seconds"
        fi
    else
        send_active_key Escape
    fi
    sleep 0.3
    capture "$prefix-dismissed.png"
    mapfile -t dismissed_window_ids < <(window_ids)
    dismissed_count="${#dismissed_window_ids[@]}"
    ! contains_id "${candidate_window_id:-}" "${dismissed_window_ids[@]}" && dismissed=true
    active_owner_now && native_restored=true
    screen_changed "$prefix-before.png" "$prefix-open.png" 200 && open_changed=true
    capture_window_state "$prefix-dismissed-state.txt"
    {
        printf 'shortcut=%s\n' "$shortcut"
        printf 'expected-title=%s\n' "$expected_title"
        printf 'candidate-title=%s\n' "$candidate_title"
        printf 'sentinel=%s\n' "$sentinel"
        printf 'candidate-window-id=%s\n' "${candidate_window_id:-}"
        printf 'before-window-count=%s\n' "$before_count"
        printf 'open-window-count=%s\n' "$open_count"
        printf 'dismissed-window-count=%s\n' "$dismissed_count"
        printf 'new-top-level-window=%s\n' "$new"
        printf 'exact-mode-title=%s\n' "$title_ok"
        printf 'candidate-focus=%s\n' "$focus_ok"
        printf 'typed-sentinel=%s\n' "$typed"
        printf 'clipboard-ready=%s\n' "$clipboard_ready"
        printf 'clipboard-exact=%s\n' "$exact"
        printf 'open-screen-transition=%s\n' "$open_changed"
        printf 'dismissed=%s\n' "$dismissed"
        printf 'native-owner-focus-restored=%s\n' "$native_restored"
        printf 'candidate-wm-class-begin\n%s\ncandidate-wm-class-end\n' "$candidate_class"
    } > "$output/$prefix-proof.txt"
    if $new && $focus_ok && $typed && $clipboard_ready && $exact && $open_changed && $dismissed && $native_restored; then
        record "$id" "passed" "$expected_title mode accepted an exact clipboard sentinel through its naturally focused input, and Escape restored owner focus; Avalonia/X11 title text and nested-window counts are retained as evidence only." "$prefix-proof.txt" "$prefix-before.png" "$prefix-open.png" "$prefix-focused.png" "$prefix-typed.png" "$prefix-dismissed.png" "$prefix-before-state.txt" "$prefix-open-state.txt" "$prefix-focused-state.txt" "$prefix-dismissed-state.txt" "$prefix-expected.txt" "$prefix-clipboard.txt"
    else
        record "$id" "failed" "$expected_title mode did not prove a new focused dialog, exact clipboard sentinel, visible transition, dismissal, and owner restoration." "$prefix-proof.txt" "$prefix-before.png" "$prefix-open.png" "$prefix-focused.png" "$prefix-typed.png" "$prefix-dismissed.png" "$prefix-before-state.txt" "$prefix-open-state.txt" "$prefix-focused-state.txt" "$prefix-dismissed-state.txt" "$prefix-expected.txt" "$prefix-clipboard.txt"
    fi
    focus_owner
}

run_find_replace_lifecycle "find-shortcut-lifecycle" ctrl+f "Find" "FreeP-physical-find-sentinel-20260725"
run_find_replace_lifecycle "replace-shortcut-lifecycle" ctrl+h "Find and Replace" "FreeP-physical-replace-sentinel-20260725"

make_dirty() {
    local prefix="$1"
    focus_owner
    capture "$prefix-before.png"
    capture_region "$prefix-before.png" "$prefix-before-thumbnails.png" "$slide_pane_geometry"
    capture_window_state "$prefix-before-state.txt"
    xdotool mousemove "$new_slide_x" "$new_slide_y"
    xdotool click --clearmodifiers 1
    sleep "$settle_seconds"
    capture "$prefix-after.png"
    capture_region "$prefix-after.png" "$prefix-after-thumbnails.png" "$slide_pane_geometry"
    capture_window_state "$prefix-after-state.txt"
    dirty_transition=false
    screen_changed "$prefix-before-thumbnails.png" "$prefix-after-thumbnails.png" 200 && dirty_transition=true
    title_dirty=false
    [[ "$(xdotool getwindowname "$owner_id" 2>/dev/null || true)" == *"*"* ]] && title_dirty=true
    {
        printf 'physical-input=pointer click on real bottom New Slide affordance\n'
        printf 'point=%s,%s\n' "$new_slide_x" "$new_slide_y"
        printf 'thumbnail-transition=%s\n' "$dirty_transition"
        printf 'owner-title=%s\n' "$(xdotool getwindowname "$owner_id" 2>/dev/null || true)"
        printf 'dirty-title-marker=%s\n' "$title_dirty"
        printf 'before-state=%s-before-state.txt\n' "$prefix"
        printf 'after-state=%s-after-state.txt\n' "$prefix"
    } > "$output/$prefix-proof.txt"
    focus_owner
}

initial_hash="$(hash_file "$document_path")"
printf '%s\n' "$initial_hash" > "$output/fixture-mounted-before.sha256.txt"

# A physical slide mutation dirties the mounted current-path presentation.
make_dirty "current-path-dirty"
before_save_hash="$(hash_file "$document_path")"
before_count="$(window_count)"
capture "file-save-current-path-before.png"
capture_window_state "file-save-current-path-before-state.txt"
send_owner_key ctrl+s
after_save_hash=""
for _ in $(seq 1 40); do
    after_save_hash="$(hash_file "$document_path")"
    [[ "$after_save_hash" != "$before_save_hash" ]] && break
    sleep 0.25
done
printf '%s\n' "$after_save_hash" > "$output/fixture-mounted-after.sha256.txt"
capture "file-save-current-path-after.png"
capture_window_state "file-save-current-path-after-state.txt"
mapfile -t save_after_windows < <(window_ids)
save_window_count="${#save_after_windows[@]}"
save_as_window=false
for candidate in "${save_after_windows[@]}"; do
    [[ "$candidate" == "$owner_id" ]] && continue
    candidate_title="$(xdotool getwindowname "$candidate" 2>/dev/null || true)"
    [[ "$candidate_title" == *Save* || "$candidate_title" == *save* ]] && save_as_window=true
done
save_owner_restored=false; active_owner_now && save_owner_restored=true
hash_changed=false; [[ -n "$after_save_hash" && "$after_save_hash" != "$before_save_hash" ]] && hash_changed=true
dirty_proof_ok=false; grep -q 'thumbnail-transition=true' "$output/current-path-dirty-proof.txt" && dirty_proof_ok=true
{
    printf 'shortcut=ctrl+s\n'
    printf 'document-path=%s\n' "$document_path"
    printf 'initial-hash=%s\n' "$initial_hash"
    printf 'before-save-hash=%s\n' "$before_save_hash"
    printf 'after-save-hash=%s\n' "$after_save_hash"
    printf 'hash-changed=%s\n' "$hash_changed"
    printf 'dirty-input-proven=%s\n' "$dirty_proof_ok"
    printf 'before-window-count=%s\n' "$before_count"
    printf 'after-window-count=%s\n' "$save_window_count"
    printf 'save-as-window-visible=%s\n' "$save_as_window"
    printf 'owner-focus-restored=%s\n' "$save_owner_restored"
    printf 'fixture-before-artifact=fixture-mounted-before.sha256.txt\n'
    printf 'fixture-after-artifact=fixture-mounted-after.sha256.txt\n'
} > "$output/file-save-shortcut-current-path-proof.txt"
if $dirty_proof_ok && $hash_changed && ! $save_as_window && $save_owner_restored && (( save_window_count == before_count )); then
    record "file-save-shortcut-current-path" "passed" "Ctrl+S physically saved the dirtied current-path fixture, changed the host-mounted document hash, and opened no Save As window." file-save-shortcut-current-path-proof.txt current-path-dirty-proof.txt current-path-dirty-before-thumbnails.png current-path-dirty-after-thumbnails.png file-save-current-path-before.png file-save-current-path-after.png file-save-current-path-before-state.txt file-save-current-path-after-state.txt fixture-mounted-before.sha256.txt fixture-mounted-after.sha256.txt
else
    record "file-save-shortcut-current-path" "failed" "Ctrl+S did not prove a current-path hash change without a Save As window after physical dirtiness." file-save-shortcut-current-path-proof.txt current-path-dirty-proof.txt current-path-dirty-before-thumbnails.png current-path-dirty-after-thumbnails.png file-save-current-path-before.png file-save-current-path-after.png file-save-current-path-before-state.txt file-save-current-path-after-state.txt fixture-mounted-before.sha256.txt fixture-mounted-after.sha256.txt
fi
focus_owner

# Dirty Ctrl+N prompt: make a second physical mutation after the direct save.
make_dirty "file-new-dirty"
before_count="$(window_count)"
capture "file-new-shortcut-before.png"
capture_window_state "file-new-shortcut-before-state.txt"
dirty_title_before="$(xdotool getwindowname "$owner_id" 2>/dev/null || true)"
mapfile -t before_window_ids < <(window_ids)
send_owner_key ctrl+n
capture "file-new-shortcut-open.png"
mapfile -t new_open_windows < <(window_ids)
open_count="${#new_open_windows[@]}"
active_after="$(xdotool getactivewindow 2>/dev/null || true)"
find_new_window "$active_after" "${new_open_windows[@]}" || true
new_prompt_title=""; new_prompt_title_ok=false; new_prompt_focus=false; new_prompt_new=false; new_prompt_changed=false
if [[ -n "$candidate_window_id" ]]; then
    new_prompt_title="$(xdotool getwindowname "$candidate_window_id" 2>/dev/null || true)"
    [[ "$new_prompt_title" == *Save* || "$new_prompt_title" == *save* || "$new_prompt_title" == *Changes* || "$new_prompt_title" == *changes* ]] && new_prompt_title_ok=true
    [[ "$active_after" == "$candidate_window_id" && "$(xdotool getwindowfocus 2>/dev/null || true)" == "$candidate_window_id" ]] && new_prompt_focus=true
    ! contains_id "$candidate_window_id" "${before_window_ids[@]}" && new_prompt_new=true
fi
capture_window_state "file-new-shortcut-open-state.txt"
screen_changed file-new-shortcut-before.png file-new-shortcut-open.png 200 && new_prompt_changed=true
send_active_key Escape
sleep 0.3
capture "file-new-shortcut-dismissed.png"
mapfile -t new_dismissed_windows < <(window_ids)
dismissed_count="${#new_dismissed_windows[@]}"
new_prompt_removed=false; ! contains_id "${candidate_window_id:-}" "${new_dismissed_windows[@]}" && new_prompt_removed=true
new_owner_restored=false; active_owner_now && new_owner_restored=true
dirty_title_after="$(xdotool getwindowname "$owner_id" 2>/dev/null || true)"
dirty_state_preserved=false; [[ "$dirty_title_before" == *"*"* && "$dirty_title_after" == *"*"* ]] && dirty_state_preserved=true
new_screen_restored=false; screen_matches file-new-shortcut-before.png file-new-shortcut-dismissed.png 500 && new_screen_restored=true
capture_window_state "file-new-shortcut-dismissed-state.txt"
{
    printf 'shortcut=ctrl+n\n'
    printf 'dirty-title-before=%s\n' "$dirty_title_before"
    printf 'prompt-window-id=%s\n' "${candidate_window_id:-}"
    printf 'prompt-title=%s\n' "$new_prompt_title"
    printf 'prompt-title-intended=%s\n' "$new_prompt_title_ok"
    printf 'prompt-focus=%s\n' "$new_prompt_focus"
    printf 'prompt-new-window=%s\n' "$new_prompt_new"
    printf 'prompt-screen-transition=%s\n' "$new_prompt_changed"
    printf 'before-window-count=%s\n' "$before_count"
    printf 'open-window-count=%s\n' "$open_count"
    printf 'dismissed-window-count=%s\n' "$dismissed_count"
    printf 'prompt-removed=%s\n' "$new_prompt_removed"
    printf 'dirty-state-preserved=%s\n' "$dirty_state_preserved"
    printf 'owner-focus-restored=%s\n' "$new_owner_restored"
    printf 'screen-restored=%s\n' "$new_screen_restored"
    printf 'dirty-title-after=%s\n' "$dirty_title_after"
} > "$output/file-new-shortcut-proof.txt"
if $new_prompt_new && $new_prompt_focus && $new_prompt_changed && $new_prompt_removed && $dirty_state_preserved && $new_owner_restored && $new_screen_restored; then
    record "file-new-shortcut-lifecycle" "passed" "Ctrl+N on a physically dirtied presentation opened a new active/focused Save Changes surface; Escape removed it while preserving dirty state, screen, and exact owner focus. The native title and nested-window counts are retained as evidence only." file-new-shortcut-proof.txt file-new-shortcut-before.png file-new-shortcut-open.png file-new-shortcut-dismissed.png file-new-shortcut-before-state.txt file-new-shortcut-open-state.txt file-new-shortcut-dismissed-state.txt file-new-dirty-proof.txt file-new-dirty-before-thumbnails.png file-new-dirty-after-thumbnails.png
else
    record "file-new-shortcut-lifecycle" "failed" "Ctrl+N did not prove a new active/focused dirty-save surface, visible transition, Escape preservation, and exact owner restoration." file-new-shortcut-proof.txt file-new-shortcut-before.png file-new-shortcut-open.png file-new-shortcut-dismissed.png file-new-shortcut-before-state.txt file-new-shortcut-open-state.txt file-new-shortcut-dismissed-state.txt file-new-dirty-proof.txt file-new-dirty-before-thumbnails.png file-new-dirty-after-thumbnails.png
fi
focus_owner

mapfile -t visible_owner_ids < <(xdotool search --onlyvisible --name "$window_pattern" 2>/dev/null || true)
if (( ${#visible_owner_ids[@]} == 0 )); then
    printf 'No visible FreeP window matched %s.\n' "$window_pattern" > "$output/window-discovery-error.txt"
    exit 1
fi
owner_id="${visible_owner_ids[${#visible_owner_ids[@]}-1]}"
owner_title="$(xdotool getwindowname "$owner_id" 2>/dev/null || true)"
capture "baseline.png"
capture_window_state "owner-discovery-state.txt"
{
    printf 'owner-window-id=%s\n' "$owner_id"
    printf 'owner-window-title=%s\n' "$owner_title"
    printf 'expected-window-pattern=%s\n' "$window_pattern"
    printf 'expected-fixture-filename=%s\n' "$expected_document_name"
    printf 'fixture-filename-in-title='; [[ "$owner_title" == *"$expected_document_name"* ]] && printf 'true\n' || printf 'false\n'
    printf 'freep-in-title='; [[ "$owner_title" == *FreeP* || "$owner_title" == *Freep* ]] && printf 'true\n' || printf 'false\n'
    printf 'visible-owner-count=%s\n' "${#visible_owner_ids[@]}"
    printf 'wm-class-begin\n'; xprop -id "$owner_id" WM_CLASS 2>/dev/null || true; printf 'wm-class-end\n'
} > "$output/visible-window-discovery-proof.txt"
if active_owner_now && [[ "$owner_title" == *"$expected_document_name"* &&
      ( "$owner_title" == *FreeP* || "$owner_title" == *Freep* ) ]]; then
    record "visible-window-discovery" "passed" "Visible focused FreeP owner and fixture filename/title were discovered through X11." visible-window-discovery-proof.txt baseline.png owner-discovery-state.txt
else
    record "visible-window-discovery" "failed" "The visible owner did not prove FreeP focus and the expected fixture filename/title." visible-window-discovery-proof.txt baseline.png owner-discovery-state.txt
fi

geometry="$(xdotool getwindowgeometry --shell "$owner_id" 2>/dev/null || true)"
eval "$geometry"
slide_pane_width=$(( WIDTH * 14 / 100 )); (( slide_pane_width > 180 )) && slide_pane_width=180; (( slide_pane_width < 140 )) && slide_pane_width=140
slide_thumbnail_x=$(( X + slide_pane_width / 2 ))
slide_thumbnail_y=$(( Y + HEIGHT * 34 / 100 ))
second_slide_thumbnail_y=$(( slide_thumbnail_y + HEIGHT * 17 / 100 ))
new_slide_x=$slide_thumbnail_x; new_slide_y=$(( Y + HEIGHT - 66 ))
slide_pane_geometry="${slide_pane_width}x$(( HEIGHT * 50 / 100 ))+${X}+$(( Y + HEIGHT * 17 / 100 ))"
status_geometry="${baseline_width}x26+0+$(( baseline_height - 26 ))"
{
    printf 'window-geometry=%s\n' "$geometry"
    printf 'slide-pane-geometry=%s\n' "$slide_pane_geometry"
    printf 'status-geometry=%s\n' "$status_geometry"
    printf 'slide-one-point=%s,%s\n' "$slide_thumbnail_x" "$slide_thumbnail_y"
    printf 'slide-two-point=%s,%s\n' "$slide_thumbnail_x" "$second_slide_thumbnail_y"
    printf 'new-slide-point=%s,%s\n' "$new_slide_x" "$new_slide_y"
    printf 'calibration=owner-window-geometry-plus-screen-capture\n'
} > "$output/physical-calibration.txt"

select_slide() {
    local slide_number="$1" y="$slide_thumbnail_y"
    [[ "$slide_number" == 2 ]] && y="$second_slide_thumbnail_y"
    focus_owner; xdotool mousemove "$slide_thumbnail_x" "$y"; xdotool click --clearmodifiers 1; sleep "$settle_seconds"; focus_owner; sleep 0.25
}

capture_selection() {
    local prefix="$1"
    capture "$prefix-owner.png"
    capture_region "$prefix-owner.png" "$prefix-thumbnails.png" "$slide_pane_geometry"
    capture_region "$prefix-owner.png" "$prefix-status.png" "$status_geometry"
    capture_window_state "$prefix-owner-state.txt"
}

run_slideshow_capture() {
    local prefix="$1" shortcut="$2"
    local before_count open_count dismissed_count active_after
    local candidate_title="" candidate_class="" candidate_new=false active_ready=false
    local native_owner_restored=false dismissed=false open_changed=false

    capture_selection "$prefix-before"
    mapfile -t before_window_ids < <(window_ids)
    before_count="${#before_window_ids[@]}"
    xdotool mousemove 0 0
    send_owner_key "$shortcut"
    sleep 0.7
    capture "$prefix-open.png"
    mapfile -t open_ids < <(window_ids)
    open_count="${#open_ids[@]}"
    active_after="$(xdotool getactivewindow 2>/dev/null || true)"
    find_new_window "$active_after" "${open_ids[@]}" || true
    if [[ -n "$candidate_window_id" ]]; then
        candidate_title="$(xdotool getwindowname "$candidate_window_id" 2>/dev/null || true)"
        candidate_class="$(xprop -id "$candidate_window_id" WM_CLASS 2>/dev/null || true)"
        ! contains_id "$candidate_window_id" "${before_window_ids[@]}" && candidate_new=true
        [[ "$active_after" == "$candidate_window_id" && "$(xdotool getwindowfocus 2>/dev/null || true)" == "$candidate_window_id" ]] && active_ready=true
    fi
    capture_stage "$prefix-open.png" "$prefix-stage.png"
    capture_window_state "$prefix-open-state.txt"
    stage_rendered=false; screen_nonblank "$prefix-stage.png" 0.02 && stage_rendered=true || true

    send_active_key Escape
    sleep 0.45
    capture "$prefix-dismissed.png"
    mapfile -t dismissed_ids < <(window_ids)
    dismissed_count="${#dismissed_ids[@]}"
    ! contains_id "${candidate_window_id:-}" "${dismissed_ids[@]}" && dismissed=true
    active_owner_now && native_owner_restored=true
    screen_changed "$prefix-before-owner.png" "$prefix-open.png" 200 && open_changed=true
    capture_window_state "$prefix-dismissed-state.txt"
    {
        printf 'shortcut=%s\n' "$shortcut"
        printf 'before-owner=%s-owner.png\n' "$prefix-before"
        printf 'open-screenshot=%s-open.png\n' "$prefix"
        printf 'stage-capture=%s-stage.png\n' "$prefix"
        printf 'dismissed-screenshot=%s-dismissed.png\n' "$prefix"
        printf 'before-owner-state=%s-owner-state.txt\n' "$prefix"
        printf 'open-state=%s-open-state.txt\n' "$prefix"
        printf 'dismissed-state=%s-dismissed-state.txt\n' "$prefix"
        printf 'owner-window-id=%s\n' "$owner_id"
        printf 'candidate-window-id=%s\n' "${candidate_window_id:-}"
        printf 'candidate-title=%s\n' "$candidate_title"
        printf 'candidate-class=%s\n' "$candidate_class"
        printf 'before-window-count=%s\n' "$before_count"
        printf 'open-window-count=%s\n' "$open_count"
        printf 'dismissed-window-count=%s\n' "$dismissed_count"
        printf 'candidate-new=%s\n' "$candidate_new"
        printf 'active-candidate=%s\n' "$active_ready"
        printf 'stage-rendered-content=%s\n' "$stage_rendered"
        printf 'open-screen-transition=%s\n' "$open_changed"
        printf 'dismissed-candidate=%s\n' "$dismissed"
        printf 'native-owner-focus-restored=%s\n' "$native_owner_restored"
        printf 'candidate-wm-class-begin\n%s\ncandidate-wm-class-end\n' "$candidate_class"
    } > "$output/$prefix-proof.txt"
    focus_owner
}

# Calibrated slideshow controls: F5 from selected slide 2 must equal the
# Shift+F5 slide-1 control and differ from Shift+F5 slide 2.
select_slide 1
run_slideshow_capture "slideshow-control-from-slide1" shift+F5
select_slide 2
run_slideshow_capture "slideshow-current-from-slide2" shift+F5
select_slide 2
run_slideshow_capture "slideshow-beginning-from-slide2" F5

control_stage="slideshow-control-from-slide1-stage.png"
current_stage="slideshow-current-from-slide2-stage.png"
beginning_stage="slideshow-beginning-from-slide2-stage.png"
control_beginning_ae="$(screen_difference "$control_stage" "$beginning_stage")"
control_current_ae="$(screen_difference "$control_stage" "$current_stage")"
control_stage_rendered=false; screen_nonblank "$control_stage" 0.02 && control_stage_rendered=true || true
current_stage_rendered=false; screen_nonblank "$current_stage" 0.02 && current_stage_rendered=true || true
beginning_stage_rendered=false; screen_nonblank "$beginning_stage" 0.02 && beginning_stage_rendered=true || true
beginning_matches_control=false; beginning_differs_current=false
[[ "$control_beginning_ae" =~ ^[0-9]+$ ]] && (( control_beginning_ae <= 1000 )) && beginning_matches_control=true
[[ "$control_current_ae" =~ ^[0-9]+$ ]] && (( control_current_ae > 1000 )) && beginning_differs_current=true
{
    printf 'control-stage=%s\n' "$control_stage"
    printf 'current-slide2-stage=%s\n' "$current_stage"
    printf 'f5-from-selected-slide2-stage=%s\n' "$beginning_stage"
    printf 'control-vs-f5-AE=%s\n' "$control_beginning_ae"
    printf 'control-vs-shift-f5-slide2-AE=%s\n' "$control_current_ae"
    printf 'control-stage-rendered-content=%s\n' "$control_stage_rendered"
    printf 'current-stage-rendered-content=%s\n' "$current_stage_rendered"
    printf 'f5-stage-rendered-content=%s\n' "$beginning_stage_rendered"
    printf 'f5-pixel-matches-shift-f5-control=%s\n' "$beginning_matches_control"
    printf 'f5-differs-from-shift-f5-slide2=%s\n' "$beginning_differs_current"
    printf 'selection-status-window-evidence=retained in each prefixed before/open/dismissed artifact\n'
} > "$output/slideshow-from-beginning-proof.txt"
if $control_stage_rendered && $current_stage_rendered && $beginning_stage_rendered && $beginning_matches_control && $beginning_differs_current &&
   grep -q 'candidate-new=true' "$output/slideshow-beginning-from-slide2-proof.txt" &&
   grep -q 'active-candidate=true' "$output/slideshow-beginning-from-slide2-proof.txt" &&
   grep -q 'open-screen-transition=true' "$output/slideshow-beginning-from-slide2-proof.txt" &&
   grep -q 'dismissed-candidate=true' "$output/slideshow-beginning-from-slide2-proof.txt" &&
   grep -q 'native-owner-focus-restored=true' "$output/slideshow-beginning-from-slide2-proof.txt"; then
    record "slideshow-from-beginning-lifecycle" "passed" \
        "F5 from physically selected slide 2 opened slide 1: its calibrated stage pixel-matched the Shift+F5 slide-1 control and differed from Shift+F5 slide 2; Escape restored owner focus." \
        slideshow-from-beginning-proof.txt slideshow-beginning-from-slide2-proof.txt slideshow-beginning-from-slide2-before-thumbnails.png slideshow-beginning-from-slide2-before-status.png slideshow-beginning-from-slide2-before-owner-state.txt slideshow-beginning-from-slide2-open.png slideshow-beginning-from-slide2-stage.png slideshow-beginning-from-slide2-dismissed.png slideshow-beginning-from-slide2-open-state.txt slideshow-beginning-from-slide2-dismissed-state.txt slideshow-control-from-slide1-stage.png slideshow-current-from-slide2-stage.png
else
    record "slideshow-from-beginning-lifecycle" "failed" \
        "F5 from selected slide 2 did not satisfy the calibrated slide-1 equality and slide-2 difference checks." \
        slideshow-from-beginning-proof.txt slideshow-beginning-from-slide2-proof.txt slideshow-beginning-from-slide2-before-thumbnails.png slideshow-beginning-from-slide2-before-status.png slideshow-beginning-from-slide2-before-owner-state.txt slideshow-beginning-from-slide2-open.png slideshow-beginning-from-slide2-stage.png slideshow-beginning-from-slide2-dismissed.png slideshow-beginning-from-slide2-open-state.txt slideshow-beginning-from-slide2-dismissed-state.txt slideshow-control-from-slide1-stage.png slideshow-current-from-slide2-stage.png
fi

{
    printf 'shift-f5-from-selected-slide2-proof=%s\n' slideshow-current-from-slide2-proof.txt
    printf 'shift-f5-slide2-stage=%s\n' "$current_stage"
    printf 'shift-f5-slide1-control-stage=%s\n' "$control_stage"
    printf 'slide2-vs-slide1-control-AE=%s\n' "$control_current_ae"
    printf 'slide2-differs-from-control=%s\n' "$beginning_differs_current"
    printf 'selection-status-window-evidence=retained in before/open/dismissed artifacts\n'
} > "$output/slideshow-from-current-proof.txt"
if $current_stage_rendered && $beginning_differs_current && grep -q 'native-owner-focus-restored=true' "$output/slideshow-current-from-slide2-proof.txt" &&
   grep -q 'candidate-new=true' "$output/slideshow-current-from-slide2-proof.txt" &&
   grep -q 'active-candidate=true' "$output/slideshow-current-from-slide2-proof.txt" &&
   grep -q 'open-screen-transition=true' "$output/slideshow-current-from-slide2-proof.txt" &&
   grep -q 'dismissed-candidate=true' "$output/slideshow-current-from-slide2-proof.txt"; then
    record "slideshow-from-current-lifecycle" "passed" \
        "Shift+F5 from physically selected slide 2 opened the selected slide, differed from the calibrated slide-1 control, and Escape restored the owner." \
        slideshow-from-current-proof.txt slideshow-current-from-slide2-proof.txt slideshow-current-from-slide2-before-thumbnails.png slideshow-current-from-slide2-before-status.png slideshow-current-from-slide2-before-owner-state.txt slideshow-current-from-slide2-open.png slideshow-current-from-slide2-stage.png slideshow-current-from-slide2-dismissed.png slideshow-current-from-slide2-open-state.txt slideshow-current-from-slide2-dismissed-state.txt slideshow-control-from-slide1-stage.png
else
    record "slideshow-from-current-lifecycle" "failed" \
        "Shift+F5 from selected slide 2 did not prove the distinct slide-2 capture and owner restoration." \
        slideshow-from-current-proof.txt slideshow-current-from-slide2-proof.txt slideshow-current-from-slide2-before-thumbnails.png slideshow-current-from-slide2-before-status.png slideshow-current-from-slide2-before-owner-state.txt slideshow-current-from-slide2-open.png slideshow-current-from-slide2-stage.png slideshow-current-from-slide2-dismissed.png slideshow-current-from-slide2-open-state.txt slideshow-current-from-slide2-dismissed-state.txt slideshow-control-from-slide1-stage.png
fi

python3 - "$records" "$screenshots_file" "$output/results.json" "$owner_id" "$owner_title" "$expected_document_name" <<'PY'
import json
import sys

records_path, screenshots_path, manifest_path, owner_id, owner_title, fixture_name = sys.argv[1:]
with open(records_path, encoding="utf-8") as handle:
    raw_results = [json.loads(line) for line in handle if line.strip()]
# The bootstrap discovery is intentionally retained as an early owner artifact;
# the final manifest keeps the last observation for each contract ID.
by_id = {}
for result in raw_results:
    by_id[result["id"]] = result
results = list(by_id.values())
with open(screenshots_path, encoding="utf-8") as handle:
    screenshots = list(dict.fromkeys(line.strip() for line in handle if line.strip()))
manifest = {
    "schemaVersion": 1,
    "suite": "freep-linux-file-slideshow-shortcut-physical",
    "platform": "linux",
    "shell": "avalonia",
    "app": "FreeP",
    "baseline": False,
    "appSurface": "document-editor-file-slideshow-shortcuts",
    "window": {"id": owner_id, "title": owner_title, "pattern": fixture_name, "visible": True},
    "parameters": {
        "width": int(__import__("os").environ.get("FREEP_SCREEN_WIDTH", "1280")),
        "height": int(__import__("os").environ.get("FREEP_SCREEN_HEIGHT", "820")),
        "dpi": int(__import__("os").environ.get("FREEP_SCREEN_DPI", "96")),
        "fixture": fixture_name,
    },
    "coverage": {
        "scope": "physical FreeP file/slideshow shortcut evidence lane",
        "exhaustive": False,
        "familyContract": "tools/Run-FamilyLinuxInteractionValidation.ps1 keeps its exact FreeP 22-row contract.",
    },
    "contractValidation": {
        "status": "pending",
        "validator": "tools/Run-FreePFileSlideshowShortcutValidation.ps1",
        "contractReference": "tools/LinuxInteractiveDocker/freep-file-slideshow-shortcut-validation.schema.json",
    },
    "screenshots": [{"name": name, "kind": "screenshot"} for name in screenshots],
    "summary": {
        "passed": sum(result["status"] == "passed" for result in results),
        "failed": sum(result["status"] == "failed" for result in results),
        "total": len(results),
    },
    "results": results,
}
with open(manifest_path, "w", encoding="utf-8") as handle:
    json.dump(manifest, handle, ensure_ascii=False, indent=2)
    handle.write("\n")
print(json.dumps(manifest["summary"], sort_keys=True))
if len(results) != 10 or manifest["summary"]["failed"]:
    sys.exit(1)
PY
