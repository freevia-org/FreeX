#!/usr/bin/env bash
set -Eeuo pipefail
output="${1:-/work/freep-internal-slide-hyperlink}"
mkdir -p "$output"
window="$(xdotool search --onlyvisible --name 'FreeP' | tail -1)"
[[ -n "$window" ]] || { echo 'FreeP window not found' > "$output/failure.txt"; exit 1; }
xdotool windowactivate --sync "$window"
sleep 1
scrot "$output/01-seeded-slide-1.png"
[[ -f "$output/fixture-postcondition.txt" ]] || exit 1

# Bind the physical workflow to slide 1 and the seeded rectangle before opening the dialog.
xdotool mousemove 90 280 click 1
sleep .4
xdotool mousemove 730 455 click 1
xdotool key alt+n; sleep .25; xdotool key k
sleep 1
scrot "$output/02-insert-hyperlink-dialog.png"
dialog="$(xdotool search --onlyvisible --name 'Hyperlink' | tail -1)"
[[ -n "$dialog" ]] || { echo 'real Insert Hyperlink dialog not found' > "$output/failure.txt"; exit 1; }
xdotool mousemove 456 367 click 1
xdotool mousemove 650 425 click 1
xdotool key Home; xdotool key Down; xdotool key Return
scrot "$output/03-hyperlink-slide-2-selected.png"
xdotool mousemove 705 513 click 1
sleep .7
scrot "$output/04-hyperlink-committed.png"
[[ -f "$output/authoring-postcondition.txt" ]] || { echo 'dialog commit did not author a shape hyperlink' > "$output/failure.txt"; exit 1; }
grep -q '^targetSlideId=' "$output/authoring-postcondition.txt"
fixture_target="$(sed -n 's/^slide2Id=//p' "$output/fixture-postcondition.txt")"
grep -q '^currentSlideIndex=0$' "$output/fixture-postcondition.txt"
authored_target="$(sed -n 's/^targetSlideId=//p' "$output/authoring-postcondition.txt")"
[[ -n "$fixture_target" && "$authored_target" == "$fixture_target" ]]

xdotool windowactivate --sync "$window"
mapfile -t before_slideshow_windows < <(xdotool search --onlyvisible --name '.*' 2>/dev/null || true)
xdotool key shift+F5
sleep 2
scrot "$output/05-slideshow-slide-1.png"
slideshow_window=""
for candidate in $(xdotool search --onlyvisible --name '.*' 2>/dev/null || true); do
    skip=false
    for existing in "${before_slideshow_windows[@]}"; do [[ "$candidate" == "$existing" ]] && skip=true; done
    if ! $skip; then slideshow_window="$candidate"; break; fi
done
[[ -n "$slideshow_window" ]] || { echo 'slideshow window was not discovered' > "$output/failure.txt"; exit 1; }
eval "$(xdotool getwindowgeometry --shell "$slideshow_window")"
read -r shape_x shape_y shape_cx shape_cy slide_cx slide_cy < <(python3 - "$output/fixture-postcondition.txt" <<'PY'
import re, sys
values = {}
for line in open(sys.argv[1], encoding='utf-8'):
    if '=' in line:
        key, value = line.rstrip().split('=', 1)
        if key != 'slide1Id' and key != 'slide2Id':
            values[key] = int(value)
print(*(values[k] for k in ('shapeOffsetXEmu','shapeOffsetYEmu','shapeExtentCxEmu','shapeExtentCyEmu','slideSizeCxEmu','slideSizeCyEmu')))
PY
)
click_x=$(( X + (shape_x + shape_cx / 2) * WIDTH / slide_cx ))
click_y=$(( Y + (shape_y + shape_cy / 2) * HEIGHT / slide_cy ))
printf 'slideshow-window-id=%s\nslideshow-geometry=%sx%s+%s+%s\nclick-x=%s\nclick-y=%s\n' "$slideshow_window" "$WIDTH" "$HEIGHT" "$X" "$Y" "$click_x" "$click_y" > "$output/activation-click-proof.txt"
xdotool mousemove "$click_x" "$click_y" click 1
sleep 1
scrot "$output/06-slideshow-target-slide-2.png"

postcondition="$output/activation-postcondition.txt"
[[ -f "$postcondition" ]] || { echo 'postcondition file missing after physical slideshow click' > "$output/failure.txt"; exit 1; }
grep -q '^activation=internal-slide-hyperlink$' "$postcondition"
grep -q '^currentSlideIndex=1$' "$postcondition"
grep -q '^targetSlideId=' "$postcondition"
activated_target="$(sed -n 's/^targetSlideId=//p' "$postcondition")"
[[ "$activated_target" == "$authored_target" ]]
printf '{"schemaVersion":1,"suite":"freep-internal-slide-hyperlink-x11","platform":"linux","shell":"avalonia","app":"FreeP","summary":{"passed":1,"failed":0,"total":1},"results":[{"id":"internal-slide-hyperlink-physical-workflow","category":"physical-x11-input","status":"passed","evidenceLevel":"physical-x11-input","evidence":["fixture-postcondition.txt","activation-click-proof.txt","01-seeded-slide-1.png","02-insert-hyperlink-dialog.png","03-hyperlink-slide-2-selected.png","04-hyperlink-committed.png","authoring-postcondition.txt","05-slideshow-slide-1.png","06-slideshow-target-slide-2.png","activation-postcondition.txt"],"note":"The real dialog authored the seeded slide-2 id on the selected rectangle; the probe transformed its exact slide-space center into the discovered slideshow window and physical activation reached currentSlideIndex=1 with the same target id."}]}' > "$output/freep-internal-slide-hyperlink-validation.json"
