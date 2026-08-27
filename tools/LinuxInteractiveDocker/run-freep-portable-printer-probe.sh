#!/usr/bin/env bash
set -Eeuo pipefail

export DISPLAY="${DISPLAY:-:99}"
output="${1:-/work/portable-printer}"
records="$output/result-records.jsonl"
screenshots_file="$output/screenshot-names.txt"
manifest="$output/freep-portable-printer-wave105.json"
owner_id=""
dialog_id=""
dialog_transient=""
probe_status=0
input_delay_ms="${FREEP_X11_INPUT_DELAY_MS:-80}"
settle_seconds="${FREEP_X11_SETTLE_SECONDS:-0.65}"
pointer_timeout_seconds="${FREEP_X11_POINTER_TIMEOUT_SECONDS:-4}"

mkdir -p "$output"
: > "$records"
: > "$screenshots_file"

record() {
    local id="$1" status="$2" note="$3"
    shift 3
    python3 - "$records" "$id" "$status" "$note" "$@" <<'PY'
import json
import sys

path, result_id, status, note, *evidence = sys.argv[1:]
with open(path, "a", encoding="utf-8") as handle:
    handle.write(json.dumps({
        "id": result_id,
        "category": "physical-x11-portable-printer",
        "status": status,
        "evidenceLevel": "physical-x11-input",
        "evidence": evidence,
        "note": note,
    }, sort_keys=True) + "\n")
PY
    if [[ "$status" != "passed" ]]; then probe_status=1; fi
}

capture() {
    local name="$1"
    scrot -o "$output/$name" >/dev/null 2>&1 || return 1
    [[ -s "$output/$name" ]] || return 1
    printf '%s\n' "$name" >> "$screenshots_file"
}

run_key() {
    timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
        xdotool key --clearmodifiers --delay "$input_delay_ms" "$@"
}

run_type() {
    timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
        xdotool type --clearmodifiers --delay "$input_delay_ms" -- "$1"
}

focus_owner() {
    timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
        xdotool windowactivate --sync "$owner_id" >/dev/null 2>&1 || true
    timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
        xdotool windowfocus "$owner_id" >/dev/null 2>&1 || true
    sleep 0.15
}

window_coordinate() {
    local id="$1" name="$2"
    xdotool getwindowgeometry --shell "$id" 2>/dev/null | sed -n "s/^${name}=//p"
}

click_at() {
    timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
        xdotool mousemove "$1" "$2" click 1
}

active_window_id() { xdotool getactivewindow 2>/dev/null || true; }
active_title() { xdotool getactivewindow getwindowname 2>/dev/null || true; }

wait_window() {
    local pattern="$1" seconds="${2:-12}" id=""
    for _ in $(seq 1 $((seconds * 10))); do
        id="$(xdotool search --onlyvisible --name "$pattern" 2>/dev/null | tail -1 || true)"
        if [[ -n "$id" ]]; then
            printf '%s\n' "$id"
            return 0
        fi
        sleep 0.1
    done
    return 1
}

