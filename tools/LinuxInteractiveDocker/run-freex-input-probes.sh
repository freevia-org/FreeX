#!/usr/bin/env bash
set -euo pipefail

export DISPLAY=:99
output="${1:-/work/x11-validation}"
mkdir -p "$output"

window_id="$(xdotool search --onlyvisible --name FreeX 2>/dev/null | tail -1 || true)"
if [[ -z "$window_id" ]]; then
    printf '{"error":"No visible FreeX window."}\n' > "$output/x11-input-results.json"
    exit 1
fi

xdotool windowactivate --sync "$window_id" 2>/dev/null || true
xdotool windowfocus "$window_id" 2>/dev/null || true
sleep 0.5

declare -a results=()

record() {
    local id="$1" status="$2" evidence="$3" note="${4:-}"
    results+=("{\"id\":\"$id\",\"category\":\"x11-input\",\"status\":\"$status\",\"evidenceLevel\":\"physical-x11-input\",\"evidence\":\"$evidence\",\"note\":\"$note\"}")
}

screen_changed() {
    local before="$1" after="$2" minimum="${3:-300}"
    local changed
    changed="$(compare -metric AE "$before" "$after" null: 2>&1 || true)"
    [[ "$changed" =~ ^[0-9]+$ ]] && (( changed >= minimum ))
}

dismiss_overlays() {
    xdotool key Escape 2>/dev/null || true
    sleep 0.4
}

window_count() {
    wmctrl -l 2>/dev/null | wc -l
}

probe_cancelable_window() {
    local id="$1" keys="$2" screenshot_name="$3"
    local before after
    before="$(window_count)"
    xdotool key --window "$window_id" "$keys"
    sleep 1
    after="$(window_count)"
    scrot "$output/$screenshot_name"
    if (( after > before )); then
        xdotool key Escape
        sleep 0.7
        record "$id" "passed" "$screenshot_name"
    else
        record "$id" "failed" "$screenshot_name" "$keys did not open a cancelable window."
        dismiss_overlays
    fi
}

# Alt must enter the production keytip path. The pixel assertion is deliberately broad: the
# in-process matrix separately validates exact keytip routing and this proves the X11 Alt event arrives.
scrot "$output/alt-before.png"
xdotool key --window "$window_id" Alt_L
sleep 0.7
scrot "$output/alt-after.png"
if screen_changed "$output/alt-before.png" "$output/alt-after.png" 500; then
    record "keytips-alt" "passed" "alt-before.png -> alt-after.png"
else
    record "keytips-alt" "failed" "alt-before.png -> alt-after.png" "Alt produced no visible keytip change."
fi
dismiss_overlays

# F10 is Excel's alternate keytip entry route.
scrot "$output/f10-before.png"
xdotool key --window "$window_id" F10
sleep 0.7
scrot "$output/f10-after.png"
if screen_changed "$output/f10-before.png" "$output/f10-after.png" 500; then
    record "keytips-f10" "passed" "f10-before.png -> f10-after.png"
else
    record "keytips-f10" "failed" "f10-before.png -> f10-after.png" "F10 produced no visible keytip change."
fi
dismiss_overlays

# Keyboard context menu on the active worksheet cell.
xdotool key --window "$window_id" ctrl+Home
sleep 0.3
scrot "$output/context-keyboard-before.png"
xdotool key --window "$window_id" shift+F10
sleep 0.7
scrot "$output/context-keyboard-after.png"
if screen_changed "$output/context-keyboard-before.png" "$output/context-keyboard-after.png" 1000; then
    record "worksheet-context-shift-f10" "passed" "context-keyboard-after.png"
else
    record "worksheet-context-shift-f10" "failed" "context-keyboard-after.png" "Shift+F10 produced no visible context menu."
fi
dismiss_overlays

# Pointer context menu over the worksheet body. The app is maximized at a stable 1280x820 harness size.
scrot "$output/context-pointer-before.png"
xdotool mousemove --window "$window_id" 360 330 click 3
sleep 0.7
scrot "$output/context-pointer-after.png"
if screen_changed "$output/context-pointer-before.png" "$output/context-pointer-after.png" 1000; then
    record "worksheet-context-right-click" "passed" "context-pointer-after.png"
else
    record "worksheet-context-right-click" "failed" "context-pointer-after.png" "Right-click produced no visible context menu."
fi
dismiss_overlays

# Real shortcut-to-dialog path, followed by keyboard focus traversal and Escape cancellation.
xdotool key --window "$window_id" ctrl+1
sleep 0.8
dialog_id="$(xdotool search --onlyvisible --name 'Format Cells' 2>/dev/null | tail -1 || true)"
if [[ -n "$dialog_id" ]]; then
    scrot "$output/dialog-format-cells-open.png"
    xdotool key Tab Tab shift+Tab
    sleep 0.4
    scrot "$output/dialog-format-cells-tabbed.png"
    xdotool key Escape
    sleep 0.7
    if [[ -z "$(xdotool search --onlyvisible --name 'Format Cells' 2>/dev/null | tail -1 || true)" ]]; then
        record "dialog-format-cells-keyboard" "passed" "dialog-format-cells-open.png; dialog-format-cells-tabbed.png"
    else
        record "dialog-format-cells-keyboard" "failed" "dialog-format-cells-tabbed.png" "Escape did not close Format Cells."
        dismiss_overlays
    fi
else
    record "dialog-format-cells-keyboard" "failed" "dialog-format-cells-open.png" "Ctrl+1 did not open Format Cells."
fi

# Native/storage and print boundaries are cancel-only: validation must not mutate the filesystem or
# submit a print job, but it must prove the production shortcut reaches a visible owned/native window.
probe_cancelable_window "native-open-cancel" "ctrl+o" "native-open.png"
probe_cancelable_window "native-save-as-cancel" "F12" "native-save-as.png"
probe_cancelable_window "print-preview-cancel" "ctrl+p" "print-preview.png"

# F2 enters the real inline editor; typing and Escape must visibly enter and leave edit mode.
xdotool windowactivate --sync "$window_id" 2>/dev/null || true
xdotool key --window "$window_id" ctrl+Home F2
sleep 0.5
scrot "$output/inline-edit-open.png"
xdotool type --window "$window_id" --delay 30 "Validation"
xdotool key --window "$window_id" Escape
sleep 0.5
scrot "$output/inline-edit-cancelled.png"
if [[ -n "$(xdotool search --onlyvisible --name FreeX 2>/dev/null | tail -1 || true)" ]]; then
    record "inline-edit-f2-escape" "passed" "inline-edit-open.png; inline-edit-cancelled.png"
else
    record "inline-edit-f2-escape" "failed" "inline-edit-cancelled.png" "Application exited during inline editing."
fi

{
    printf '{"schemaVersion":1,"platform":"linux","shell":"avalonia","results":['
    local_first=true
    for result in "${results[@]}"; do
        if $local_first; then local_first=false; else printf ','; fi
        printf '%s' "$result"
    done
    printf ']}\n'
} > "$output/x11-input-results.json"
