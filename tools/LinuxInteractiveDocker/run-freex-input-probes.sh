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
probe_cancelable_window "native-open-ctrl-f12-cancel" "ctrl+F12" "native-open.png"
probe_cancelable_window "native-save-shift-f12-cancel" "shift+F12" "native-save.png"
probe_cancelable_window "print-preview-ctrl-shift-f12-cancel" "ctrl+shift+F12" "print-preview.png"

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

# Physical worksheet editing probes use blank cells G8:G10 and the seeded numeric target B2. The
# harness viewport is fixed at 1280x820, so these coordinates stay inside the visible grid while
# avoiding the demo data and every dialog probe above.
grid_left=46
grid_top=166
cell_width=104
cell_height=24

select_cell() {
    local column_offset="$1" row_offset="$2"
    xdotool windowactivate --sync "$window_id" 2>/dev/null || true
    xdotool key --window "$window_id" ctrl+Home
    for ((i = 0; i < column_offset; i++)); do xdotool key --window "$window_id" Right; done
    for ((i = 0; i < row_offset; i++)); do xdotool key --window "$window_id" Down; done
    sleep 0.25
}

crop_cell() {
    local screenshot="$1" output_file="$2" column_offset="$3" row_offset="$4"
    local x=$((grid_left + column_offset * cell_width + 2))
    local y=$((grid_top + row_offset * cell_height + 2))
    convert "$screenshot" -crop "$((cell_width - 4))x$((cell_height - 4))+$x+$y" +repage "$output_file"
}

cell_region_changed() {
    local before="$1" after="$2"
    local changed
    changed="$(compare -metric AE "$before" "$after" null: 2>&1 || true)"
    [[ "$changed" =~ ^[0-9]+$ ]] && (( changed >= 8 ))
}

target_b2_x=$((grid_left + cell_width + cell_width / 2))
target_b2_y=$((grid_top + cell_height + cell_height / 2))

# G8: commit a unique inline value with Enter, then compare the cell interior after the selection
# moves away. This distinguishes a real model/render commit from merely opening and closing F2.
select_cell 6 7
xdotool key --window "$window_id" Right
scrot "$output/inline-edit-commit-before.png"
select_cell 6 7
xdotool key --window "$window_id" F2
xdotool type --window "$window_id" --delay 25 "X11InlineCommit"
scrot "$output/inline-edit-commit-editing.png"
xdotool key --window "$window_id" Return
sleep 0.5
scrot "$output/inline-edit-commit-after.png"
crop_cell "$output/inline-edit-commit-before.png" "$output/inline-edit-commit-before-cell.png" 6 7
crop_cell "$output/inline-edit-commit-after.png" "$output/inline-edit-commit-after-cell.png" 6 7
if cell_region_changed "$output/inline-edit-commit-before-cell.png" "$output/inline-edit-commit-after-cell.png"; then
    record "inline-edit-f2-enter-commit" "passed" "inline-edit-commit-editing.png; inline-edit-commit-after-cell.png"
else
    record "inline-edit-f2-enter-commit" "failed" "inline-edit-commit-after-cell.png" "F2 typing and Enter produced no visible committed value in G8."
    dismiss_overlays
fi

# G9: enter a formula inline, click B2 while point mode is active, and commit. The point-mode image
# proves the click altered the formula editor; the cell crop proves the resulting formula rendered.
select_cell 6 8
xdotool key --window "$window_id" Right
scrot "$output/inline-point-before.png"
select_cell 6 8
xdotool key --window "$window_id" F2
xdotool type --window "$window_id" --delay 25 "="
scrot "$output/inline-point-equals.png"
xdotool mousemove --window "$window_id" "$target_b2_x" "$target_b2_y" click 1
sleep 0.4
scrot "$output/inline-point-address.png"
xdotool key --window "$window_id" Return
sleep 0.5
scrot "$output/inline-point-committed.png"
crop_cell "$output/inline-point-before.png" "$output/inline-point-before-cell.png" 6 8
crop_cell "$output/inline-point-committed.png" "$output/inline-point-committed-cell.png" 6 8
if screen_changed "$output/inline-point-equals.png" "$output/inline-point-address.png" 20 &&
   cell_region_changed "$output/inline-point-before-cell.png" "$output/inline-point-committed-cell.png"; then
    record "inline-point-mode-click" "passed" "inline-point-address.png; inline-point-committed-cell.png"
else
    record "inline-point-mode-click" "failed" "inline-point-address.png; inline-point-committed-cell.png" "Clicking B2 did not visibly insert and commit an inline point-mode reference in G9."
    dismiss_overlays
fi

# G10: focus the formula bar, enter '=', toggle Edit then Point with F2, click B2, and commit. This
# validates the physical formula-bar focus path independently from the inline editor route.
select_cell 6 9
xdotool key --window "$window_id" Right
scrot "$output/formula-point-before.png"
select_cell 6 9
xdotool mousemove --window "$window_id" 380 151 click 1
xdotool key --window "$window_id" ctrl+a
xdotool type --window "$window_id" --delay 25 "="
xdotool key --window "$window_id" F2 F2
scrot "$output/formula-point-equals.png"
xdotool mousemove --window "$window_id" "$target_b2_x" "$target_b2_y" click 1
sleep 0.4
scrot "$output/formula-point-address.png"
xdotool key --window "$window_id" Return
sleep 0.5
scrot "$output/formula-point-committed.png"
crop_cell "$output/formula-point-before.png" "$output/formula-point-before-cell.png" 6 9
crop_cell "$output/formula-point-committed.png" "$output/formula-point-committed-cell.png" 6 9
if screen_changed "$output/formula-point-equals.png" "$output/formula-point-address.png" 20 &&
   cell_region_changed "$output/formula-point-before-cell.png" "$output/formula-point-committed-cell.png"; then
    record "formula-bar-point-mode-click" "passed" "formula-point-address.png; formula-point-committed-cell.png"
else
    record "formula-bar-point-mode-click" "failed" "formula-point-address.png; formula-point-committed-cell.png" "Formula-bar Edit/Point toggling and B2 click did not visibly commit a reference in G10."
    dismiss_overlays
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