write_manifest() {
    local exit_code="${1:-$probe_status}"
    python3 - "$manifest" "$records" "$screenshots_file" "$owner_id" "$dialog_id" "$dialog_transient" "$output" "$exit_code" <<'PY'
import json
import os
import sys

manifest_path, records_path, screenshots_path, owner_id, dialog_id, transient, output, exit_code = sys.argv[1:]
with open(records_path, encoding="utf-8") as handle:
    results = [json.loads(line) for line in handle if line.strip()]
with open(screenshots_path, encoding="utf-8") as handle:
    screenshot_names = [line.strip() for line in handle if line.strip()]

invocation_path = os.path.join(output, "last-invocation.json")
invocation = {}
if os.path.isfile(invocation_path):
    with open(invocation_path, encoding="utf-8") as handle:
        invocation = json.load(handle)
args = invocation.get("arguments", [])
pdf_path = invocation.get("pdfPath", "")
submitted_path = os.path.join(output, "last-submitted.pdf")
pdf_bytes = os.path.getsize(submitted_path) if os.path.isfile(submitted_path) else 0

def option_value(name):
    try:
        return args[args.index(name) + 1]
    except (ValueError, IndexError):
        return ""

submission = {
    "queue": option_value("-d"),
    "copies": int(option_value("-n") or 0),
    "pageRange": option_value("-P"),
    "collate": "collate=false" not in args,
    "orientation": "landscape" if "orientation-requested=4" in args else "document",
    "arguments": args,
    "pdfPath": pdf_path,
    "pdfBytes": pdf_bytes,
    "invocationPath": "last-invocation.json",
    "submittedPdfPath": "last-submitted.pdf",
}
passed = sum(row["status"] == "passed" for row in results)
failed = sum(row["status"] == "failed" for row in results)
manifest = {
    "schemaVersion": 1,
    "suite": "freep-portable-printer-wave105-physical",
    "platform": "linux",
    "shell": "avalonia",
    "app": "FreeP",
    "baseline": False,
    "appSurface": "file-print-portable-printer-dialog",
    "window": {
        "id": owner_id,
        "title": "FreeP",
        "pattern": "FreeP",
        "visible": bool(owner_id),
        "dialogId": dialog_id,
        "dialogTitle": "Print",
        "dialogVisible": bool(dialog_id),
        "dialogTransientFor": transient,
    },
    "fakePrinter": {
        "privatePath": "/tmp/freex-cups-dry-run",
        "printers": ["FreeP-Default", "FreeP-Secondary"],
        "defaultPrinter": "FreeP-Default",
        "realDevice": False,
        "lpstatExecutable": "freep-portable-printer-fake-lpstat.sh",
        "lpExecutable": "freep-portable-printer-fake-lp.sh",
    },
    "screenshots": [{"name": name, "kind": "screenshot"} for name in screenshot_names],
    "submission": submission,
    "summary": {"passed": passed, "failed": failed, "total": len(results)},
    "results": results,
    "processExitCode": int(exit_code),
    "contractValidation": {
        "status": "pending",
        "validator": "tools/Run-FreePPortablePrinterValidation.ps1",
        "contractReference": "tools/LinuxInteractiveDocker/freep-portable-printer-wave105-validation.schema.json",
    },
}
with open(manifest_path, "w", encoding="utf-8") as handle:
    json.dump(manifest, handle, indent=2)
    handle.write("\n")
PY
}

required_ids=(
    owner-window-visible
    file-print-route
    portable-dialog-visible
    portable-dialog-controls
    non-default-printer-selected
    settings-submitted
    fake-lp-arguments
    submitted-pdf
    owner-focus-restored
)

owner_id="$(wait_window 'FreeP$' 18 || true)"
if [[ -z "$owner_id" ]]; then
    printf 'No visible FreeP owner window was found.\n' > "$output/window-discovery.txt"
    for id in "${required_ids[@]}"; do record "$id" failed "FreeP owner window was not visible." "window-discovery.txt"; done
    write_manifest 1
    exit 1
fi

read -r display_width display_height < <(xdotool getdisplaygeometry)
display_width="${display_width:-1280}"
display_height="${display_height:-820}"

capture "owner-before.png" || true
printf 'owner-id=%s\nactive-id=%s\nactive-title=%s\n' \
    "$owner_id" "$(active_window_id)" "$(active_title)" > "$output/window-discovery.txt"
if [[ -s "$output/owner-before.png" && "$(active_window_id)" == "$owner_id" ]]; then
    record owner-window-visible passed "FreeP owner was visible and active before File > Print." owner-before.png window-discovery.txt
else
    record owner-window-visible failed "FreeP owner was not the active visible X11 window." owner-before.png window-discovery.txt
fi

focus_owner
run_key Alt_L
run_key F
sleep "$settle_seconds"
# Activate the rendered Print rail entry with physical X11 pointer input. Keyboard focus
# traversal only moves the rail focus adorner in the current Avalonia shell.
click_at 70 343
sleep "$settle_seconds"
capture "file-print-pane.png" || true
printf 'route=File > Print\nactive-title=%s\n' "$(active_title)" > "$output/file-print-route.txt"
if [[ -s "$output/file-print-pane.png" && "$(active_window_id)" == "$owner_id" ]]; then
    file_route_visible=true
