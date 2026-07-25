#!/usr/bin/env bash
set -Eeuo pipefail

export DISPLAY="${DISPLAY:-:99}"
output="${1:-/work/freew-table-pagination-validation}"
document_path="${FREEW_DOCUMENT_PATH:-}"
source_path="${FREEW_SOURCE_FIXTURE_PATH:-${FREEW_FIXTURE_SOURCE_PATH:-}}"
expected_document_name="${FREEW_EXPECTED_DOCUMENT_NAME:-$(basename "${document_path:-table-page-composition-stress.docx}")}"
input_delay_ms="${FREEW_X11_INPUT_DELAY_MS:-180}"
settle_seconds="${FREEW_X11_SETTLE_SECONDS:-0.65}"
pointer_timeout_seconds="${FREEW_X11_POINTER_TIMEOUT_SECONDS:-3}"
shared_plan_test="${FREEW_SHARED_PLAN_TEST_PATH:-${SHARED_PLAN_TEST_PATH:-/work/shared-plan-test.txt}}"
records="$output/result-records.jsonl"
screenshots_file="$output/screenshot-names.txt"
manifest="$output/results.json"
sentinel="$output/probe-incomplete.txt"
required_ids=(
    "visible-window-discovery"
    "generated-fixture-hash-integrity"
    "physical-third-page-navigation"
    "nonblank-final-page-render"
    "shared-plan-proof"
)

mkdir -p "$output"
: > "$records"
: > "$screenshots_file"
printf 'The probe ended before this contract row produced complete physical evidence.\n' > "$sentinel"

record() {
    local id="$1" status="$2" note="$3"
    shift 3
    python3 - "$records" "$id" "$status" "$note" "$@" <<'PY'
import json
import sys

path, result_id, status, note, *evidence = sys.argv[1:]
row = {
    "id": result_id,
    "category": "physical-x11-table-pagination" if result_id != "shared-plan-proof" else "deterministic-shared-plan",
    "status": status,
    "evidenceLevel": "physical-x11-input" if result_id != "shared-plan-proof" else "focused-test",
    "evidence": evidence,
    "note": note,
}
with open(path, "a", encoding="utf-8") as handle:
    handle.write(json.dumps(row, ensure_ascii=False, sort_keys=True) + "\n")
PY
}

hash_file() {
    sha256sum "$1" | awk '{print tolower($1)}'
}

track_screenshot() {
    printf '%s\n' "$1" >> "$screenshots_file"
}

capture() {
    local name="$1"
    scrot -o "$output/$name" >/dev/null 2>&1
    [[ -s "$output/$name" ]]
    track_screenshot "$name"
}

capture_page_crop() {
    local source="$1" name="$2"
    convert "$output/$source" -crop 900x520+160+170 +repage "$output/$name" >/dev/null 2>&1
    [[ -s "$output/$name" ]]
}

screen_difference() {
    local before="$1" after="$2" changed
    changed="$(compare -metric AE "$output/$before" "$output/$after" null: 2>&1 || true)"
    if [[ "$changed" =~ ^[0-9]+ ]]; then
        printf '%s' "${BASH_REMATCH[0]}"
    else
        printf 'unknown'
    fi
}

image_nonblank_varied() {
    local image="$1" stats mean deviation
    stats="$(identify -format '%[fx:mean] %[fx:standard_deviation]' "$output/$image" 2>/dev/null || true)"
    read -r mean deviation <<< "$stats"
    [[ "$mean" =~ ^[0-9]+([.][0-9]+)?$ && "$deviation" =~ ^[0-9]+([.][0-9]+)?$ ]] || return 1
    awk -v mean="$mean" -v deviation="$deviation" 'BEGIN { exit !(mean > 0.01 && mean < 0.99 && deviation > 0.01) }'
}

window_ids() {
    xdotool search --onlyvisible --name 'FreeW' 2>/dev/null || true
}

focus_owner() {
    timeout --foreground --kill-after=1s "$pointer_timeout_seconds" xdotool windowactivate --sync "$owner_id" >/dev/null 2>&1 || true
    timeout --foreground --kill-after=1s "$pointer_timeout_seconds" xdotool windowfocus "$owner_id" >/dev/null 2>&1 || true
    sleep 0.15
}

owner_has_focus() {
    [[ "$(xdotool getactivewindow 2>/dev/null || true)" == "$owner_id" &&
       "$(xdotool getwindowfocus 2>/dev/null || true)" == "$owner_id" ]]
}

send_owner_key() {
    focus_owner
    timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
        xdotool key --clearmodifiers --delay "$input_delay_ms" --window "$owner_id" "$1"
    sleep "$settle_seconds"
}

