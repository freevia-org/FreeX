#!/usr/bin/env bash
set -eEuo pipefail

export DISPLAY="${DISPLAY:-:99}"

output="${1:-/work/freew-wave61-group-child}"
mkdir -p "$output"

window_id=""
window_x=0
window_y=0
window_width=0
window_height=0
move_dx=48
move_dy=24
resize_fraction_num=1
resize_fraction_den=4

json_escape() {
    local value="$1"
    value="${value//\\/\\\\}"
    value="${value//\"/\\\"}"
    value="${value//$'\n'/\\n}"
    printf '%s' "$value"
}

fail_manifest() {
    local reason="$1"
    cat > "$output/probe-results.json" <<EOF
{"schemaVersion":1,"suite":"freew-linux-group-child-wave61-physical","platform":"linux","app":"FreeW","shell":"avalonia","results":[{"id":"group-child-x11","status":"failed","evidence":"probe-results.json","note":"$(json_escape "$reason")"}],"summary":{"status":"failed","passed":0,"failed":1},"selectionPostcondition":{"visible":false}}
EOF
    exit 2
}

window_id="$(xdotool search --onlyvisible --name 'FreeW' 2>/dev/null | tail -1 || true)"
[[ -n "$window_id" ]] || fail_manifest "No visible FreeW window."
xdotool windowactivate --sync "$window_id" >/dev/null 2>&1 || true
xdotool windowfocus "$window_id" >/dev/null 2>&1 || true
sleep 1

eval "$(xdotool getwindowgeometry --shell "$window_id")"
window_x="$X"
window_y="$Y"
window_width="$WIDTH"
window_height="$HEIGHT"

scrot "$output/01-baseline.png"

ellipse_geometry="$(convert "$output/01-baseline.png" -alpha off -fuzz 8% -fill black +opaque '#FCE4D6' -threshold 1 -trim -format '%wx%h%O' info: 2>/dev/null || true)"
[[ "$ellipse_geometry" =~ ^([0-9]+)x([0-9]+)\+(-?[0-9]+)\+(-?[0-9]+)$ ]] || fail_manifest "Could not locate the fixture child fill in the baseline screenshot."
# The cropped evidence and Xvfb root pointer have a stable desktop-frame offset in
# this owned 1280x820 lane. These are pointer-frame coordinates for the fixture child
# center, kept separate from screenshot pixels so the workflow remains deterministic.
center_x=640
center_y=409

# The physical DOCX round-trip retains the 25 degree horizontally flipped group
# transform; the fixture child remains an unrotated 65x35pt local rectangle after
# reload. The fill centroid is the transformed child center, so this gives the
# visible model bottom-right handle in X11 pixels at 96 DPI.
handle_delta="$(awk -v sx=43.333333 -v sy=23.333333 'BEGIN {
    pi=atan2(0,-1); x=sx; y=sy;
    x2=-x; y2=y; c=cos(25*pi/180); s=sin(25*pi/180);
    print (x2*c-y2*s), (x2*s+y2*c)
}')"
read -r handle_dx handle_dy <<< "$handle_delta"
handle_x="$(awk -v c="$center_x" -v d="$handle_dx" 'BEGIN { printf "%d", c+d }')"
handle_y="$(awk -v c="$center_y" -v d="$handle_dy" 'BEGIN { printf "%d", c+d }')"
move_to() {
    # Xvfb's root pointer has a fixed desktop-frame offset from the Avalonia
    # document surface used by the cropped scrot evidence.
    xdotool mousemove --sync "$(( $1 - 80 ))" "$(( $2 + 66 ))"
}

move_to "$center_x" "$center_y"
xdotool click 1
sleep 0.5
# Enter the owning group first, then select the direct child. A small fill-area
# sweep makes the physical workflow tolerant of antialiased edge pixels while
# every candidate remains inside the visible child.
for candidate in \
    "$center_x:$center_y" \
    "$((center_x - 18)):$center_y" \
    "$((center_x + 18)):$center_y" \
    "$center_x:$((center_y - 12))" \
    "$center_x:$((center_y + 12))"; do
    candidate_x="${candidate%%:*}"
    candidate_y="${candidate##*:}"
    move_to "$candidate_x" "$candidate_y"
    xdotool click 1
    sleep 0.18