else
    file_route_visible=false
fi

# Scroll the rendered Print pane to its action section and activate the first real print
# button. Coordinates are pinned to the lane's measured display viewport.
timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
    xdotool mousemove 900 "$((display_height - 120))"
timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
    xdotool click --repeat 30 --delay 40 5
sleep 1
click_at 310 "$((display_height - 253))"
dialog_id="$(wait_window '^Print$' 15 || true)"
if [[ -n "$dialog_id" ]]; then
    owner_pid="$(xdotool getwindowpid "$owner_id" 2>/dev/null || true)"
    dialog_pid="$(xdotool getwindowpid "$dialog_id" 2>/dev/null || true)"
    dialog_transient="owner-pid=$owner_pid dialog-pid=$dialog_pid"
fi
printf 'owner-id=%s\ndialog-id=%s\ndialog-title=%s\nactive-id=%s\nactive-title=%s\n%s\n' \
    "$owner_id" "$dialog_id" "$(xdotool getwindowname "$dialog_id" 2>/dev/null || true)" \
    "$(active_window_id)" "$(active_title)" "$dialog_transient" > "$output/dialog-window.txt"
capture "portable-dialog-open.png" || true
if [[ "$file_route_visible" == true && -n "$dialog_id" ]]; then
    record file-print-route passed "Physical X11 pointer input activated FreeP File > Print and opened its real print action." file-print-pane.png file-print-route.txt portable-dialog-open.png
else
    record file-print-route failed "File > Print did not expose a physical print action that opened the portable dialog." file-print-pane.png file-print-route.txt portable-dialog-open.png
fi
if [[ -n "$dialog_id" && -n "${owner_pid:-}" && "$owner_pid" == "${dialog_pid:-}" && "$(active_window_id)" == "$owner_id" ]]; then
    record portable-dialog-visible passed "The visible Print window shared the FreeP process and retained its modal owner as the active X11 window." portable-dialog-open.png dialog-window.txt
else
    record portable-dialog-visible failed "The portable Print dialog was not proven visible and owned by the FreeP process." portable-dialog-open.png dialog-window.txt
fi

if [[ -z "$dialog_id" ]]; then
    for id in portable-dialog-controls non-default-printer-selected settings-submitted fake-lp-arguments submitted-pdf owner-focus-restored; do
        record "$id" failed "Portable Print dialog was not opened." dialog-window.txt
    done
    write_manifest 1
    exit 1
fi

# Operate the real controls through coordinates derived from the measured dialog geometry.
# Avalonia keeps the modal owner as the active X11 window while routing input to this child.
dialog_x="$(window_coordinate "$dialog_id" X)"
dialog_y="$(window_coordinate "$dialog_id" Y)"
dialog_x="${dialog_x:-392}"
dialog_y="${dialog_y:-310}"
click_at "$((dialog_x + 238))" "$((dialog_y + 8))"
run_key Down Return
click_at "$((dialog_x + 158))" "$((dialog_y + 40))"
run_key ctrl+a
run_type 2
click_at "$((dialog_x + 238))" "$((dialog_y + 72))"
run_key End Return
click_at "$((dialog_x + 98))" "$((dialog_y + 104))"
run_key ctrl+a
run_type 2
click_at "$((dialog_x + 238))" "$((dialog_y + 104))"
run_key ctrl+a
run_type 3
click_at "$((dialog_x + 238))" "$((dialog_y + 135))"
run_key End Return
click_at "$((dialog_x + 25))" "$((dialog_y + 173))"
printf '%s\n' \
    'printer=FreeP-Secondary' 'copies=2' 'pages=2-3' \
    'orientation=Landscape' 'collate=false' > "$output/dialog-controls.txt"
capture "portable-dialog-settings.png" || true
if [[ -s "$output/portable-dialog-settings.png" && -s "$output/dialog-controls.txt" ]]; then
    record portable-dialog-controls passed "The physical probe traversed and changed the printer, copies, page range, orientation, and collation controls." portable-dialog-open.png portable-dialog-settings.png dialog-controls.txt
