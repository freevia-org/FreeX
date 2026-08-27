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
avalonia_table_test="${FREEW_AVALONIA_TABLE_TEST_PATH:-/work/avalonia-table-structure-test.txt}"
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

capture_status_crop() {
    local source="$1" name="$2" dimensions width height crop_y
    dimensions="$(identify -format '%w %h' "$output/$source" 2>/dev/null || true)"
    read -r width height <<< "$dimensions"
    [[ "$width" =~ ^[0-9]+$ && "$height" =~ ^[0-9]+$ ]]
    crop_y=$((height - 56))
    (( crop_y < 0 )) && crop_y=0
    convert "$output/$source" -crop "520x56+0+$crop_y" +repage "$output/$name" >/dev/null 2>&1
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

click_document_surface() {
    local key value max_x max_y click_exit
    document_click_geometry="$(xdotool getwindowgeometry --shell "$owner_id" 2>/dev/null || true)"
    document_window_x=""
    document_window_y=""
    document_window_width=""
    document_window_height=""
    while IFS='=' read -r key value; do
        case "$key" in
            X) document_window_x="$value" ;;
            Y) document_window_y="$value" ;;
            WIDTH) document_window_width="$value" ;;
            HEIGHT) document_window_height="$value" ;;
        esac
    done <<< "$document_click_geometry"

    if [[ ! "$document_window_x" =~ ^-?[0-9]+$ ||
          ! "$document_window_y" =~ ^-?[0-9]+$ ||
          ! "$document_window_width" =~ ^[0-9]+$ ||
          ! "$document_window_height" =~ ^[0-9]+$ ]]; then
        printf 'window-geometry=%s\nclick-status=invalid-owner-geometry\n' "$document_click_geometry" > "$output/document-focus-click-proof.txt"
        return 1
    fi

    document_click_relative_x=$((document_window_width / 2))
    document_click_relative_y=$((document_window_height * 45 / 100))
    max_x=$((document_window_width - 180))
    max_y=$((document_window_height - 140))
    (( document_click_relative_x < 220 )) && document_click_relative_x=220
    (( max_x < 220 )) && max_x=$((document_window_width - 20))
    (( document_click_relative_x > max_x )) && document_click_relative_x=$max_x
    (( document_click_relative_y < 200 )) && document_click_relative_y=200
    (( max_y < 200 )) && max_y=$((document_window_height - 40))
    (( document_click_relative_y > max_y )) && document_click_relative_y=$max_y
    document_click_absolute_x=$((document_window_x + document_click_relative_x))
    document_click_absolute_y=$((document_window_y + document_click_relative_y))

    focus_owner
    document_click_focus_before="$(if owner_has_focus; then printf true; else printf false; fi)"
    if timeout --foreground --kill-after=1s "$pointer_timeout_seconds" \
        xdotool mousemove --sync --window "$owner_id" \
            "$document_click_relative_x" "$document_click_relative_y" click 1; then
        click_exit=0
    else
        click_exit=$?
    fi
    sleep "$settle_seconds"
    document_click_focus_after="$(if owner_has_focus; then printf true; else printf false; fi)"
    {
        printf 'window-geometry=%s\n' "$document_click_geometry"
        printf 'window-x=%s\nwindow-y=%s\nwindow-width=%s\nwindow-height=%s\n' \
            "$document_window_x" "$document_window_y" "$document_window_width" "$document_window_height"
        printf 'click-relative-x=%s\nclick-relative-y=%s\nclick-absolute-x=%s\nclick-absolute-y=%s\n' \
            "$document_click_relative_x" "$document_click_relative_y" "$document_click_absolute_x" "$document_click_absolute_y"
        printf 'click-target-policy=center-x-and-45-percent-height-clamped-away-from-chrome\n'
        printf 'click-exit-code=%s\nowner-focused-before-click=%s\nowner-focused-after-click=%s\n' \
            "$click_exit" "$document_click_focus_before" "$document_click_focus_after"
    } > "$output/document-focus-click-proof.txt"
    [[ "$click_exit" -eq 0 && "$document_click_focus_after" == true ]]
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
    if click_document_surface; then
        document_click_ready=true
    else
        document_click_ready=false
    fi
    capture document-focus-click.png
    capture_page_crop document-focus-click.png document-focus-click-page-crop.png
    capture_state document-focus-click
    printf 'click-screenshot=document-focus-click.png\nclick-page-crop=document-focus-click-page-crop.png\nclick-state=document-focus-click-state.txt\n' >> "$output/document-focus-click-proof.txt"

    if [[ "$document_click_ready" == true ]]; then
        navigation_baseline_hash="$(sha256sum "$output/document-focus-click-page-crop.png" | awk '{print $1}')"
        navigation_evidence=(
            baseline.png
            baseline-page-crop.png
            document-focus-click.png
            document-focus-click-page-crop.png
            document-focus-click-state.txt
            document-focus-click-proof.txt
        )
        send_owner_key ctrl+End
        capture ctrl-end-logical.png
        capture_page_crop ctrl-end-logical.png ctrl-end-logical-page-crop.png
        capture_status_crop ctrl-end-logical.png ctrl-end-logical-status-bar-crop.png
        capture_state ctrl-end-logical
        ctrl_end_logical_delta="$(screen_difference document-focus-click-page-crop.png ctrl-end-logical-page-crop.png)"
        ctrl_end_owner_focus="$(if owner_has_focus; then printf true; else printf false; fi)"
        navigation_evidence+=(
            ctrl-end-logical.png
            ctrl-end-logical-page-crop.png
            ctrl-end-logical-status-bar-crop.png
            ctrl-end-logical-state.txt
        )

        page_down_max=8
        material_change_seen=false
        stable_endpoint_reached=false
        stable_step=""
        stable_previous_crop=""
        stable_endpoint_full=""
        stable_endpoint_crop=""
        stable_endpoint_status=""
        stable_endpoint_state=""
        stable_endpoint_focus=false
        previous_crop=ctrl-end-logical-page-crop.png
        : > "$output/page-down-steps.txt"

        for ((page_down_step = 1; page_down_step <= page_down_max; page_down_step++)); do
            printf -v page_down_label '%02d' "$page_down_step"
            step_full="page-down-$page_down_label.png"
            step_crop="page-down-$page_down_label-page-crop.png"
            step_status="page-down-$page_down_label-status-bar-crop.png"
            step_state="page-down-$page_down_label-state.txt"
            send_owner_key Page_Down
            capture "$step_full"
            capture_page_crop "$step_full" "$step_crop"
            capture_status_crop "$step_full" "$step_status"
            capture_state "page-down-$page_down_label"
            step_delta="$(screen_difference "$previous_crop" "$step_crop")"
            step_baseline_delta="$(screen_difference document-focus-click-page-crop.png "$step_crop")"
            step_owner_focus="$(if owner_has_focus; then printf true; else printf false; fi)"
            {
                printf 'step=%s\nkey-symbol=Page_Down\nprevious-crop=%s\nfull-screenshot=%s\npage-crop=%s\nstatus-bar-crop=%s\n' \
                    "$page_down_step" "$previous_crop" "$step_full" "$step_crop" "$step_status"
                printf 'previous-step-AE=%s\npost-click-baseline-AE=%s\nowner-focused=%s\n\n' \
                    "$step_delta" "$step_baseline_delta" "$step_owner_focus"
            } >> "$output/page-down-steps.txt"
            navigation_evidence+=("$step_full" "$step_crop" "$step_status" "$step_state")

            if [[ "$step_delta" =~ ^[0-9]+$ ]] && (( step_delta > 100 )); then
                material_change_seen=true
            fi
            if [[ "$material_change_seen" == true &&
                  "$step_delta" =~ ^[0-9]+$ ]] &&
                  (( step_delta <= 100 )) &&
                  [[ "$step_owner_focus" == true ]]; then
                stable_endpoint_reached=true
                stable_step="$page_down_step"
                stable_previous_crop="$previous_crop"
                stable_endpoint_full="$step_full"
                stable_endpoint_crop="$step_crop"
                stable_endpoint_status="$step_status"
                stable_endpoint_state="$step_state"
                stable_endpoint_focus="$step_owner_focus"
                break
            fi
            previous_crop="$step_crop"
        done

        {
            printf 'logical-end-key-symbol=ctrl+End\n'
            printf 'logical-end-full=ctrl-end-logical.png\nlogical-end-page-crop=ctrl-end-logical-page-crop.png\n'
            printf 'logical-end-status-bar-crop=ctrl-end-logical-status-bar-crop.png\n'
            printf 'navigation-baseline=document-focus-click-page-crop.png\nnavigation-baseline-sha256=%s\n' "$navigation_baseline_hash"
            printf 'logical-end-from-baseline-AE=%s\nlogical-end-owner-focused=%s\n' "$ctrl_end_logical_delta" "$ctrl_end_owner_focus"
            printf 'page-down-key-symbol=Page_Down\npage-down-max=%s\nmaterial-change-threshold-AE=100\nstability-threshold-AE=100\n' "$page_down_max"
            printf 'material-change-seen=%s\nstable-endpoint-reached=%s\nstable-step=%s\n' "$material_change_seen" "$stable_endpoint_reached" "$stable_step"
            printf 'stable-previous-crop=%s\nstable-endpoint-crop=%s\nstable-endpoint-owner-focused=%s\n' "$stable_previous_crop" "$stable_endpoint_crop" "$stable_endpoint_focus"
            printf 'click-focus-proof=document-focus-click-proof.txt\npage-down-step-proof=page-down-steps.txt\n'
        } > "$output/third-page-navigation-proof.txt"

        if [[ "$stable_endpoint_reached" == true ]]; then
            cp "$output/$stable_endpoint_full" "$output/final.png"
            cp "$output/$stable_endpoint_crop" "$output/final-page-crop.png"
            cp "$output/$stable_endpoint_status" "$output/final-status-bar-crop.png"
            cp "$output/$stable_endpoint_state" "$output/final-state.txt"
            track_screenshot final.png
            printf 'final-source-step=%s\nfinal-screenshot=final.png\nfinal-status-bar-crop=final-status-bar-crop.png\n' "$stable_step" >> "$output/third-page-navigation-proof.txt"
            navigation_evidence+=(final.png final-page-crop.png final-status-bar-crop.png final-state.txt)
        fi

        if [[ "$material_change_seen" == true &&
              "$stable_endpoint_reached" == true &&
              "$ctrl_end_owner_focus" == true &&
              "$stable_endpoint_focus" == true ]]; then
            record physical-third-page-navigation passed "A document-body click established keyboard focus; Ctrl+End established the logical endpoint, then bounded physical Page_Down inputs produced a materially changed viewport and converged to a stable endpoint tied to the deterministic three-page rendering proof." "${navigation_evidence[@]}" page-down-steps.txt third-page-navigation-proof.txt
        else
            record physical-third-page-navigation failed "The focused Ctrl+End plus bounded Page_Down sequence did not prove both material viewport movement and a stable endpoint while retaining owner focus." "${navigation_evidence[@]}" page-down-steps.txt third-page-navigation-proof.txt
        fi
        if [[ "$stable_endpoint_reached" == true ]] && image_nonblank_varied final-page-crop.png; then
            record nonblank-final-page-render passed "The final crop comes from the stable bounded Page_Down endpoint and is nonblank with measurable visual variation; the retained status crop is for manual review, not OCR." final.png final-page-crop.png final-status-bar-crop.png final-state.txt page-down-steps.txt third-page-navigation-proof.txt
        else
            record nonblank-final-page-render failed "No stable Page_Down endpoint was available, or ImageMagick could not prove its final crop nonblank and varied." "${navigation_evidence[@]}" page-down-steps.txt third-page-navigation-proof.txt
        fi
    else
        record physical-third-page-navigation failed "The geometry-derived document-body click did not complete while retaining owner focus, so Ctrl+End navigation was not attempted." document-focus-click.png document-focus-click-page-crop.png document-focus-click-state.txt document-focus-click-proof.txt
        record nonblank-final-page-render failed "Final-page rendering was not attempted because document-surface keyboard focus was not proven." document-focus-click.png document-focus-click-state.txt document-focus-click-proof.txt
    fi
