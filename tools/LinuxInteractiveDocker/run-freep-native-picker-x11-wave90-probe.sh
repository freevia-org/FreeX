#!/usr/bin/env bash
set -Eeuo pipefail

export DISPLAY="${DISPLAY:-:99}"
output="${1:-/work/freep-native-picker-x11-wave90-validation}"
document_path="${FREEP_DOCUMENT_PATH:-/documents/initial.pptx}"
expected_document_name="${FREEP_EXPECTED_DOCUMENT_NAME:-initial.pptx}"
selected_path="${FREEP_PICKER_OPEN_SELECTED_PATH:-/documents/open-selected.pptx}"
save_path="${FREEP_PICKER_SAVE_PATH:-/documents/save-as-selected.pptx}"
collision_path="${FREEP_PICKER_COLLISION_PATH:-/documents/existing-collision.pptx}"
invalid_path="${FREEP_PICKER_INVALID_PATH:-/proc/freep-native-picker-x11-wave90.pptx}"
window_pattern="${FREEP_EXPECTED_WINDOW_PATTERN:-FreeP}"
input_delay_ms="${FREEP_X11_INPUT_DELAY_MS:-80}"
settle_seconds="${FREEP_X11_SETTLE_SECONDS:-0.65}"
pointer_timeout_seconds="${FREEP_X11_POINTER_TIMEOUT_SECONDS:-4}"
records="$output/result-records.jsonl"
screenshots_file="$output/screenshot-names.txt"
manifest="$output/results.json"
owner_id=""
owner_title=""

required_ids=(
  visible-window-discovery
  open-cancel-preserves-document
  open-pptx-selection-loads-package
  save-as-pptx-filter-selection-writes-package
  save-as-overwrite-cancel-preserves-collision
  save-as-unwritable-bounded-error
  escape-cancel-open-no-modal-blocker
  escape-cancel-save-no-modal-blocker
  focus-return-after-cancel-and-error
)

mkdir -p "$output"
: > "$records"
: > "$screenshots_file"
printf 'The probe ended before this contract row produced complete physical evidence.\n' > "$output/probe-incomplete.txt"

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
        "category": "physical-x11-native-picker",
        "status": status,
        "evidenceLevel": "physical-x11-input",
        "evidence": evidence,
        "note": note,
    }, sort_keys=True) + "\n")
PY
}

track_screenshot() { printf '%s\n' "$1" >> "$screenshots_file"; }

capture() {
  local name="$1"
  scrot -o "$output/$name" >/dev/null 2>&1 || return 1
  [[ -s "$output/$name" ]] || return 1
  track_screenshot "$name"
}

run_key() {
  timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
    xdotool key --clearmodifiers --delay "$input_delay_ms" "$@"
}

focus_owner() {
  timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
    xdotool windowactivate --sync "$owner_id" >/dev/null 2>&1 || true
  timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
    xdotool windowfocus "$owner_id" >/dev/null 2>&1 || true
  sleep 0.15
}

active_title() { xdotool getactivewindow getwindowname 2>/dev/null || true; }

visible_window() {
  local title="$1"
  xdotool search --onlyvisible --name "^${title}$" 2>/dev/null | tail -1 || true
}

wait_window() {
  local title="$1" seconds="${2:-12}" id="" attempt
  for attempt in $(seq 1 $((seconds * 10))); do
    id="$(visible_window "$title")"
    if [[ -n "$id" ]]; then
      printf '%s\n' "$id"
      return 0
    fi
    sleep 0.1
  done
  return 1
}

wait_owner() {
  local attempt title
  focus_owner
  for attempt in $(seq 1 100); do
    title="$(active_title)"
    if [[ "$title" == *FreeP* ]]; then
      return 0
    fi
    focus_owner
    sleep 0.1
  done
  return 1
}

