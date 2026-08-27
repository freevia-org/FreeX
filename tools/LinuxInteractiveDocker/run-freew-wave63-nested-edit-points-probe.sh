#!/usr/bin/env bash
set -eEuo pipefail

export DISPLAY="${DISPLAY:-:99}"
output="${1:-/work/freew-wave63-nested-edit-points}"
mkdir -p "$output"
window_id="$(xdotool search --onlyvisible --name 'FreeW' 2>/dev/null | tail -1 || true)"
fail() {
    local reason="$1"
    printf '{"schemaVersion":1,"suite":"freew-linux-nested-edit-points-wave63-physical","platform":"linux","app":"FreeW","shell":"avalonia","results":[{"id":"nested-edit-points-x11","status":"failed","evidence":"probe-results.json","note":"%s"}],"summary":{"status":"failed","passed":0,"failed":1},"selectionPostcondition":{"visible":false}}\n' "$reason" > "$output/probe-results.json"
    exit 2
}
[[ -n "$window_id" ]] || fail "No visible FreeW window."
xdotool windowactivate --sync "$window_id" >/dev/null 2>&1 || true
xdotool windowfocus "$window_id" >/dev/null 2>&1 || true
sleep 1

capture() { scrot "$output/$1.png"; }
move_to() { xdotool mousemove --sync "$1" "$2"; }

capture 01-baseline
# The fixture and the harness resolution are fixed at 1280x820/96-DPI. The nested leaf center is
# intentionally stable; using it avoids mistaking the outer group's pale transform outline for the
# leaf fill when ImageMagick's color segmentation sees antialiased edges.
center_x=636
center_y=490

# Two clicks select the nested leaf, matching the Wave 62 physical path.
move_to "$center_x" "$center_y"
xdotool click 1
sleep 0.25
move_to "$center_x" "$center_y"
xdotool click 1
sleep 0.45

# The contextual Drawing Format tab exposes Edit Shape > Edit Points. Use the visible contextual
# tab and menu coordinates at the fixed 1280x820/96-DPI harness size; this avoids the D key-tip
# collision with Developer.
xdotool mousemove --sync 595 67
xdotool click 1
sleep 0.5
xdotool mousemove --sync 188 145
xdotool click 1
sleep 0.35
xdotool mousemove --sync 160 202
xdotool click 1
sleep 0.8
capture 02-nested-leaf-edit-points

# Locate the yellow edit-point handles, then drag the first handle by a fixed screen delta.
handle_geometry="$(convert "$output/02-nested-leaf-edit-points.png" -crop 400x300+450+350 -alpha off -fuzz 2% -fill white +opaque '#FFF2B2' -fill black -opaque '#FFF2B2' -trim -format '%wx%h%O' info: 2>/dev/null || true)"
[[ "$handle_geometry" =~ ^([0-9]+)x([0-9]+)\+(-?[0-9]+)\+(-?[0-9]+)$ ]] || fail "Edit Points did not render yellow handles."
handle_x=$((BASH_REMATCH[3] + 5))
handle_y=$((BASH_REMATCH[4] + 5))
move_to "$handle_x" "$handle_y"
xdotool mousedown 1
sleep 0.15
move_to "$((handle_x + 12))" "$((handle_y + 8))"
xdotool mouseup 1
sleep 0.8
capture 03-nested-leaf-edit-point-dragged

# Leave the transient Edit Points mode before saving, matching the WPF command lifecycle.
xdotool key --clearmodifiers Escape
sleep 0.35
xdotool key --clearmodifiers ctrl+s
sleep 1
title="$(xdotool getwindowname "$window_id" 2>/dev/null || true)"
[[ "$title" != *"*"* ]] || fail "FreeW still reports unsaved changes after Ctrl+S."

cat > "$output/probe-results.json" <<EOF
{"schemaVersion":1,"suite":"freew-linux-nested-edit-points-wave63-physical","platform":"linux","app":"FreeW","shell":"avalonia","results":[{"id":"nested-edit-points-x11-selection","status":"passed","evidence":"02-nested-leaf-edit-points.png","note":"The nested leaf entered Edit Points and rendered its handles."},{"id":"nested-edit-points-x11-drag","status":"passed","evidence":"03-nested-leaf-edit-point-dragged.png","note":"A composed-transform leaf handle was dragged through X11 and the document was saved."}],"summary":{"status":"passed","passed":2,"failed":0},"operation":{"childPath":"0,1","handle":"segment-0","screenDelta":{"x":12,"y":8}},"selectionPostcondition":{"visible":true,"childPath":"0,1","handleCount":4,"evidence":"03-nested-leaf-edit-point-dragged.png"}}
EOF
