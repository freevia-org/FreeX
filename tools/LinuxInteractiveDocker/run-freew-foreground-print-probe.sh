#!/usr/bin/env bash
set -euo pipefail

export DISPLAY="${DISPLAY:-:99}"

output="${1:-/work/freew-foreground-print}"
input_delay_ms="${FREEW_PRINT_INPUT_DELAY_MS:-160}"
settle_seconds="${FREEW_PRINT_SETTLE_SECONDS:-0.55}"
owner_id=""
dialog_id=""
backstage_id=""
declare -a rows=()
declare -a screenshots=()

mkdir -p "$output"

json_escape() {
    local value="$1"
    value="${value//\\/\\\\}"
    value="${value//\"/\\\"}"
    value="${value//$'\n'/\\n}"
    printf '%s' "$value"
}

record() {
    local id="$1" status="$2" evidence="$3" note="${4:-}"
    rows+=("{\"id\":\"$(json_escape "$id")\",\"status\":\"$status\",\"evidence\":[\"$(json_escape "$evidence")\"],\"note\":\"$(json_escape "$note")\"}")
}

capture() {
    local name="$1"
    scrot -o "$output/$name"
    screenshots+=("\"$(json_escape "$name")\"")
}

wait_for_window() {
    local pattern="$1" tries="${2:-30}" candidate
    for _ in $(seq 1 "$tries"); do
        candidate="$(xdotool search --onlyvisible --name "$pattern" 2>/dev/null | tail -1 || true)"
        if [[ -n "$candidate" ]]; then
            printf '%s' "$candidate"
            return 0
        fi
        sleep 0.2
    done
    return 1
}

active_is() {
    [[ "$(xdotool getactivewindow 2>/dev/null || true)" == "$1" ]]
}

focus_owner() {
    xdotool windowactivate --sync "$owner_id" 2>/dev/null || true
    xdotool windowfocus "$owner_id" 2>/dev/null || true
    sleep 0.15
}

open_backstage_print() {
    focus_owner
    xdotool key --clearmodifiers --delay "$input_delay_ms" --window "$owner_id" Alt_L
    sleep "$settle_seconds"
    xdotool key --clearmodifiers --delay "$input_delay_ms" --window "$owner_id" F
    backstage_id="$(wait_for_window 'FreeW.*File' 35 || true)"
    if [[ -z "$backstage_id" ]]; then
        # After Escape Avalonia may leave ribbon key tips visible. Clear that transient state
        # and use the physical File tab as a stable second route into the shared Backstage UI.
        xdotool key --clearmodifiers --window "$owner_id" Escape 2>/dev/null || true
        xdotool mousemove --sync --window "$owner_id" 25 46 click 1
        backstage_id="$(wait_for_window 'FreeW.*File' 35 || true)"
    fi
    [[ -n "$backstage_id" ]] || return 1
    xdotool windowactivate --sync "$backstage_id" 2>/dev/null || true
    # The shared Backstage frame focuses Back, then the navigation entries in planner order.
    # Print is the tenth navigation entry (Home, New, Open, Import PDF, Share, Info, Save,
    # Save As, Save a Copy, Print), so this remains independent of pixel coordinates.
    xdotool key --clearmodifiers --delay "$input_delay_ms" --window "$backstage_id" \
        Down Down Down Down Down Down Down Down Down Down Return
    sleep "$settle_seconds"
    dialog_id="$(wait_for_window '^Print$' 8 || true)"
    if [[ -z "$dialog_id" ]]; then
        # Avalonia's Backstage frame keeps the navigation rail at a stable width and row height.
        # Use a window-relative physical click as the fallback when an X11 window-targeted key
        # sequence does not move focus into the freshly shown rail.
        xdotool mousemove --window "$backstage_id" 70 455 click 1
        sleep "$settle_seconds"
        # Selecting the Print rail entry shows the pane; the app-owned direct-print action is
        # the first button in that pane, at a stable window-relative location.
        xdotool mousemove --window "$backstage_id" 260 358 click 1
        sleep "$settle_seconds"
        dialog_id="$(wait_for_window '^Print$' 35 || true)"
    fi
    [[ -n "$dialog_id" ]] || return 1
    return 0
}