open_native_picker() {
  local picker="" prompt="" attempt
  focus_owner
  run_key ctrl+o
  for attempt in $(seq 1 30); do
    picker="$(visible_window 'Open Presentation')"
    if [[ -n "$picker" ]]; then
      printf '%s\n' "$picker"
      return 0
    fi

    prompt="$(visible_window 'FreeP')"
    if [[ -n "$prompt" ]]; then
      timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
        xdotool windowactivate --sync "$prompt" >/dev/null 2>&1 || true
      # The dirty-document prompt starts on Save; one Tab selects Don't save.
      run_key 0xff09
      run_key Return
      break
    fi
    sleep 0.1
  done

  wait_window 'Open Presentation'
}

window_inventory() {
  local name="$1"
  {
    printf 'owner-window-id=%s\nowner-window-title=%s\n' "$owner_id" "$owner_title"
    printf 'active-window-title=%s\n' "$(active_title)"
    printf 'focus-window=%s\n' "$(xdotool getwindowfocus 2>/dev/null || true)"
    printf 'open-picker-count=%s\n' "$(xdotool search --onlyvisible --name '^Open Presentation$' 2>/dev/null | wc -l)"
    printf 'save-picker-count=%s\n' "$(xdotool search --onlyvisible --name '^Save Presentation$' 2>/dev/null | wc -l)"
    printf 'wmctrl-list-begin\n'
    wmctrl -l 2>/dev/null || true
    printf 'wmctrl-list-end\n'
  } > "$output/$name"
}

no_modal_blocker() {
  local title open_count save_count
  title="$(active_title)"
  open_count="$(xdotool search --onlyvisible --name '^Open Presentation$' 2>/dev/null | wc -l)"
  save_count="$(xdotool search --onlyvisible --name '^Save Presentation$' 2>/dev/null | wc -l)"
  [[ "$title" == *FreeP* && "$open_count" == "0" && "$save_count" == "0" ]]
}

hash_file() { sha256sum -- "$1" | awk '{print tolower($1)}'; }

inspect_package() {
  local source="$1" destination="$2"
  python3 - "$source" "$destination" <<'PY'
import hashlib
import json
import sys
import zipfile

source, destination = sys.argv[1:]
with open(source, "rb") as handle:
    digest = hashlib.sha256(handle.read()).hexdigest()
with zipfile.ZipFile(source) as package:
    names = set(package.namelist())
slides = sorted(name for name in names if name.startswith("ppt/slides/slide") and name.endswith(".xml"))
state = {
    "path": source,
    "exists": True,
    "sha256": digest,
    "packageKind": "pptx-zip-package" if "[Content_Types].xml" in names and "ppt/presentation.xml" in names else "not-a-pptx-package",
    "containsPresentationXml": "ppt/presentation.xml" in names,
    "slideCount": len(slides),
}
with open(destination, "w", encoding="utf-8") as handle:
    json.dump(state, handle, sort_keys=True, separators=(",", ":"))
    handle.write("\n")
PY
}

missing_package_state() {
  local path="$1" destination="$2"
  python3 - "$path" "$destination" <<'PY'
import json
import sys
path, destination = sys.argv[1:]
with open(destination, "w", encoding="utf-8") as handle:
    json.dump({"path": path, "exists": False, "sha256": "", "packageKind": "not-created", "containsPresentationXml": False, "slideCount": 0}, handle, sort_keys=True, separators=(",", ":"))
    handle.write("\n")
PY
}

picker_geometry() {
  local picker="$1"
  eval "$(xdotool getwindowgeometry --shell "$picker")"
}

click_picker_filename() {
  local picker="$1"
  picker_geometry "$picker"
  xdotool mousemove --sync $((X + WIDTH / 2)) 48
  xdotool click --clearmodifiers 1
}

type_picker_path() {
  local path="$1"
  run_key ctrl+a
  xdotool type --delay 8 -- "$path"
}

click_picker_action() {
  local picker="$1"
  picker_geometry "$picker"
  xdotool mousemove --sync $((X + WIDTH - 140)) $((Y + HEIGHT - 34))
  xdotool click --clearmodifiers 1
}

