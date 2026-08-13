#!/usr/bin/env bash
set -euo pipefail
export DISPLAY="${DISPLAY:-:99}"

result=/work/table-properties-result.json
evidence=/work/table-properties-x11
mkdir -p "$evidence"

window_id="$(sed -n 's/.*"windowId": "\{0,1\}\([^",]*\).*/\1/p' /work/ready.json)"
[[ "$window_id" =~ ^[0-9]+$ ]] || { echo 'FreeW ready window ID was not available.' >&2; exit 10; }
sleep 0.5

xdotool windowactivate --sync "$window_id"
dialog_id="$(xdotool search --onlyvisible --name '^Table Properties$' | tail -n 1)"
[[ "$dialog_id" =~ ^[0-9]+$ ]] || { echo 'The real Table Properties dialog was not available.' >&2; exit 10; }
xdotool windowactivate --sync "$dialog_id"
scrot "$evidence/dialog-open.png"

# Traverse every real production tab header and retain one screenshot per page.
# Dialog-relative coordinates remain stable while Avalonia resizes and re-centers
# the window for pages with different content heights.
xdotool mousemove --window "$dialog_id" 36 46 click 1
sleep 0.2
scrot "$evidence/table-page.png"
xdotool mousemove --window "$dialog_id" 76 46 click 1
sleep 0.2
scrot "$evidence/row-page.png"
xdotool mousemove --window "$dialog_id" 123 46 click 1
sleep 0.2
scrot "$evidence/column-page.png"
xdotool mousemove --window "$dialog_id" 169 46 click 1
sleep 0.2
scrot "$evidence/cell-page.png"
xdotool mousemove --window "$dialog_id" 36 46 click 1
sleep 0.2
scrot "$evidence/table-page-returned.png"

# Return focus to the first editor, then keyboard-traverse Alignment and Text
# wrapping to edit the real Indent editor.
xdotool mousemove --window "$dialog_id" 280 186 click 1
xdotool key Tab
xdotool key Tab
xdotool key Tab
xdotool key ctrl+a
xdotool type --delay 25 12
scrot "$evidence/focus-traversed.png"

# Activate the real OK button relative to the current dialog bounds. The app-side
# focus trace below is the authoritative target identity check.
eval "$(xdotool getwindowgeometry --shell "$dialog_id")"
xdotool mousemove --window "$dialog_id" "$((WIDTH - 137))" "$((HEIGHT - 26))" click 1
for _ in $(seq 1 40); do
  [[ -s "$result" ]] && break
  sleep 0.25
done
[[ -s "$result" ]] || { echo 'FreeW did not emit the Table Properties result.' >&2; exit 11; }

validation="$(python3 - "$result" <<'PY'
import json, sys
data = json.load(open(sys.argv[1], encoding="utf-8"))
trace = data.get("focusTrace", [])
required = ["TablePropertiesPreferredWidthBox", "TablePropertiesTableTab", "TablePropertiesIndentBox", "TabPage:Table", "TabPage:Row", "TabPage:Column", "TabPage:Cell"]
missing = [item for item in required if item not in trace]
if data.get("status") != "applied": raise SystemExit("Unexpected result status: %s" % data.get("status"))
if data.get("tableRows") != 2 or data.get("tableColumns") != 2: raise SystemExit("Unexpected table shape: %sx%s" % (data.get("tableRows"), data.get("tableColumns")))
if data.get("values", {}).get("IndentFromLeftPt") != 12: raise SystemExit("Unexpected indent postcondition: %s" % data.get("values", {}).get("IndentFromLeftPt"))
if missing: raise SystemExit("Missing focus/page targets: %s" % missing)
if not trace or trace[-1] != "TablePropertiesOkButton": raise SystemExit("OK was not the final focused target: %s" % trace)
print(json.dumps(trace, separators=(",", ":")))
PY
)"
focus_trace="$validation"

cat > /work/table-properties-x11-result.json <<EOF
{"probe":"freew-table-properties-x11","status":"passed","dialog":"real","tabsTraversed":true,"tabPages":["Table","Row","Column","Cell"],"focusTrace":$focus_trace,"editedProperty":"IndentFromLeftPt","expectedIndentFromLeftPt":12}
EOF