else
    record physical-third-page-navigation failed "Navigation was not attempted because the focused FreeW owner or baseline crop was unavailable." probe-incomplete.txt
    record nonblank-final-page-render failed "Final-page rendering was not attempted because the baseline capture was unavailable." probe-incomplete.txt
fi

focused_test_succeeded() {
    local path="$1"
    [[ -s "$path" ]] && grep -Eiq 'Failed:[[:space:]]*0' "$path" && grep -Eiq 'Passed:[[:space:]]*[1-9][[:digit:]]*' "$path"
}

if focused_test_succeeded "$shared_plan_test" && focused_test_succeeded "$avalonia_table_test"; then
    record shared-plan-proof passed "Both focused planner and Avalonia table-structure outputs contain passing test summaries." shared-plan-test.txt avalonia-table-structure-test.txt
else
    {
        printf 'shared-plan-test-path=%s\n' "$shared_plan_test"
        if [[ -f "$shared_plan_test" ]]; then cat "$shared_plan_test"; fi
        printf '\navalonia-table-structure-test-path=%s\n' "$avalonia_table_test"
        if [[ -f "$avalonia_table_test" ]]; then cat "$avalonia_table_test"; fi
    } > "$output/shared-plan-proof.txt"
    record shared-plan-proof failed "Both focused planner and Avalonia table-structure outputs are required to contain passing test summaries." shared-plan-proof.txt
fi

if ! python3 - "$records" <<'PY'
import json
import sys

required_ids = [
    "visible-window-discovery",
    "generated-fixture-hash-integrity",
    "physical-third-page-navigation",
    "nonblank-final-page-render",
    "shared-plan-proof",
]
with open(sys.argv[1], encoding="utf-8") as handle:
    rows = [json.loads(line) for line in handle if line.strip()]
passed = (
    len(rows) == len(required_ids)
    and [row.get("id") for row in rows] == required_ids
    and all(row.get("status") == "passed" for row in rows)
)
raise SystemExit(0 if passed else 1)
PY
then
    exit 1
fi