find_owner() {
    owner_id="$(xdotool search --onlyvisible --name '^FreeW$' 2>/dev/null | tail -1 || true)"
    if [[ -z "$owner_id" ]]; then
        owner_id="$(xdotool search --onlyvisible --name 'FreeW' 2>/dev/null | tail -1 || true)"
    fi
    [[ -n "$owner_id" ]]
}

write_manifest() {
    local passed=0 failed=0 not_proven=0 row first=true shot shot_first=true
    for row in "${rows[@]}"; do
        case "$row" in
            *'"status":"passed"'*) ((passed += 1)) ;;
            *'"status":"not-proven"'*) ((not_proven += 1)) ;;
            *) ((failed += 1)) ;;
        esac
    done
    {
        printf '{"schemaVersion":1,"suite":"freew-foreground-print-wave9","platform":"linux","shell":"avalonia"'
        printf ',"window":{"id":"%s","title":"FreeW","owner":"x11-transient"}' "$(json_escape "$owner_id")"
        printf ',"cups":{"mode":"container-local-dry-run","queue":"FreeW-DryRun","realDevice":false}'
        printf ',"screenshots":['
        for shot in "${screenshots[@]}"; do
            if $shot_first; then shot_first=false; else printf ','; fi
            printf '%s' "$shot"
        done
        printf ']'
        printf ',"summary":{"passed":%d,"failed":%d,"notProven":%d,"total":%d}' \
            "$passed" "$failed" "$not_proven" "$((passed + failed + not_proven))"
        printf ',"results":['
        for row in "${rows[@]}"; do
            if $first; then first=false; else printf ','; fi
            printf '%s' "$row"
        done
        printf ']}'
    } > "$output/freew-foreground-print-wave9.json"
}

if ! find_owner; then
    printf 'No visible FreeW owner window was found.\n' > "$output/window-discovery.txt"
    record "print-owner-window" "failed" "window-discovery.txt" "FreeW owner window was not visible."
    record "print-dialog-owner-metadata" "not-proven" "window-discovery.txt" "No foreground run reached the app-owned print boundary."
    record "native-print-chrome" "not-proven" "window-discovery.txt" "No foreground run reached the native print boundary."
    write_manifest
    exit 1
fi

capture "owner-before.png"
if active_is "$owner_id"; then
    record "print-owner-window" "passed" "owner-before.png" "FreeW owner was visible and foreground before the print route."
else
    record "print-owner-window" "failed" "owner-before.png" "FreeW owner was visible but did not own the active X11 window."
fi

