#!/usr/bin/env bash
set -euo pipefail

export DISPLAY="${DISPLAY:-:99}"

output="${1:-/work/window-activation}"
input_delay_ms="${FREEX_X11_INPUT_DELAY_MS:-180}"
settle_seconds="${FREEX_X11_SETTLE_SECONDS:-0.45}"
cycle_key="${FREEX_X11_WINDOW_CYCLE_KEY:-ctrl+F6}"
window_id=""
created_window_ids=()

mkdir -p "$output"

freex_window_ids() {
    xdotool search --onlyvisible --name '^.+ - FreeX$' 2>/dev/null | sort -n || true
}

window_bounds_signature() {
    local id
    while read -r id; do
        [[ -z "$id" ]] && continue
        eval "$(xdotool getwindowgeometry --shell "$id" 2>/dev/null)" || continue
        printf '%s:%s,%s,%s,%s\n' "$id" "$X" "$Y" "$WIDTH" "$HEIGHT"
    done < <(freex_window_ids)
}

focus_app() {
    xdotool windowactivate --sync "$window_id" 2>/dev/null || true
    xdotool windowfocus "$window_id" 2>/dev/null || true
    sleep 0.15
}

keytip_key() {
    xdotool key --clearmodifiers --delay "$input_delay_ms" --window "$window_id" "$1"
    sleep "$settle_seconds"
}

enter_view_keytip() {
    focus_app
    xdotool keydown --window "$window_id" Alt_L
    sleep 0.18
    xdotool keyup --window "$window_id" Alt_L
    sleep "$settle_seconds"
    keytip_key w
}

cleanup() {
    local id
    for id in "${created_window_ids[@]}"; do
        [[ -z "$id" || "$id" == "$window_id" ]] && continue
        xdotool windowclose "$id" 2>/dev/null || true
    done
}
trap cleanup EXIT

window_id="$(freex_window_ids | tail -1)"
if [[ -z "$window_id" ]]; then
    printf 'window-discovery=failed\n' > "$output/postcondition.txt"
    exit 2
fi

mapfile -t baseline_window_ids < <(freex_window_ids)
before_count="${#baseline_window_ids[@]}"
printf 'initial-client-id=%s\n' "$window_id" > "$output/postcondition.txt"
printf 'initial-client-ids=%s\n' "${baseline_window_ids[*]}" >> "$output/postcondition.txt"
printf 'initial-active-id=%s\n' "$(xdotool getactivewindow 2>/dev/null || true)" >> "$output/postcondition.txt"
printf 'initial-net-active=%s\n' "$(xprop -root _NET_ACTIVE_WINDOW 2>/dev/null || true)" >> "$output/postcondition.txt"

enter_view_keytip
keytip_key n
keytip_key w
sleep 1

mapfile -t current_window_ids < <(freex_window_ids)
for candidate in "${current_window_ids[@]}"; do
    known=false
    for existing in "${baseline_window_ids[@]}"; do
        if [[ "$candidate" == "$existing" ]]; then
            known=true
            break
        fi
    done
    $known || created_window_ids+=("$candidate")
done

if (( ${#current_window_ids[@]} != before_count + 1 || ${#created_window_ids[@]} != 1 )); then
    printf 'after-new-count=%s\ncreated-client-ids=%s\nnew-window=failed\n' \
        "${#current_window_ids[@]}" "${created_window_ids[*]}" >> "$output/postcondition.txt"
    exit 1
fi

printf 'after-new-client-ids=%s\n' "${current_window_ids[*]}" >> "$output/postcondition.txt"
printf 'after-new-active-id=%s\n' "$(xdotool getactivewindow 2>/dev/null || true)" >> "$output/postcondition.txt"
printf 'after-new-bounds=%s\n' "$(window_bounds_signature | tr '\n' ';')" >> "$output/postcondition.txt"

focus_app
enter_view_keytip
keytip_key a
keytip_key t
sleep 1
printf 'after-arrange-bounds=%s\n' "$(window_bounds_signature | tr '\n' ';')" >> "$output/postcondition.txt"

focus_app
xdotool mousemove --window "$window_id" 520 420 click 1
sleep 0.2
active_before="$(xdotool getactivewindow 2>/dev/null || true)"
net_active_before="$(xprop -root _NET_ACTIVE_WINDOW 2>/dev/null || true)"
xdotool key --clearmodifiers --delay "$input_delay_ms" "$cycle_key"
sleep 1
active_after="$(xdotool getactivewindow 2>/dev/null || true)"
net_active_after="$(xprop -root _NET_ACTIVE_WINDOW 2>/dev/null || true)"

active_after_is_created=false
for candidate in "${created_window_ids[@]}"; do
    [[ "$candidate" == "$active_after" ]] && active_after_is_created=true
done

changed=false
[[ -n "$active_before" && "$active_before" != "$active_after" ]] && changed=true
printf 'active-before-id=%s\nactive-after-id=%s\nactive-changed=%s\nactive-after-is-created=%s\n' \
    "$active_before" "$active_after" "$changed" "$active_after_is_created" >> "$output/postcondition.txt"
printf 'net-active-before=%s\nnet-active-after=%s\n' "$net_active_before" "$net_active_after" >> "$output/postcondition.txt"
if [[ "$active_after_is_created" != true && ${#created_window_ids[@]} -eq 1 ]]; then
    xdotool windowactivate --sync "${created_window_ids[0]}" 2>/dev/null || true
    sleep 0.35
    printf 'direct-xdotool-active-id=%s\n' "$(xdotool getactivewindow 2>/dev/null || true)" >> "$output/postcondition.txt"
fi
printf 'final-client-ids=%s\n' "$(freex_window_ids | tr '\n' ' ')" >> "$output/postcondition.txt"
printf 'wmctrl=%s\n' "$(wmctrl -lG | tr '\n' ';')" >> "$output/postcondition.txt"

cat "$output/postcondition.txt"
if [[ "$changed" != true || "$active_after_is_created" != true ]]; then
    exit 1
fi