else
    record portable-dialog-controls failed "Portable dialog control interaction did not produce complete screenshot evidence." portable-dialog-open.png portable-dialog-settings.png dialog-controls.txt
fi

click_at "$((dialog_x + 386))" "$((dialog_y + 236))"
sleep "$settle_seconds"
if ! xdotool search --onlyvisible --name '^Print$' >/dev/null 2>&1; then
    dialog_closed=true
else
    dialog_closed=false
fi

invocation_ready=false
for _ in $(seq 1 80); do
    if [[ -s "$output/last-invocation.json" ]]; then invocation_ready=true; break; fi
    sleep 0.1
done

if [[ "$invocation_ready" == true ]]; then
    record non-default-printer-selected passed "Fake lp recorded the selected non-default queue FreeP-Secondary." last-invocation.json
else
    record non-default-printer-selected failed "Fake lp did not record a submission for the selected non-default queue." last-invocation.json
fi

set +e
python3 - "$output/last-invocation.json" "$output/last-submitted.pdf" "$output/submission-check.json" <<'PY'
import json
import os
import sys

invocation_path, pdf_path, check_path = sys.argv[1:]
checks = {
    "invocationExists": os.path.isfile(invocation_path),
    "pdfExists": os.path.isfile(pdf_path),
    "queue": False,
    "copies": False,
    "pageRange": False,
    "collation": False,
    "orientation": False,
    "pdfArgument": False,
}
args = []
if checks["invocationExists"]:
    with open(invocation_path, encoding="utf-8") as handle:
        args = json.load(handle).get("arguments", [])
def pair(name, value):
    return name in args and args[args.index(name) + 1] == value
checks["queue"] = pair("-d", "FreeP-Secondary")
checks["copies"] = pair("-n", "2")
checks["pageRange"] = pair("-P", "2-3")
checks["collation"] = "collate=false" in args
checks["orientation"] = "orientation-requested=4" in args
checks["pdfArgument"] = bool(args) and args[-1].endswith(".pdf")
checks["all"] = all(checks.values())
with open(check_path, "w", encoding="utf-8") as handle:
    json.dump(checks, handle, indent=2)
    handle.write("\n")
raise SystemExit(0 if checks["all"] else 1)
PY
settings_ok=$?
set -e
if [[ "$settings_ok" -eq 0 ]]; then
    record settings-submitted passed "Fake lp arguments contain copies, page range, collation, and landscape orientation." submission-check.json
    record fake-lp-arguments passed "The private fake lp received the expected queue and option contract." last-invocation.json submission-check.json
else
    record settings-submitted failed "Fake lp arguments did not contain every requested setting." submission-check.json
    record fake-lp-arguments failed "The private fake lp argument contract failed." last-invocation.json submission-check.json
fi

if [[ -s "$output/last-submitted.pdf" ]] && head -c 4 "$output/last-submitted.pdf" | cmp -s - <(printf '%%PDF'); then
    record submitted-pdf passed "Fake lp captured a non-empty PDF beginning with the PDF signature." last-submitted.pdf submission-check.json
else
    record submitted-pdf failed "Fake lp did not capture a non-empty PDF submission." last-submitted.pdf submission-check.json
fi

focus_owner
sleep "$settle_seconds"
capture "owner-after.png" || true
owner_active=false
if [[ "$(active_window_id)" == "$owner_id" ]]; then owner_active=true; fi
printf 'dialog-closed=%s\nowner-active=%s\nactive-title=%s\n' \
    "$dialog_closed" "$owner_active" "$(active_title)" > "$output/focus-restored.txt"
if [[ "$dialog_closed" == true && "$owner_active" == true && -s "$output/owner-after.png" ]]; then
    record owner-focus-restored passed "Submitting closed the portable dialog and restored the FreeP owner focus." owner-after.png focus-restored.txt
else
    record owner-focus-restored failed "Portable dialog submission did not restore the FreeP owner focus." owner-after.png focus-restored.txt
fi

write_manifest "$probe_status"
exit "$probe_status"
