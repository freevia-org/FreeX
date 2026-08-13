#!/usr/bin/env bash

probe_track_screenshot() {
    printf '%s\n' "$1" >> "$screenshots_file"
}

probe_capture() {
    local name="$1"
    command -v scrot >/dev/null 2>&1 || return 1
    scrot -o "$output/$name" >/dev/null 2>&1 || return 1
    [[ -s "$output/$name" ]] || return 1
    probe_track_screenshot "$name"
}

probe_focus_owner() {
    timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
        xdotool windowactivate --sync "$owner_id" >/dev/null 2>&1 || true
    timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
        xdotool windowfocus "$owner_id" >/dev/null 2>&1 || true
    sleep 0.12
}

probe_send_owner_key() {
    probe_focus_owner
    timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
        xdotool key --clearmodifiers --delay "$input_delay_ms" "$@"
    sleep "$settle_seconds"
}

probe_capture_window_state() {
    local name="$1"
    {
        printf 'owner-window-id=%s\nowner-window-title=%s\n' "$owner_id" "$owner_title"
        printf 'active-window=%s\nfocus-window=%s\n' \
            "$(xdotool getactivewindow 2>/dev/null || true)" \
            "$(xdotool getwindowfocus 2>/dev/null || true)"
        printf 'wmctrl-list-begin\n'
        wmctrl -l 2>/dev/null || true
        printf 'wmctrl-list-end\n'
    } > "$output/$name"
}
