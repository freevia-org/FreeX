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

# The foreground print probe uses the production CUPS adapter with an owned, container-local
# dry-run queue. The shim has the same lpstat/lp process boundary but only copies the generated
# PDF into the mounted session directory; it never reaches a host printer or device.
if [[ "${FREEX_CUPS_DRY_RUN:-0}" == "1" ]]; then
    cups_dry_run_bin="/tmp/freex-cups-dry-run"
    cups_dry_run_output="/work/cups-dry-run"
    cups_dry_run_queue="${app_window_title}-DryRun"
    mkdir -p "$cups_dry_run_bin" "$cups_dry_run_output"
    export FREEX_CUPS_DRY_RUN_QUEUE="$cups_dry_run_queue"
    cat > "$cups_dry_run_bin/lpstat" <<'SH'
#!/usr/bin/env bash
set -euo pipefail
case " $* " in
    *" -p "*) printf 'printer %s is idle.\n' "${FREEX_CUPS_DRY_RUN_QUEUE}" ;;
    *" -d "*) printf 'system default destination: %s\n' "${FREEX_CUPS_DRY_RUN_QUEUE}" ;;
    *) printf 'FreeW dry-run lpstat received unsupported arguments: %s\n' "$*" >&2; exit 2 ;;
esac
SH
    cat > "$cups_dry_run_bin/lp" <<'SH'
#!/usr/bin/env bash
set -euo pipefail
pdf_path="${@: -1}"
if [[ ! -f "$pdf_path" ]]; then
    printf 'FreeW dry-run lp received no PDF path.\n' >&2
    exit 2
fi
mkdir -p /work/cups-dry-run
printf 'lp ' > /work/cups-dry-run/last-invocation.txt
printf '%q ' "$@" >> /work/cups-dry-run/last-invocation.txt
printf '\n' >> /work/cups-dry-run/last-invocation.txt
if [[ "${FREEX_CUPS_DRY_RUN_MODE:-success}" == "failure" ]]; then
    printf 'FreeW dry-run backend rejected the job.\n' > /work/cups-dry-run/last-error.txt
    printf 'FreeW dry-run backend rejected the job.\n' >&2
    exit 1
fi
cp -- "$pdf_path" /work/cups-dry-run/last-submitted.pdf
printf 'request id is %s-1 (1 file(s))\n' "${FREEX_CUPS_DRY_RUN_QUEUE}"
SH
    chmod 0755 "$cups_dry_run_bin/lpstat" "$cups_dry_run_bin/lp"
    export PATH="$cups_dry_run_bin:$PATH"
fi

declare -a app_arguments=()
if [[ -n "$app_arguments_b64" ]]; then
    mapfile -t app_arguments < <(printf '%s' "$app_arguments_b64" | base64 -d)
fi
interaction_validation=false
physical_validation=false
read_aloud_pause_validation=false
for argument in "${app_arguments[@]}"; do
    if [[ "$argument" == "--interaction-validation" ]]; then
        interaction_validation=true
        break
    fi
    if [[ "$argument" == "--physical-validation" || "$argument" == --physical-validation=* ]]; then
        physical_validation=true
        break
    fi
    if [[ "$argument" == "--read-aloud-pause-smoke" ]]; then
        read_aloud_pause_validation=true
        break
    fi
done
if [[ -n "$app_document" ]]; then
    app_arguments+=("$app_document")
fi
"./$app_executable" "${app_arguments[@]}" > /work/logs/app.log 2>&1 &
app_pid=$!
child_pids+=("$app_pid")

# Interaction validation is intentionally headless and can finish before X11 observes a window.
# Publish readiness immediately so the host can wait on the mounted manifest, then retain the
# desktop container until the orchestrator has collected the result and stops this owned session.
if [[ "$interaction_validation" == true || "$physical_validation" == true || "$read_aloud_pause_validation" == true ]]; then
    cat > /work/ready.json <<JSON
{
  "status": "ready",
  "appExecutable": "$app_executable",
  "windowId": "",
  "windowTitle": "$app_window_title validation",
  "display": ":99",
  "screen": "${screen_width}x${screen_height}",
  "dpi": $screen_dpi,
  "initialScreenshot": ""
}
JSON

    set +e
    wait "$app_pid"
    app_exit=$?
    set -e
    if [[ $app_exit -ne 0 && "$read_aloud_pause_validation" == true ]]; then
        cat > /work/failure.json <<JSON
{
  "status": "failed",
  "reason": "$app_executable read-aloud pause smoke exited with code $app_exit.",
  "appExecutable": "$app_executable"
}
JSON
    elif [[ $app_exit -ne 0 && ! -s /work/validation/interaction-validation.json ]]; then
        cat > /work/failure.json <<JSON
{
  "status": "failed",
  "reason": "$app_executable interaction validation exited with code $app_exit before writing a manifest.",
  "appExecutable": "$app_executable"
}
JSON
    fi
    while true; do
        sleep 3600
    done
fi

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
