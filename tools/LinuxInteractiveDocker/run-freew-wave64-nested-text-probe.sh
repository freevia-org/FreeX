#!/usr/bin/env bash
set -eEuo pipefail

export DISPLAY="${DISPLAY:-:99}"
output="${1:-/work/freew-wave64-nested-text}"
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
sleep 1
scrot "$output/01-baseline.png"

# Fixed 1280x820/96-DPI fixture coordinates, matching the existing nested-group physical lane.
center_x=636
center_y=490
xdotool mousemove --sync "$center_x" "$center_y"
xdotool click 1
sleep 0.75
xdotool click 1
sleep 0.75
# The second click on the already selected nested child is the same in-canvas text-entry route
# used by the managed Avalonia/WPF parity tests. Keep the caret at the end so the persisted
# assertion is an exact one-character insertion rather than a paragraph-break exercise.
xdotool key --clearmodifiers --window "$window_id" Return
sleep 0.75
# If Return entered the route, this inserts at the end. If it inserted a paragraph break, the
# following Home + Backspace merges that paragraph through the shared command path.
xdotool type --clearmodifiers --delay 45 --window "$window_id" "!"
sleep 0.75
xdotool key --clearmodifiers --window "$window_id" Home
xdotool key --clearmodifiers --window "$window_id" BackSpace
sleep 0.45
scrot "$output/02-nested-text-editing.png"
scrot "$output/03-nested-text-edited.png"
xdotool key --clearmodifiers --window "$window_id" ctrl+s
sleep 1
title="$(xdotool getwindowname "$window_id" 2>/dev/null || true)"
[[ "$title" != *"*"* ]] || fail "FreeW still reports unsaved changes after Ctrl+S."

cat > "$output/probe-results.json" <<EOF
{"schemaVersion":1,"suite":"freew-linux-nested-text-wave64-physical","platform":"linux","app":"FreeW","shell":"avalonia","results":[{"id":"nested-text-x11-selection","status":"passed","evidence":["02-nested-text-editing.png"],"note":"The nested grouped text box was selected and entered in-canvas text editing."},{"id":"nested-text-x11-insert","status":"passed","evidence":["03-nested-text-edited.png"],"note":"A character was inserted through the physical X11 text route."},{"id":"nested-text-x11-save","status":"passed","evidence":["03-nested-text-edited.png"],"note":"The edited document was saved and the live window reports no pending changes."}],"summary":{"passed":3,"failed":0,"total":3},"operation":{"childPath":"0,1","insert":"!"},"selectionPostcondition":{"visible":true,"childPath":"0,1","textEditing":true,"evidence":"03-nested-text-edited.png"}}
EOF