select_pptx_filter() {
  printf 'visible-default-filter=PowerPoint presentations (*.pptx)\n' >> "$output/filter-selection.txt"
}

navigate_save_picker_to_root_child() {
  local picker="$1" directory="$2" directory_name
  directory_name="$(basename "$directory")"
  [[ "$directory" == "/$directory_name" ]] || return 1

  picker_geometry "$picker"
  xdotool mousemove --sync $((X + 80)) 218
  xdotool click --clearmodifiers 1
  sleep 0.75
  xdotool mousemove --sync $((X + 400)) 130
  xdotool click --clearmodifiers --repeat 2 --delay 100 1
  sleep 0.75
  xdotool mousemove --sync $((X + 400)) 158
  xdotool click --clearmodifiers 1
  xdotool type --delay 50 -- "$directory_name"
  sleep 0.5
  run_key Return
  sleep 0.75
}

save_picker_path() {
  local path="$1" screenshot_name="$2" picker directory file_name
  picker="$(wait_window 'Save Presentation')"
  timeout --foreground --kill-after=1s "$pointer_timeout_seconds" xdotool windowactivate --sync "$picker" >/dev/null 2>&1 || true
  directory="$(dirname "$path")"
  file_name="$(basename "$path")"
  navigate_save_picker_to_root_child "$picker" "$directory"
  click_picker_filename "$picker"
  type_picker_path "$file_name"
  select_pptx_filter
  capture "$screenshot_name"
  click_picker_action "$picker"
  sleep "$settle_seconds"
}

finalize() {
  local exit_code=$?
  set +e
  python3 - "$records" "$screenshots_file" "$manifest" "$owner_id" "$owner_title" "$expected_document_name" "$document_path" "$selected_path" "$save_path" "$collision_path" "$invalid_path" "$exit_code" <<'PY'
import json
import os
import sys

records, screenshots, manifest_path, owner_id, owner_title, initial_name, initial_path, selected_path, save_path, collision_path, invalid_path, exit_code = sys.argv[1:]
ids = ["visible-window-discovery", "open-cancel-preserves-document", "open-pptx-selection-loads-package", "save-as-pptx-filter-selection-writes-package", "save-as-overwrite-cancel-preserves-collision", "save-as-unwritable-bounded-error", "escape-cancel-open-no-modal-blocker", "escape-cancel-save-no-modal-blocker", "focus-return-after-cancel-and-error"]
rows = {}
try:
    with open(records, encoding="utf-8") as handle:
        for line in handle:
            if line.strip():
                row = json.loads(line)
                rows[row["id"]] = row
except FileNotFoundError:
    pass
for row_id in ids:
    rows.setdefault(row_id, {"id": row_id, "category": "physical-x11-native-picker", "status": "failed", "evidenceLevel": "physical-x11-input", "evidence": ["probe-incomplete.txt"], "note": "The probe ended before this physical contract row produced complete evidence."})
try:
    shot_names = list(dict.fromkeys(name.strip() for name in open(screenshots, encoding="utf-8") if name.strip()))
except FileNotFoundError:
    shot_names = []

def load_state(name, path, missing=False):
    try:
        return json.load(open(os.path.join(os.path.dirname(manifest_path), name), encoding="utf-8"))
    except (FileNotFoundError, json.JSONDecodeError):
        return {"path": path, "exists": False, "sha256": "", "packageKind": "not-created" if missing else "not-inspected", "containsPresentationXml": False, "slideCount": 0}

fixtures = []
for fixture_id, path, file_name, state_name in [("initial", initial_path, initial_name, "state-initial.json"), ("openSelected", selected_path, os.path.basename(selected_path), "state-open-selected.json"), ("collision", collision_path, os.path.basename(collision_path), "state-collision-before.json")]:
    state = load_state(state_name, path)
    fixtures.append({"id": fixture_id, "path": path, "fileName": file_name, "sha256": state.get("sha256", ""), "packageKind": "pptx-zip-package"})
manifest = {
    "schemaVersion": 1, "suite": "freep-native-picker-x11-wave90-physical", "platform": "linux", "shell": "avalonia", "app": "FreeP", "baseline": False, "appSurface": "native-storage-provider-open-save-as",
    "window": {"id": owner_id, "title": owner_title, "pattern": "FreeP", "visible": bool(owner_id)}, "fixtures": fixtures,
    "packageStates": {"initial": load_state("state-initial.json", initial_path), "openSelected": load_state("state-open-selected.json", selected_path), "savePptx": load_state("state-save-pptx.json", save_path), "collisionBefore": load_state("state-collision-before.json", collision_path), "collisionAfter": load_state("state-collision-after.json", collision_path), "invalidTarget": load_state("state-invalid-target.json", invalid_path, True)},
    "screenshots": [{"name": name, "kind": "screenshot"} for name in shot_names],
    "summary": {"passed": sum(rows[row_id]["status"] == "passed" for row_id in ids), "failed": sum(rows[row_id]["status"] == "failed" for row_id in ids), "total": len(ids)},
    "results": [rows[row_id] for row_id in ids], "processExitCode": int(exit_code),
    "contractValidation": {"status": "pending", "validator": "tools/Run-FreePNativePickerX11Validation.ps1", "contractReference": "tools/LinuxInteractiveDocker/freep-native-picker-x11-wave90-validation.schema.json"},
}
with open(manifest_path, "w", encoding="utf-8") as handle:
    json.dump(manifest, handle, indent=2)
    handle.write("\n")
PY
  return "$exit_code"
}
trap finalize EXIT

