#!/usr/bin/env bash
set -euo pipefail

export DISPLAY="${DISPLAY:-:99}"

# Avalonia can retain stale X11 damage regions when the first VNC client starts
# watching the display. Toggling maximization forces a complete expose/repaint.
sleep 1
window_id="$(xdotool search --onlyvisible --name "${APP_WINDOW_TITLE:-FreeX}" 2>/dev/null | tail -1 || true)"
if [[ -z "$window_id" ]]; then
    exit 0
fi

wmctrl -ir "$window_id" -b remove,maximized_vert,maximized_horz || true
sleep 0.25
wmctrl -ir "$window_id" -b add,maximized_vert,maximized_horz || true
xdotool windowactivate "$window_id" 2>/dev/null || true