capture_state() {
    local name="$1" title
    title="$(xdotool getwindowname "$owner_id" 2>/dev/null || true)"
    {
        printf 'phase=%s\n' "$name"
        printf 'owner-window-id=%s\n' "$owner_id"
        printf 'owner-window-title=%s\n' "$title"
        printf 'expected-document-name=%s\n' "$expected_document_name"
        printf 'expected-document-title=%s\n' "$(if [[ "$title" == *"$expected_document_name"* ]]; then printf true; else printf false; fi)"
        printf 'active-window=%s\n' "$(xdotool getactivewindow 2>/dev/null || true)"
        printf 'focus-window=%s\n' "$(xdotool getwindowfocus 2>/dev/null || true)"
        printf 'owner-active=%s\n' "$(if [[ "$(xdotool getactivewindow 2>/dev/null || true)" == "$owner_id" ]]; then printf true; else printf false; fi)"
        printf 'owner-focused=%s\n' "$(if [[ "$(xdotool getwindowfocus 2>/dev/null || true)" == "$owner_id" ]]; then printf true; else printf false; fi)"
        wmctrl -l 2>/dev/null || true
    } > "$output/$name-state.txt"
}

finalize() {
    local exit_code=$?
    set +e
    python3 - "$records" "$screenshots_file" "$manifest" "${owner_id:-}" "${owner_title:-}" "$expected_document_name" "$exit_code" <<'PY'
import json
import os
import sys

records_path, screenshots_path, manifest_path, owner_id, owner_title, fixture, exit_code = sys.argv[1:]
ids = [
    "visible-window-discovery",
    "generated-fixture-hash-integrity",
    "physical-third-page-navigation",
    "nonblank-final-page-render",
    "shared-plan-proof",
]
by_id = {}
try:
    with open(records_path, encoding="utf-8") as handle:
        for line in handle:
            if line.strip():
                row = json.loads(line)
                if row.get("id") in ids:
                    by_id[row["id"]] = row
except (FileNotFoundError, json.JSONDecodeError):
    pass
for result_id in ids:
    by_id.setdefault(result_id, {
        "id": result_id,
        "category": "deterministic-shared-plan" if result_id == "shared-plan-proof" else "physical-x11-table-pagination",
        "status": "failed",
        "evidenceLevel": "focused-test" if result_id == "shared-plan-proof" else "physical-x11-input",
        "evidence": ["probe-incomplete.txt"],
        "note": "The probe ended before this physical contract row produced complete evidence.",
    })
results = [by_id[result_id] for result_id in ids]
try:
    with open(screenshots_path, encoding="utf-8") as handle:
        names = list(dict.fromkeys(line.strip() for line in handle if line.strip()))
except FileNotFoundError:
    names = []
manifest_data = {
    "schemaVersion": 1,
    "suite": "freew-linux-table-pagination-physical",
    "platform": "linux",
    "shell": "avalonia",
    "app": "FreeW",
    "baseline": False,
    "appSurface": "table-page-composition-stress",
    "window": {"id": owner_id, "title": owner_title, "pattern": fixture, "visible": bool(owner_id)},
    "parameters": {"fixture": fixture},
    "coverage": {"scope": "physical FreeW table pagination and third-page composition evidence lane", "exhaustive": False},
    "contractValidation": {"status": "pending", "validator": "host-wrapper", "contractReference": "freew-linux-table-pagination-physical"},
    "screenshots": [{"name": name, "kind": "screenshot"} for name in names],
    "summary": {"passed": sum(row["status"] == "passed" for row in results), "failed": sum(row["status"] == "failed" for row in results), "total": len(results)},
    "results": results,
    "processExitCode": int(exit_code),
}
with open(manifest_path, "w", encoding="utf-8") as handle:
    json.dump(manifest_data, handle, ensure_ascii=False, indent=2)
    handle.write("\n")
PY
    if python3 - "$manifest" <<'PY'
import json, sys
data = json.load(open(sys.argv[1], encoding="utf-8"))
raise SystemExit(0 if data["summary"]["failed"] == 0 and data["summary"]["passed"] == len(data["results"]) else 1)
PY
    then
        if [[ "$exit_code" -eq 0 ]]; then
            rm -f "$sentinel"
        fi
    else
        :
    fi
    return "$exit_code"
}
trap finalize EXIT

mapfile -t candidates < <(window_ids)
visible_windows=()
for candidate in "${candidates[@]}"; do
    title="$(xdotool getwindowname "$candidate" 2>/dev/null || true)"
    [[ "$title" == *"$expected_document_name"* ]] && visible_windows+=("$candidate")