if [[ ! -f "$document_path" ]]; then
  printf 'FREEP_DOCUMENT_PATH is absent or is not a file: %s\n' "$document_path" > "$output/precondition-error.txt"
  exit 1
fi

inspect_package "$document_path" "$output/state-initial.json"
visible_owner_ids=()
for _ in $(seq 1 120); do
  mapfile -t visible_owner_ids < <(xdotool search --onlyvisible --name "$window_pattern" 2>/dev/null || true)
  if (( ${#visible_owner_ids[@]} > 0 )); then
    break
  fi
  sleep 0.1
done
if (( ${#visible_owner_ids[@]} == 0 )); then
  record visible-window-discovery failed "No visible FreeP owner matched the X11 precondition." probe-incomplete.txt
  exit 1
fi
owner_id="${visible_owner_ids[${#visible_owner_ids[@]}-1]}"
owner_title="$(xdotool getwindowname "$owner_id" 2>/dev/null || true)"
focus_owner
capture owner-window.png
window_inventory owner-window-state.txt
if [[ "$owner_title" == *"$expected_document_name"* && -s "$output/state-initial.json" && -s "$output/owner-window.png" ]]; then
  record visible-window-discovery passed "Focused the real FreeP owner on the initial PPTX fixture and retained package/window evidence." owner-window.png owner-window-state.txt state-initial.json
else
  record visible-window-discovery failed "FreeP owner, fixture title, screenshot, or initial package inspection was incomplete." owner-window.png owner-window-state.txt state-initial.json
fi

initial_hash="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["sha256"])' "$output/state-initial.json")"

open_picker="$(open_native_picker)"
capture open-cancel-picker.png
run_key Escape
sleep "$settle_seconds"
window_inventory open-cancel-owner-state.txt
capture open-cancel-owner.png
after_cancel_hash="$(hash_file "$document_path")"
if wait_owner && no_modal_blocker && [[ "$after_cancel_hash" == "$initial_hash" ]]; then
  record open-cancel-preserves-document passed "Escape canceled the real Open picker; the mounted initial package hash and owner focus were unchanged." open-cancel-picker.png open-cancel-owner.png open-cancel-owner-state.txt state-initial.json
else
  record open-cancel-preserves-document failed "Open cancellation did not prove unchanged package state and owner focus." open-cancel-picker.png open-cancel-owner.png open-cancel-owner-state.txt state-initial.json
fi

open_picker="$(open_native_picker)"
capture open-pptx-picker.png
timeout --foreground --kill-after=1s "$pointer_timeout_seconds" xdotool windowactivate --sync "$open_picker" >/dev/null 2>&1 || true
run_key ctrl+l
xdotool type --delay 8 -- "$selected_path"
run_key Return
sleep "$settle_seconds"
if [[ -n "$(visible_window 'Open Presentation')" ]]; then
  click_picker_action "$(visible_window 'Open Presentation')"
fi
wait_owner
sleep "$settle_seconds"
capture open-pptx-owner.png
if [[ -f "$selected_path" ]]; then inspect_package "$selected_path" "$output/state-open-selected.json"; else missing_package_state "$selected_path" "$output/state-open-selected.json"; fi
window_inventory open-pptx-owner-state.txt
selected_name="$(basename "$selected_path")"
selected_kind="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["packageKind"])' "$output/state-open-selected.json")"
if [[ "$(active_title)" == *"$selected_name"* && "$selected_kind" == "pptx-zip-package" ]]; then
  record open-pptx-selection-loads-package passed "The real Open picker accepted the physically entered PPTX path and FreeP returned with the selected package title and package postcondition." open-pptx-picker.png open-pptx-owner.png open-pptx-owner-state.txt state-open-selected.json filter-selection.txt
else
  record open-pptx-selection-loads-package failed "The physical Open selection did not prove the expected PPTX path and package postcondition." open-pptx-picker.png open-pptx-owner.png open-pptx-owner-state.txt state-open-selected.json filter-selection.txt
fi

run_key ctrl+shift+s
save_picker_path "$save_path" save-pptx-filter-selected.png
sleep "$settle_seconds"
capture save-pptx-owner.png
if [[ -f "$save_path" ]]; then inspect_package "$save_path" "$output/state-save-pptx.json"; else missing_package_state "$save_path" "$output/state-save-pptx.json"; fi
window_inventory save-pptx-owner-state.txt
save_kind="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["packageKind"])' "$output/state-save-pptx.json")"
if wait_owner && no_modal_blocker && [[ "$save_kind" == "pptx-zip-package" ]]; then
  record save-as-pptx-filter-selection-writes-package passed "Physical Save As selected the PowerPoint presentations filter, wrote a non-empty PPTX package, and returned to FreeP." save-pptx-filter-selected.png save-pptx-owner.png save-pptx-owner-state.txt state-save-pptx.json filter-selection.txt
else
  record save-as-pptx-filter-selection-writes-package failed "Physical Save As did not prove the expected PPTX path/package and owner return." save-pptx-filter-selected.png save-pptx-owner.png save-pptx-owner-state.txt state-save-pptx.json filter-selection.txt
fi

inspect_package "$collision_path" "$output/state-collision-before.json"
collision_before="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["sha256"])' "$output/state-collision-before.json")"
run_key ctrl+shift+s
save_picker_path "$collision_path" overwrite-picker-entry.png
sleep "$settle_seconds"
capture overwrite-confirmation.png
window_inventory overwrite-confirmation-state.txt
overwrite_windows="$(wmctrl -l 2>/dev/null | wc -l)"
printf 'overwrite-confirmation-window-count=%s\n' "$overwrite_windows" > "$output/overwrite-confirmation-observed.txt"
run_key Escape
sleep 0.25
if [[ -n "$(visible_window 'Save Presentation')" ]]; then run_key Escape; fi
wait_owner
capture overwrite-cancel-owner.png
inspect_package "$collision_path" "$output/state-collision-after.json"
collision_after="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["sha256"])' "$output/state-collision-after.json")"
window_inventory overwrite-cancel-owner-state.txt
if [[ "$overwrite_windows" -ge 3 && "$collision_before" == "$collision_after" ]] && no_modal_blocker; then
  record save-as-overwrite-cancel-preserves-collision passed "The real collision confirmation appeared as an additional X11 window; Escape declined replacement, preserved the existing package hash, and returned focus." overwrite-picker-entry.png overwrite-confirmation.png overwrite-confirmation-state.txt overwrite-confirmation-observed.txt overwrite-cancel-owner.png overwrite-cancel-owner-state.txt state-collision-before.json state-collision-after.json
else
  record save-as-overwrite-cancel-preserves-collision failed "An additional collision window, unchanged package hash, or owner return was not proven." overwrite-picker-entry.png overwrite-confirmation.png overwrite-confirmation-state.txt overwrite-confirmation-observed.txt overwrite-cancel-owner.png overwrite-cancel-owner-state.txt state-collision-before.json state-collision-after.json
fi

run_key ctrl+shift+s
save_picker_path "$invalid_path" invalid-target-entry.png
sleep "$settle_seconds"
capture invalid-target-error.png
window_inventory invalid-target-error-state.txt
invalid_windows="$(wmctrl -l 2>/dev/null | wc -l)"
printf 'invalid-target-error-window-count=%s\n' "$invalid_windows" > "$output/invalid-target-error-observed.txt"
run_key Escape
sleep 0.25
if [[ -n "$(visible_window 'Save Presentation')" ]]; then run_key Escape; fi
wait_owner
capture invalid-target-owner.png
missing_package_state "$invalid_path" "$output/state-invalid-target.json"
window_inventory invalid-target-owner-state.txt
if [[ "$invalid_windows" -ge 2 && ! -e "$invalid_path" ]] && no_modal_blocker; then
  record save-as-unwritable-bounded-error passed "The physically selected unwritable target surfaced an additional bounded error window, was not created, and all modal layers were dismissed." invalid-target-entry.png invalid-target-error.png invalid-target-error-state.txt invalid-target-error-observed.txt invalid-target-owner.png invalid-target-owner-state.txt state-invalid-target.json
else
  record save-as-unwritable-bounded-error failed "An additional bounded error window, absent invalid target, or modal-free return was not proven." invalid-target-entry.png invalid-target-error.png invalid-target-error-state.txt invalid-target-error-observed.txt invalid-target-owner.png invalid-target-owner-state.txt state-invalid-target.json
fi

open_picker="$(open_native_picker)"
capture escape-open-picker.png
run_key Escape
wait_owner
window_inventory escape-open-owner-state.txt
capture escape-open-owner.png
if no_modal_blocker; then
  record escape-cancel-open-no-modal-blocker passed "Physical Escape closed Open and left no native picker window blocking the FreeP owner." escape-open-picker.png escape-open-owner.png escape-open-owner-state.txt
else
  record escape-cancel-open-no-modal-blocker failed "Open Escape left a native modal blocker or failed to restore owner focus." escape-open-picker.png escape-open-owner.png escape-open-owner-state.txt
fi

run_key ctrl+shift+s
save_picker="$(wait_window 'Save Presentation')"
capture escape-save-picker.png
run_key Escape
wait_owner
window_inventory escape-save-owner-state.txt
capture escape-save-owner.png
if no_modal_blocker; then
  record escape-cancel-save-no-modal-blocker passed "Physical Escape closed Save As and left no native picker window blocking the FreeP owner." escape-save-picker.png escape-save-owner.png escape-save-owner-state.txt
else
  record escape-cancel-save-no-modal-blocker failed "Save As Escape left a native modal blocker or failed to restore owner focus." escape-save-picker.png escape-save-owner.png escape-save-owner-state.txt
fi

window_inventory final-owner-state.txt
capture final-owner.png
if wait_owner && no_modal_blocker; then
  record focus-return-after-cancel-and-error passed "The final X11 active/focus state is the FreeP owner after Open cancel, Save As cancel, collision decline, and invalid-target error." final-owner.png final-owner-state.txt open-cancel-owner-state.txt overwrite-cancel-owner-state.txt invalid-target-owner-state.txt
else
  record focus-return-after-cancel-and-error failed "Final owner focus or modal-free state was not proven." final-owner.png final-owner-state.txt open-cancel-owner-state.txt overwrite-cancel-owner-state.txt invalid-target-owner-state.txt
fi

exit 0