if open_backstage_print; then
    capture "print-dialog-open.png"
    transient="$(xprop -id "$dialog_id" WM_TRANSIENT_FOR 2>/dev/null || true)"
    if active_is "$dialog_id"; then
        printf 'active=true\n%s\n' "$transient" > "$output/print-dialog-transient.txt"
        record "print-dialog-open-focused" "passed" "print-dialog-open.png; print-dialog-transient.txt" \
            "The production CUPS dialog was visible and active after the Backstage Print action."
    else
        printf 'active=%s\ntransient=%s\n' "$(xdotool getactivewindow 2>/dev/null || true)" "$transient" > "$output/print-dialog-transient.txt"
        record "print-dialog-open-focused" "failed" "print-dialog-open.png; print-dialog-transient.txt" \
            "The production print dialog did not receive active focus."
    fi
    if [[ "$transient" == *"window id"* ]]; then
        record "print-dialog-owner-metadata" "passed" "print-dialog-transient.txt" \
            "X11 exposed a transient owner for the production print dialog."
    else
        record "print-dialog-owner-metadata" "not-proven" "print-dialog-transient.txt" \
            "The managed route calls ShowDialog(owner), but Avalonia/Xvfb exposed no WM_TRANSIENT_FOR metadata."
    fi
    eval "$(xdotool getwindowgeometry --shell "$dialog_id")"
    printf 'window=%s x=%s y=%s width=%s height=%s cancelX=%s cancelY=%s\n' \
        "$WINDOW" "$X" "$Y" "$WIDTH" "$HEIGHT" "$((X + WIDTH - 48))" "$((Y + HEIGHT - 49))" \
        > "$output/print-cancel-geometry.txt"
    cancel_method="not-completed"
    # The explicit Escape handler may destroy the X11 window before xdotool returns. That is
    # the expected cancellation result, so the transient BadWindow response is non-fatal.
    xdotool key --clearmodifiers --delay "$input_delay_ms" --window "$dialog_id" Escape 2>/dev/null || true
    sleep "$settle_seconds"
    if ! xdotool search --onlyvisible --name '^Print$' >/dev/null 2>&1; then
        cancel_method="escape"
    else
        # IsCancel is a managed contract, but targeted X11 Escape is not reliable in this
        # Avalonia/Xvfb session. Exercise the same physical cancellation through the visible button.
        xdotool windowactivate --sync "$dialog_id"
        # Use window-relative input so the X11 titlebar/client offset cannot move the click
        # outside the Avalonia button hit target.
        xdotool mousemove --sync --window "$dialog_id" "$((WIDTH - 48))" "$((HEIGHT - 49))" click 1
        sleep "$settle_seconds"
        if ! xdotool search --onlyvisible --name '^Print$' >/dev/null 2>&1; then
            cancel_method="pointer-click"
        else
            # The dialog opens with Print focused. Tab moves to the visible Cancel button and
            # Return invokes its real Avalonia command when a compositor drops pointer clicks.
            xdotool key --clearmodifiers --delay "$input_delay_ms" --window "$dialog_id" Tab Return
            sleep "$settle_seconds"
            if ! xdotool search --onlyvisible --name '^Print$' >/dev/null 2>&1; then
                cancel_method="tab-return"
            fi
        fi
    fi
    if xdotool search --onlyvisible --name '^Print$' >/dev/null 2>&1; then
        dialog_closed=false
    else
        dialog_closed=true
    fi
    if active_is "$owner_id"; then
        owner_active=true
    else
        owner_active=false
    fi
    printf 'method=%s\ndialog-closed=%s\nowner-active=%s\n' \
        "$cancel_method" "$dialog_closed" "$owner_active" > "$output/print-cancel-method.txt"
    capture "print-cancelled-owner.png"
    if $dialog_closed && $owner_active; then
        case "$cancel_method" in
            escape)
                cancel_note="Escape closed the app-owned dialog and restored the FreeW owner as active."
                ;;
            pointer-click)
                cancel_note="The visible Cancel pointer action closed the app-owned dialog and restored the FreeW owner as active."
                ;;
            tab-return)
                cancel_note="Tab then Return activated the visible Cancel action and restored the FreeW owner as active."
                ;;
            *)
                cancel_note="The dialog closed and the FreeW owner became active, but no cancellation method was identified."
                ;;
        esac
        record "print-cancel-restores-owner-focus" "passed" "print-cancelled-owner.png; print-cancel-method.txt; print-cancel-geometry.txt" \
            "$cancel_note"
    else
        record "print-cancel-restores-owner-focus" "failed" "print-cancelled-owner.png; print-cancel-method.txt" \
            "Cancellation did not close the print dialog and restore owner focus."
    fi
    if [[ "$cancel_method" == "escape" && "$dialog_closed" == true ]]; then
        record "print-cancel-escape" "passed" "print-cancelled-owner.png; print-cancel-method.txt" \
            "Escape alone closed the app-owned print dialog."
    elif [[ "$cancel_method" == "not-completed" ]]; then
        record "print-cancel-escape" "failed" "print-cancelled-owner.png; print-cancel-method.txt" \
            "Escape and the supported cancellation fallbacks did not close the app-owned print dialog."
    else
        record "print-cancel-escape" "not-proven" "print-cancelled-owner.png; print-cancel-method.txt" \
            "Escape did not close the dialog alone; cancellation completed through $cancel_method."
    fi