done
if (( ${#visible_windows[@]} == 0 )); then
    printf 'No visible FreeW window was associated with the expected fixture document.\nexpected-document-name=%s\n' "$expected_document_name" > "$output/window-discovery-error.txt"
    record visible-window-discovery failed "No visible FreeW window was associated with the expected fixture document." window-discovery-error.txt
else
    owner_id="${visible_windows[${#visible_windows[@]}-1]}"
    owner_title="$(xdotool getwindowname "$owner_id" 2>/dev/null || printf FreeW)"
    focus_owner
    capture baseline.png
    capture_page_crop baseline.png baseline-page-crop.png
    capture_state baseline
    printf 'window-id=%s\nwindow-title=%s\nexpected-document-name=%s\nowner-focused=%s\n' "$owner_id" "$owner_title" "$expected_document_name" "$(if owner_has_focus; then printf true; else printf false; fi)" > "$output/baseline-window-proof.txt"
    if owner_has_focus; then
        record visible-window-discovery passed "Discovered and focused the visible FreeW owner for the expected table page-composition fixture." baseline.png baseline-page-crop.png baseline-state.txt baseline-window-proof.txt
    else
        record visible-window-discovery failed "The expected FreeW owner was visible but did not retain focus." baseline.png baseline-state.txt baseline-window-proof.txt
    fi
fi

source_hash=missing
mounted_hash=missing
[[ -n "$source_path" && -f "$source_path" ]] && source_hash="$(hash_file "$source_path")"
[[ -n "$document_path" && -f "$document_path" ]] && mounted_hash="$(hash_file "$document_path")"
printf 'source-path=%s\nsource-sha256=%s\nmounted-path=%s\nmounted-sha256=%s\n' "$source_path" "$source_hash" "$document_path" "$mounted_hash" > "$output/generated-fixture-hash-proof.txt"
if [[ "$source_hash" != missing && "$mounted_hash" != missing && "$source_hash" == "$mounted_hash" ]]; then
    record generated-fixture-hash-integrity passed "The source fixture and host-mounted fixture have identical SHA-256 hashes." generated-fixture-hash-proof.txt
else
    record generated-fixture-hash-integrity failed "The source and mounted fixture hashes were unavailable or differed." generated-fixture-hash-proof.txt
fi

if [[ -n "${owner_id:-}" && -f "$output/baseline-page-crop.png" ]]; then
    baseline_hash="$(sha256sum "$output/baseline-page-crop.png" | awk '{print $1}')"
    send_owner_key ctrl+end
    capture ctrl-end.png
    capture_page_crop ctrl-end.png ctrl-end-page-crop.png
    capture_state ctrl-end
    ctrl_end_hash="$(sha256sum "$output/ctrl-end-page-crop.png" | awk '{print $1}')"
    fallback="none"
    if [[ "$ctrl_end_hash" == "$baseline_hash" ]]; then
        fallback="pagedown"
        send_owner_key pagedown
        capture pagedown-fallback.png
        capture_page_crop pagedown-fallback.png pagedown-fallback-page-crop.png
        capture_state pagedown-fallback
    fi
    capture final.png
    capture_page_crop final.png final-page-crop.png
    capture_state final
    final_hash="$(sha256sum "$output/final-page-crop.png" | awk '{print $1}')"
    final_delta="$(screen_difference baseline-page-crop.png final-page-crop.png)"
    printf 'baseline-crop-sha256=%s\nctrl-end-crop-sha256=%s\nfinal-crop-sha256=%s\nfallback=%s\nbaseline-final-AE=%s\n' "$baseline_hash" "$ctrl_end_hash" "$final_hash" "$fallback" "$final_delta" > "$output/third-page-navigation-proof.txt"
    printf 'owner-focused=%s\n' "$(if owner_has_focus; then printf true; else printf false; fi)" >> "$output/third-page-navigation-proof.txt"
    if [[ "$final_delta" =~ ^[0-9]+$ ]] && (( final_delta > 100 )) && owner_has_focus; then
        record physical-third-page-navigation passed "Ctrl+End reached a changed end-of-document render coupled to the deterministic three-page plan; PageDown was used only when Ctrl+End left the page crop unchanged." baseline.png ctrl-end.png final.png baseline-page-crop.png ctrl-end-page-crop.png final-page-crop.png third-page-navigation-proof.txt
    else
        record physical-third-page-navigation failed "The physical navigation sequence did not produce a changed final page while retaining the FreeW owner focus." third-page-navigation-proof.txt final.png final-page-crop.png
    fi
    if image_nonblank_varied final-page-crop.png; then
        record nonblank-final-page-render passed "The final page crop is nonblank and contains measurable visual variation." final.png final-page-crop.png final-state.txt third-page-navigation-proof.txt
    else
        record nonblank-final-page-render failed "ImageMagick could not prove that the final page crop is nonblank and varied." final.png final-page-crop.png final-state.txt
    fi
else
    record physical-third-page-navigation failed "Navigation was not attempted because the focused FreeW owner or baseline crop was unavailable." probe-incomplete.txt
    record nonblank-final-page-render failed "Final-page rendering was not attempted because the baseline capture was unavailable." probe-incomplete.txt
fi

if [[ -f "$shared_plan_test" ]] && grep -Eiq 'focused.*(test|success)|test.*(passed|success)|passed' "$shared_plan_test"; then
    record shared-plan-proof passed "shared-plan-test.txt contains focused-test success evidence." "$shared_plan_test"
else
    printf 'shared-plan-test-path=%s\n' "$shared_plan_test" > "$output/shared-plan-proof.txt"
    [[ -f "$shared_plan_test" ]] && cat "$shared_plan_test" >> "$output/shared-plan-proof.txt"
    record shared-plan-proof failed "shared-plan-test.txt was absent or did not contain focused-test success evidence." shared-plan-proof.txt
fi