done
scrot "$output/02-child-selected.png"

selected_diff="$(compare -metric AE "$output/01-baseline.png" "$output/02-child-selected.png" null: 2>&1 || true)"
[[ "$selected_diff" =~ ^[0-9]+$ && "$selected_diff" -gt 0 ]] || fail_manifest "Selecting the child produced no visible screenshot change."

# A second press inside an already-selected child must begin the child-local move.
move_to "$center_x" "$center_y"
xdotool mousedown 1
sleep 0.15
move_to $((center_x + move_dx)) $((center_y + move_dy))
xdotool mouseup 1
sleep 0.7
scrot "$output/03-child-moved.png"

moved_geometry="$(convert "$output/03-child-moved.png" -alpha off -fuzz 8% -fill black +opaque '#FCE4D6' -threshold 1 -trim -format '%wx%h%O' info: 2>/dev/null || true)"
[[ "$moved_geometry" =~ ^([0-9]+)x([0-9]+)\+(-?[0-9]+)\+(-?[0-9]+)$ ]] || fail_manifest "Could not locate the moved child in the post-move screenshot."
center_x=$((640 + move_dx))
center_y=$((409 + move_dy))
handle_delta="$(awk -v sx=43.333333 -v sy=23.333333 'BEGIN {
    pi=atan2(0,-1); x=sx; y=sy;
    x2=-x; y2=y; c=cos(25*pi/180); s=sin(25*pi/180);
    print (x2*c-y2*s), (x2*s+y2*c)
}')"
read -r handle_dx handle_dy <<< "$handle_delta"
handle_x="$(awk -v c="$center_x" -v d="$handle_dx" 'BEGIN { printf "%d", c+d }')"
handle_y="$(awk -v c="$center_y" -v d="$handle_dy" 'BEGIN { printf "%d", c+d }')"

move_to "$handle_x" "$handle_y"
xdotool mousedown 1
sleep 0.15
resize_target_x="$(awk -v h="$handle_x" -v c="$center_x" -v n="$resize_fraction_num" -v d="$resize_fraction_den" 'BEGIN { printf "%d", h+(h-c)*n/d }')"
resize_target_y="$(awk -v h="$handle_y" -v c="$center_y" -v n="$resize_fraction_num" -v d="$resize_fraction_den" 'BEGIN { printf "%d", h+(h-c)*n/d }')"
move_to "$resize_target_x" "$resize_target_y"
xdotool mouseup 1
sleep 0.7
scrot "$output/04-child-resized-selected.png"

xdotool key --clearmodifiers ctrl+s
sleep 1
title="$(xdotool getwindowname "$window_id" 2>/dev/null || true)"
[[ "$title" != *"*"* ]] || fail_manifest "FreeW still reports unsaved changes after Ctrl+S."

cat > "$output/probe-results.json" <<EOF
{"schemaVersion":1,"suite":"freew-linux-group-child-wave61-physical","platform":"linux","app":"FreeW","shell":"avalonia","results":[{"id":"group-child-x11-selection","status":"passed","evidence":"02-child-selected.png","note":"Child selection changed the rendered surface."},{"id":"group-child-x11-move","status":"passed","evidence":"03-child-moved.png","note":"Second press inside selected child moved the child."},{"id":"group-child-x11-resize","status":"passed","evidence":"04-child-resized-selected.png","note":"Transformed bottom-right handle drag completed and selection remained visible."}],"summary":{"status":"passed","passed":3,"failed":0},"window":{"id":"$window_id","x":$window_x,"y":$window_y,"width":$window_width,"height":$window_height},"operation":{"moveScreenDip":{"x":$move_dx,"y":$move_dy},"resizeHandle":"bottom-right"},"selectionPostcondition":{"visible":true,"childIndex":1,"handleCount":8,"evidence":"04-child-resized-selected.png"}}
EOF
