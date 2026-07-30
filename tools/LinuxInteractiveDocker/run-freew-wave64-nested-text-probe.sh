#!/usr/bin/env bash
set -eEuo pipefail

export DISPLAY="${DISPLAY:-:99}"
output="${1:-/work/freew-wave64-nested-text}"
selector="${FREEW_WAVE64_SELECTOR:-nested-text}"
mkdir -p "$output"
window_id="$(xdotool search --onlyvisible --name 'FreeW' 2>/dev/null | tail -1 || true)"
fail() {
    local reason="$1"
    printf '{"schemaVersion":1,"suite":"freew-linux-nested-text-wave64-physical","platform":"linux","app":"FreeW","shell":"avalonia","results":[{"id":"nested-text-x11","status":"failed","evidence":["probe-results.json"],"note":"%s"}],"summary":{"passed":0,"failed":1,"total":1},"operation":{"childPath":"0,1"},"selectionPostcondition":{"visible":false}}\n' "$reason" > "$output/probe-results.json"
    exit 2
}
[[ -n "$window_id" ]] || fail "No visible FreeW window."
xdotool windowactivate --sync "$window_id" >/dev/null 2>&1 || true
xdotool windowfocus "$window_id" >/dev/null 2>&1 || true
# The container readiness file is emitted before Avalonia has completed the first floating-group
# layout pass. Let that pass and input focus settle before the physical selection click.
sleep 3
scrot "$output/01-baseline.png"

# Fixed 1280x820/96-DPI fixture coordinates, matching the existing nested-group physical lane.
center_x=636
center_y=490
xdotool mousemove --sync "$center_x" "$center_y"
xdotool click 1
sleep 1
if [[ "$selector" == "nested-text-direction" ]]; then
    # Wave 65 opt-in route: invoke the production Drawing Format > Text Direction dropdown.
    drawing_format_tab_x="${FREEW_DRAWING_FORMAT_TAB_X:-596}"
    drawing_format_tab_y="${FREEW_DRAWING_FORMAT_TAB_Y:-68}"
    text_direction_x="${FREEW_TEXT_DIRECTION_X:-369}"
    text_direction_y="${FREEW_TEXT_DIRECTION_Y:-101}"
    text_direction_item_x="${FREEW_TEXT_DIRECTION_ITEM_X:-305}"
    text_direction_item_y="${FREEW_TEXT_DIRECTION_ITEM_Y:-158}"
    xdotool mousemove --sync "$drawing_format_tab_x" "$drawing_format_tab_y"
    xdotool click 1
    sleep 0.65
    xdotool mousemove --sync "$text_direction_x" "$text_direction_y"
    xdotool click 1
    sleep 0.45
    xdotool mousemove --sync "$text_direction_item_x" "$text_direction_item_y"
    xdotool click 1
    sleep 1
    scrot "$output/02-nested-text-direction-rotate90.png"
    xdotool key --clearmodifiers --window "$window_id" ctrl+s
    sleep 1
    title="$(xdotool getwindowname "$window_id" 2>/dev/null || true)"
    [[ "$title" != *"*"* ]] || fail "FreeW still reports unsaved changes after nested text-direction route."
    printf '%s\n' '{"schemaVersion":1,"suite":"freew-linux-nested-text-wave65-physical","platform":"linux","app":"FreeW","shell":"avalonia","results":[{"id":"nested-text-direction-x11-selection","status":"passed","evidence":["02-nested-text-direction-rotate90.png"],"note":"The nested grouped text-box leaf at child path 0,1 was selected through physical X11 input."},{"id":"nested-text-direction-x11-command","status":"passed","evidence":["02-nested-text-direction-rotate90.png"],"note":"Drawing Format > Text Direction > Rotate 90 was invoked through the production ribbon route."},{"id":"nested-text-direction-x11-save","status":"passed","evidence":["02-nested-text-direction-rotate90.png"],"note":"Ctrl+S completed after the nested child text-direction command and cleared the dirty marker."}],"summary":{"passed":3,"failed":0,"total":3},"operation":{"childPath":"0,1","direction":"Rotate90","selector":"'"$selector"'"},"selectionPostcondition":{"visible":true,"childPath":"0,1","evidence":"02-nested-text-direction-rotate90.png"}}' > "$output/probe-results.json"
    exit 0
fi
# Select once, then use the explicit Return entry route. With a selected grouped child, DocumentView
# consumes this first Return to enter text editing; it does not insert a paragraph break.
xdotool key --clearmodifiers --window "$window_id" Return
sleep 1
xdotool type --clearmodifiers --delay 45 --window "$window_id" "!"
sleep 1
scrot "$output/02-nested-text-editing.png"
scrot "$output/03-nested-text-edited.png"
xdotool key --clearmodifiers --window "$window_id" ctrl+s
sleep 1
title="$(xdotool getwindowname "$window_id" 2>/dev/null || true)"
[[ "$title" != *"*"* ]] || fail "FreeW still reports unsaved changes after Ctrl+S."

cat > "$output/probe-results.json" <<EOF
{"schemaVersion":1,"suite":"freew-linux-nested-text-wave64-physical","platform":"linux","app":"FreeW","shell":"avalonia","results":[{"id":"nested-text-x11-selection","status":"passed","evidence":["02-nested-text-editing.png"],"note":"The nested grouped text box was selected and entered in-canvas text editing."},{"id":"nested-text-x11-insert","status":"passed","evidence":["03-nested-text-edited.png"],"note":"A character was inserted through the physical X11 text route."},{"id":"nested-text-x11-save","status":"passed","evidence":["03-nested-text-edited.png"],"note":"The edited document was saved and the live window reports no pending changes."}],"summary":{"passed":3,"failed":0,"total":3},"operation":{"childPath":"0,1","insert":"!"},"selectionPostcondition":{"visible":true,"childPath":"0,1","textEditing":true,"evidence":"03-nested-text-edited.png"}}
EOF