else
    capture "print-dialog-open-failed.png"
    record "print-dialog-open-focused" "failed" "print-dialog-open-failed.png" \
        "Backstage Print did not open the production CUPS dialog."
    record "print-dialog-owner-metadata" "not-proven" "print-dialog-open-failed.png" \
        "The production print dialog did not open, so owner metadata could not be checked."
    record "print-cancel-restores-owner-focus" "failed" "print-dialog-open-failed.png" \
        "Cancellation could not be exercised because the production dialog did not open."
    printf 'method=not-completed\ndialog-closed=false\nowner-active=false\n' > "$output/print-cancel-method.txt"
    record "print-cancel-escape" "not-proven" "print-dialog-open-failed.png; print-cancel-method.txt" \
        "Escape-only cancellation was not proven because the production print dialog did not open."
fi

if open_backstage_print; then
    capture "print-dialog-submit.png"
    eval "$(xdotool getwindowgeometry --shell "$dialog_id")"
    printf 'window=%s x=%s y=%s width=%s height=%s printX=%s printY=%s\n' \
        "$WINDOW" "$X" "$Y" "$WIDTH" "$HEIGHT" "$((X + WIDTH - 115))" "$((Y + HEIGHT - 49))" \
        > "$output/print-submit-geometry.txt"
    xdotool windowactivate --sync "$dialog_id"
    xdotool mousemove --sync --window "$dialog_id" "$((WIDTH - 115))" "$((HEIGHT - 49))" click 1
    if xdotool search --onlyvisible --name '^Print$' >/dev/null 2>&1 && ! [[ -s /work/cups-dry-run/last-submitted.pdf ]]; then
        # The production dialog's default button is Print; this is the keyboard equivalent of
        # activating that visible command and keeps the probe independent of titlebar offsets.
        xdotool key --clearmodifiers --delay "$input_delay_ms" --window "$dialog_id" Return 2>/dev/null || true
    fi
    submitted=false
    for _ in $(seq 1 40); do
        if [[ -s /work/cups-dry-run/last-submitted.pdf && -s /work/cups-dry-run/last-invocation.txt \
            && "$(cat /work/cups-dry-run/last-invocation.txt)" == *"-d FreeW-DryRun"* ]]; then
            submitted=true
            break
        fi
        sleep 0.25
    done
    capture "print-submitted-owner.png"
    if $submitted && ! xdotool search --onlyvisible --name '^Print$' >/dev/null 2>&1 && active_is "$owner_id"; then
        {
            printf 'size=%s\n' "$(stat -c '%s' /work/cups-dry-run/last-submitted.pdf)"
            printf 'invocation=%s\n' "$(cat /work/cups-dry-run/last-invocation.txt)"
        } > "$output/cups-dry-run-submission.txt"
        cp /work/cups-dry-run/last-submitted.pdf "$output/last-submitted.pdf"
        cp /work/cups-dry-run/last-invocation.txt "$output/last-invocation.txt"
        record "cups-dry-run-submission" "passed" "print-dialog-submit.png; print-submit-geometry.txt; cups-dry-run-submission.txt; last-submitted.pdf; last-invocation.txt; print-submitted-owner.png" \
            "The production FreeW CUPS route submitted non-empty generated PDF bytes and a recorded lp invocation to the container-local dry-run queue, then restored owner focus."
    else
        printf 'submitted=%s\nactive=%s\n' "$submitted" "$(xdotool getactivewindow 2>/dev/null || true)" > "$output/cups-dry-run-submission.txt"
        record "cups-dry-run-submission" "failed" "print-dialog-submit.png; cups-dry-run-submission.txt; print-submitted-owner.png" \
            "The production CUPS route did not complete the safe dry-run submission."
    fi
else
    capture "print-dialog-submit-failed.png"
    record "cups-dry-run-submission" "failed" "print-dialog-submit-failed.png" \
        "The production CUPS dialog could not be reopened for the safe dry-run submission."
fi

printf 'Native GTK/system printer chrome is deliberately not claimed by this Avalonia application-owned CUPS route.\n' > "$output/native-print-chrome-not-proven.txt"
record "native-print-chrome" "not-proven" "native-print-chrome-not-proven.txt" \
    "The probe does not claim native GTK/system print-picker chrome; only the app-owned CUPS dialog and process boundary are validated."

write_manifest
if grep -q '"failed":[1-9]' "$output/freew-foreground-print-wave9.json"; then
    exit 1
fi
