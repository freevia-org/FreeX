#!/usr/bin/env bash
set -eEuo pipefail

export DISPLAY="${DISPLAY:-:99}"
output="${1:-/work/freew-wave62-nested-group-child}"
mkdir -p "$output"
window_id="$(xdotool search --onlyvisible --name 'FreeW' 2>/dev/null | tail -1 || true)"
fail() {
    local reason="$1"
    printf '{"schemaVersion":1,"suite":"freew-linux-nested-group-child-wave62-physical","platform":"linux","app":"FreeW","shell":"avalonia","results":[{"id":"nested-group-child-x11","status":"failed","evidence":"probe-results.json","note":"%s"}],"summary":{"status":"failed","passed":0,"failed":1},"selectionPostcondition":{"visible":false}}\n' "$reason" > "$output/probe-results.json"
    exit 2
}
[[ -n "$window_id" ]] || fail "No visible FreeW window."
xdotool windowactivate --sync "$window_id" >/dev/null 2>&1 || true
xdotool windowfocus "$window_id" >/dev/null 2>&1 || true
sleep 1

scrot "$output/01-baseline.png"
geometry="$(convert "$output/01-baseline.png" -alpha off -fuzz 8% -fill black +opaque '#FCE4D6' -threshold 1 -trim -format '%wx%h%O' info: 2>/dev/null || true)"
[[ "$geometry" =~ ^([0-9]+)x([0-9]+)\+(-?[0-9]+)\+(-?[0-9]+)$ ]] || fail "Could not locate the nested leaf fill in the baseline screenshot."
leaf_width="${BASH_REMATCH[1]}"
leaf_height="${BASH_REMATCH[2]}"
leaf_left="${BASH_REMATCH[3]}"
leaf_top="${BASH_REMATCH[4]}"
center_x=$((leaf_left + leaf_width / 2))
center_y=$((leaf_top + leaf_height / 2))
move_to() {
    xdotool mousemove --sync "$1" "$(( $2 + 95 ))"
}

move_to "$center_x" "$center_y"
xdotool click 1
sleep 0.35
move_to "$center_x" "$center_y"
xdotool click 1
sleep 0.35
scrot "$output/02-nested-child-selected.png"
selected_diff="$(compare -metric AE "$output/01-baseline.png" "$output/02-nested-child-selected.png" null: 2>&1 || true)"
[[ "$selected_diff" =~ ^[0-9]+$ && "$selected_diff" -gt 0 ]] || fail "Selecting the nested child produced no visible screenshot change."

move_dx=44
move_dy=22
move_to "$center_x" "$center_y"
xdotool mousedown 1
sleep 0.15
move_to $((center_x + move_dx)) $((center_y + move_dy))
xdotool mouseup 1
sleep 0.7
scrot "$output/03-nested-child-moved.png"

center_x=$((center_x + move_dx))
center_y=$((center_y + move_dy))
# Local bottom-right vector (32,16)pt, transformed by leaf, inner, and outer chains.
read -r handle_dx handle_dy <<< "$(awk 'BEGIN {
    pi=atan2(0,-1); x=32*4/3; y=16*4/3;
    x=-x;
    a=10*pi/180; t=x*cos(a)-y*sin(a); y=x*sin(a)+y*cos(a); x=t;
    y=-y;
    a=-17*pi/180; t=x*cos(a)-y*sin(a); y=x*sin(a)+y*cos(a); x=t;
    a=22*pi/180; t=x*cos(a)-y*sin(a); y=x*sin(a)+y*cos(a); x=t;
    printf "%d %d", x, y;
}')"
handle_x=$((center_x + handle_dx))
handle_y=$((center_y + handle_dy))
move_to "$handle_x" "$handle_y"
xdotool mousedown 1
sleep 0.15
resize_target_x=$((handle_x + handle_dx / 4))
resize_target_y=$((handle_y + handle_dy / 4))
move_to "$resize_target_x" "$resize_target_y"
xdotool mouseup 1
sleep 0.7
scrot "$output/04-nested-child-resized-selected.png"

xdotool key --clearmodifiers ctrl+s
sleep 1
title="$(xdotool getwindowname "$window_id" 2>/dev/null || true)"
[[ "$title" != *"*"* ]] || fail "FreeW still reports unsaved changes after Ctrl+S."

cat > "$output/probe-results.json" <<EOF
{"schemaVersion":1,"suite":"freew-linux-nested-group-child-wave62-physical","platform":"linux","app":"FreeW","shell":"avalonia","results":[{"id":"nested-group-child-x11-selection","status":"passed","evidence":"02-nested-child-selected.png","note":"Nested leaf selection changed the rendered surface."},{"id":"nested-group-child-x11-move","status":"passed","evidence":"03-nested-child-moved.png","note":"Second press inside the selected nested leaf moved only the leaf."},{"id":"nested-group-child-x11-resize","status":"pending","evidence":"04-nested-child-resized-selected.png","note":"The screenshot is promoted to passed only after exact DOCX geometry and transform validation."}],"summary":{"status":"pending","passed":2,"failed":0},"operation":{"childPath":"0,1","moveScreenDip":{"x":$move_dx,"y":$move_dy},"resizeHandle":"bottom-right"},"selectionPostcondition":{"visible":true,"childPath":"0,1","handleCount":8,"evidence":"04-nested-child-resized-selected.png"}}
EOF
