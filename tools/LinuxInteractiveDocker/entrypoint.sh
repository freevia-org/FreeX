#!/usr/bin/env bash
set -euo pipefail

export DISPLAY=:99
export LIBGL_ALWAYS_SOFTWARE=1
export NO_AT_BRIDGE=1

screen_width="${SCREEN_WIDTH:-1280}"
screen_height="${SCREEN_HEIGHT:-820}"
screen_dpi="${SCREEN_DPI:-96}"
app_executable="${APP_EXECUTABLE:-FreeX}"
app_window_title="${APP_WINDOW_TITLE:-FreeX}"
app_document="${APP_DOCUMENT:-}"
app_arguments_b64="${APP_ARGUMENTS_B64:-}"

mkdir -p /work/logs /work/screenshots
rm -f /work/ready.json /work/failure.json

declare -a child_pids=()

cleanup() {
    for pid in "${child_pids[@]:-}"; do
        kill "$pid" 2>/dev/null || true
    done
}
trap cleanup EXIT INT TERM

Xvfb :99 \
    -screen 0 "${screen_width}x${screen_height}x24" \
    -dpi "$screen_dpi" \
    -nolisten tcp \
    > /work/logs/xvfb.log 2>&1 &
child_pids+=("$!")

sleep 1
eval "$(dbus-launch --sh-syntax)"
openbox > /work/logs/openbox.log 2>&1 &
child_pids+=("$!")
xsetroot -solid "#d9e1e8"

picom \
    --backend xrender \
    > /work/logs/picom.log 2>&1 &
child_pids+=("$!")

x11vnc \
    -display :99 \
    -forever \
    -shared \
    -nopw \
    -localhost \
    -rfbport 5900 \
    -afteraccept /usr/local/bin/freex-refresh-after-vnc \
    -o /work/logs/x11vnc.log \
    > /dev/null 2>&1 &
child_pids+=("$!")

websockify \
    --web=/usr/share/novnc \
    6080 \
    localhost:5900 \
    > /work/logs/novnc.log 2>&1 &
child_pids+=("$!")

cd /opt/published
declare -a app_arguments=()
if [[ -n "$app_arguments_b64" ]]; then
    mapfile -t app_arguments < <(printf '%s' "$app_arguments_b64" | base64 -d)
fi
if [[ -n "$app_document" ]]; then
    app_arguments+=("$app_document")
fi
"./$app_executable" "${app_arguments[@]}" > /work/logs/app.log 2>&1 &
app_pid=$!
child_pids+=("$app_pid")

window_id=""
for _ in $(seq 1 60); do
    if ! kill -0 "$app_pid" 2>/dev/null; then
        break
    fi

    window_id="$(xdotool search --onlyvisible --name "$app_window_title" 2>/dev/null | tail -1 || true)"
    if [[ -n "$window_id" ]]; then
        break
    fi
    sleep 1
done

if [[ -z "$window_id" ]]; then
    cat > /work/failure.json <<JSON
{
  "status": "failed",
  "reason": "No visible $app_window_title window appeared within 60 seconds.",
  "appExecutable": "$app_executable"
}
JSON
    tail -100 /work/logs/app.log >&2 || true
    exit 1
fi

wmctrl -ir "$window_id" -b add,maximized_vert,maximized_horz || true
xdotool windowactivate --sync "$window_id" || true
sleep 1
scrot /work/screenshots/initial.png
window_name="$(xdotool getwindowname "$window_id" 2>/dev/null || printf '%s' "$app_window_title")"

cat > /work/ready.json <<JSON
{
  "status": "ready",
  "appExecutable": "$app_executable",
  "windowId": "$window_id",
  "windowTitle": "$window_name",
  "display": ":99",
  "screen": "${screen_width}x${screen_height}",
  "dpi": $screen_dpi,
  "initialScreenshot": "screenshots/initial.png"
}
JSON

wait "$app_pid"
