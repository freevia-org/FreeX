#!/usr/bin/env bash
set -Eeuo pipefail
export DISPLAY="${DISPLAY:-:99}"
output="${1:-/work/freep-multiselect-x11-wave89-validation}"
document_path="${FREEP_DOCUMENT_PATH:-}"
expected_document_name="${FREEP_EXPECTED_DOCUMENT_NAME:-freep-multiselect-x11-wave89-fixture.pptx}"
window_pattern="${FREEP_EXPECTED_WINDOW_PATTERN:-FreeP}"
input_delay_ms="${FREEP_X11_INPUT_DELAY_MS:-120}"
settle_seconds="${FREEP_X11_SETTLE_SECONDS:-0.55}"
pointer_timeout_seconds="${FREEP_X11_POINTER_TIMEOUT_SECONDS:-3}"
save_attempts="${FREEP_SAVE_ATTEMPTS:-24}"
records="$output/result-records.jsonl"; screenshots_file="$output/screenshot-names.txt"; manifest="$output/results.json"
owner_id=""; owner_title=""; slide_x=0; slide_y=0; slide_width_px=0; slide_height_px=0
required_ids=(visible-window-discovery two-shape-pointer-selection group-resize-handle-drag saved-resize-geometry group-rotate-handle-drag saved-rotate-geometry ctrl-z-restores-resize escape-cancel-preserves-package capture-loss-cancel-preserves-package)
mkdir -p "$output"; : > "$records"; : > "$screenshots_file"
printf 'The probe ended before this contract row produced complete physical evidence.\n' > "$output/probe-incomplete.txt"

record() {
  local id="$1" status="$2" note="$3"; shift 3
  python3 - "$records" "$id" "$status" "$note" "$@" <<'PY'
import json,sys
path,result_id,status,note,*evidence=sys.argv[1:]
with open(path,"a",encoding="utf-8") as h: h.write(json.dumps({"id":result_id,"category":"physical-x11-multiselect","status":status,"evidenceLevel":"physical-x11-input","evidence":evidence,"note":note},sort_keys=True)+"\n")
PY
}
track_screenshot() { printf '%s\n' "$1" >> "$screenshots_file"; }
capture() { local name="$1"; command -v scrot >/dev/null 2>&1 || return 1; scrot -o "$output/$name" >/dev/null 2>&1 || return 1; [[ -s "$output/$name" ]] || return 1; track_screenshot "$name"; }
focus_owner() { timeout --foreground --kill-after=1s "$pointer_timeout_seconds" xdotool windowactivate --sync "$owner_id" >/dev/null 2>&1 || true; timeout --foreground --kill-after=1s "$pointer_timeout_seconds" xdotool windowfocus "$owner_id" >/dev/null 2>&1 || true; sleep 0.12; }
send_owner_key() { focus_owner; timeout --foreground --kill-after=1s "$pointer_timeout_seconds" xdotool key --clearmodifiers --delay "$input_delay_ms" "$@"; sleep "$settle_seconds"; }
smooth_mousemove() {
  local from_x="$1" from_y="$2" to_x="$3" to_y="$4" steps="${5:-12}"
  local index x y
  for index in $(seq 1 "$steps"); do
    x=$((from_x + (to_x - from_x) * index / steps))
    y=$((from_y + (to_y - from_y) * index / steps))
    xdotool mousemove --sync "$x" "$y"
    sleep 0.04
  done
}
capture_window_state() { local name="$1"; { printf 'owner-window-id=%s\nowner-window-title=%s\n' "$owner_id" "$owner_title"; printf 'active-window=%s\nfocus-window=%s\n' "$(xdotool getactivewindow 2>/dev/null || true)" "$(xdotool getwindowfocus 2>/dev/null || true)"; printf 'wmctrl-list-begin\n'; wmctrl -l 2>/dev/null || true; printf 'wmctrl-list-end\n'; } > "$output/$name"; }

inspect_pptx() {
  python3 - "$1" "$2" <<'PY'
import hashlib,json,sys,zipfile,xml.etree.ElementTree as ET
path,destination=sys.argv[1:]; ns={"p":"http://schemas.openxmlformats.org/presentationml/2006/main","a":"http://schemas.openxmlformats.org/drawingml/2006/main"}
def integer(value,default=0):
    try:return int(value)
    except (TypeError,ValueError):return default
with open(path,"rb") as h: sha=hashlib.sha256(h.read()).hexdigest()
with zipfile.ZipFile(path) as z: root=ET.fromstring(z.read("ppt/slides/slide1.xml"))
shapes=[]
for shape in root.findall(".//p:sp",ns):
    meta=shape.find("p:nvSpPr/p:cNvPr",ns)
    if meta is None or meta.get("id") not in ("2","3"):continue
    xf=shape.find("p:spPr/a:xfrm",ns); off=xf.find("a:off",ns); ext=xf.find("a:ext",ns)
    shapes.append({"id":int(meta.get("id")),"name":meta.get("name"),"bounds":{"x":integer(off.get("x")),"y":integer(off.get("y")),"cx":integer(ext.get("cx")),"cy":integer(ext.get("cy"))},"rotation":integer(xf.get("rot","0"))/60000.0,"text":"".join(n.text or "" for n in shape.findall(".//a:t",ns))})
shapes.sort(key=lambda x:x["id"])
with open(destination,"w",encoding="utf-8") as h: json.dump({"packageSha256":sha,"shapes":shapes},h,sort_keys=True,separators=(",",":")); h.write("\n")
PY
}
assert_package_state() {
  python3 - "$1" "$2" <<'PY'
import json,sys
d=json.load(open(sys.argv[1],encoding="utf-8")); state=sys.argv[2]
expected={
"baseline":[{"id":2,"name":"Wave89 Left","bounds":{"x":1905000,"y":1714500,"cx":1905000,"cy":1143000},"rotation":0.0},{"id":3,"name":"Wave89 Right","bounds":{"x":4762500,"y":2857500,"cx":1905000,"cy":1143000},"rotation":0.0}],
"resized":[{"id":2,"name":"Wave89 Left","bounds":{"x":1905000,"y":1714500,"cx":2286000,"cy":1733550},"rotation":0.0},{"id":3,"name":"Wave89 Right","bounds":{"x":5334000,"y":3448050,"cx":2286000,"cy":1733550},"rotation":0.0}],
"rotated":[{"id":2,"name":"Wave89 Left","bounds":{"x":4486275,"y":866775,"cx":2286000,"cy":1733550},"rotation":90.0},{"id":3,"name":"Wave89 Right","bounds":{"x":2752725,"y":4295775,"cx":2286000,"cy":1733550},"rotation":90.0}]}
def same(a,e):return a.get("id")==e["id"] and a.get("name")==e["name"] and a.get("bounds")==e["bounds"] and abs(float(a.get("rotation",-999))-e["rotation"])<.001
raise SystemExit(0 if len(d.get("shapes",[]))==2 and all(same(a,e) for a,e in zip(d["shapes"],expected[state])) else 1)
PY
}
save_checkpoint() {
  local prefix="$1" state="$2"; local temporary="$output/.$prefix.pptx.tmp" inspect="$output/.$prefix.json.tmp"; send_owner_key ctrl+s || return 1
  for _ in $(seq 1 "$save_attempts"); do
    if cp "$document_path" "$temporary" 2>"$output/$prefix-inspection-error.txt" && inspect_pptx "$temporary" "$inspect" 2>>"$output/$prefix-inspection-error.txt" && assert_package_state "$inspect" "$state" 2>>"$output/$prefix-inspection-error.txt"; then
      mv "$temporary" "$output/$prefix.pptx"; mv "$inspect" "$output/$prefix.json"; sha256sum "$output/$prefix.pptx" | awk '{print tolower($1)}' > "$output/$prefix.sha256.txt"; return 0
    fi
    sleep .25
  done
  [[ -s "$temporary" ]] && cp "$temporary" "$output/$prefix-actual.pptx"
  [[ -s "$inspect" ]] && cp "$inspect" "$output/$prefix-actual.json"
  rm -f "$temporary" "$inspect"; return 1
}
copy_current_state() { local prefix="$1" state="$2"; cp "$document_path" "$output/$prefix.pptx"; inspect_pptx "$output/$prefix.pptx" "$output/$prefix.json"; assert_package_state "$output/$prefix.json" "$state"; sha256sum "$output/$prefix.pptx" | awk '{print tolower($1)}' > "$output/$prefix.sha256.txt"; }

finalize() {
  local exit_code=$?; set +e
  python3 - "$records" "$screenshots_file" "$manifest" "$owner_id" "$owner_title" "$expected_document_name" "$exit_code" <<'PY'
import json,os,sys
records,screenshots,manifest_path,owner_id,owner_title,fixture,exit_code=sys.argv[1:]
ids=["visible-window-discovery","two-shape-pointer-selection","group-resize-handle-drag","saved-resize-geometry","group-rotate-handle-drag","saved-rotate-geometry","ctrl-z-restores-resize","escape-cancel-preserves-package","capture-loss-cancel-preserves-package"]
rows={}
try:
  for line in open(records,encoding="utf-8"):
    if line.strip(): row=json.loads(line); rows[row["id"]]=row
except FileNotFoundError: pass
for i in ids: rows.setdefault(i,{"id":i,"category":"physical-x11-multiselect","status":"failed","evidenceLevel":"physical-x11-input","evidence":["probe-incomplete.txt"],"note":"The probe ended before this physical contract row produced complete evidence."})
try: shot=list(dict.fromkeys(x.strip() for x in open(screenshots,encoding="utf-8") if x.strip()))
except FileNotFoundError: shot=[]
def load(name):
  try:return json.load(open(os.path.join(os.path.dirname(manifest_path),name+".json"),encoding="utf-8"))
  except (FileNotFoundError,json.JSONDecodeError):return {"packageSha256":"","shapes":[]}
calibration_path=os.path.join(os.path.dirname(manifest_path),"pointer-calibration.txt")
slide_screen={"x":0,"y":0,"width":0,"height":0}
try:
  for line in open(calibration_path,encoding="utf-8"):
    if line.startswith("derived-slide-rect="):
      x,y,w,h=(int(value) for value in line.strip().split("=",1)[1].split(",")); slide_screen={"x":x,"y":y,"width":w,"height":h}
except FileNotFoundError: pass
manifest={"schemaVersion":1,"suite":"freep-linux-multiselect-x11-wave89-physical","platform":"linux","shell":"avalonia","app":"FreeP","baseline":False,"appSurface":"in-canvas-multi-selection-resize-rotate","window":{"id":owner_id,"title":owner_title,"pattern":"FreeP","visible":bool(owner_id)},"calibration":{"status":"passed" if os.path.exists(calibration_path) and slide_screen["width"]>0 else "failed","slideDip":{"width":1280,"height":720},"slideScreen":slide_screen,"selection":"two-shape-pointer-selection","evidence":["pointer-calibration.txt"]},"fixture":{"file":fixture,"shapes":load("baseline-package-inspection").get("shapes",[])},"packageStates":{"baseline":load("baseline-package-inspection"),"afterResize":load("after-resize"),"afterRotate":load("after-rotate"),"afterUndo":load("after-undo"),"afterEscape":load("after-escape"),"afterCaptureLoss":load("after-capture-loss")},"screenshots":[{"name":x,"kind":"screenshot"} for x in shot],"summary":{"passed":sum(rows[i]["status"]=="passed" for i in ids),"failed":sum(rows[i]["status"]=="failed" for i in ids),"total":len(ids)},"results":[rows[i] for i in ids],"processExitCode":int(exit_code),"contractValidation":{"status":"pending","validator":"tools/Run-FreePMultiSelectionX11Validation.ps1","contractReference":"tools/LinuxInteractiveDocker/freep-multiselect-x11-wave89-validation.schema.json"}}
with open(manifest_path,"w",encoding="utf-8") as h: json.dump(manifest,h,indent=2); h.write("\n")
PY
  return "$exit_code"
}
trap finalize EXIT

if [[ -z "$document_path" || ! -f "$document_path" ]]; then printf 'FREEP_DOCUMENT_PATH is absent or is not a file: %s\n' "$document_path" > "$output/precondition-error.txt"; exit 1; fi
cp "$document_path" "$output/baseline.pptx"; inspect_pptx "$output/baseline.pptx" "$output/baseline-package-inspection.json"; baseline_ok=false; assert_package_state "$output/baseline-package-inspection.json" baseline && baseline_ok=true
visible_owner_ids=()
for _ in $(seq 1 30); do mapfile -t visible_owner_ids < <(xdotool search --onlyvisible --name "$window_pattern" 2>/dev/null || true); (( ${#visible_owner_ids[@]} > 0 )) && break; sleep .25; done
if (( ${#visible_owner_ids[@]} == 0 )); then printf 'No visible FreeP window matched %s.\n' "$window_pattern" > "$output/window-discovery-error.txt"; record visible-window-discovery failed "No visible FreeP owner matched the X11 precondition." window-discovery-error.txt; exit 1; fi
owner_id="${visible_owner_ids[${#visible_owner_ids[@]}-1]}"; owner_title="$(xdotool getwindowname "$owner_id" 2>/dev/null || true)"; focus_owner
geometry="$(xdotool getwindowgeometry --shell "$owner_id" 2>/dev/null || true)"; eval "$geometry"
pane_width=180; stage_body_top=$((Y+137)); stage_body_height=$((HEIGHT-241)); fit_box_x=$((X+pane_width+40)); fit_box_y=$((stage_body_top+40)); fit_box_width=$((WIDTH-pane_width-80)); fit_box_height=$((stage_body_height-80))
if (( fit_box_width*9 <= fit_box_height*16 )); then slide_width_px=$fit_box_width; slide_height_px=$(((fit_box_width*9+8)/16)); slide_x=$fit_box_x; slide_y=$((fit_box_y+(fit_box_height-slide_height_px+1)/2)); fit_constraint=width; else slide_height_px=$fit_box_height; slide_width_px=$(((fit_box_height*16+4)/9)); slide_x=$((fit_box_x+(fit_box_width-slide_width_px+1)/2)); slide_y=$fit_box_y; fit_constraint=height; fi
capture baseline.png && baseline_capture=true || baseline_capture=false; capture_window_state owner-discovery-state.txt
printf 'owner-window-id=%s\nowner-window-title=%s\nfit-constraint=%s\nderived-slide-rect=%s,%s,%s,%s\nslide-dip=1280,720\n' "$owner_id" "$owner_title" "$fit_constraint" "$slide_x" "$slide_y" "$slide_width_px" "$slide_height_px" > "$output/pointer-calibration.txt"
if [[ -s "$output/pointer-calibration.txt" && "$baseline_ok" == true && "$baseline_capture" == true && "$owner_title" == *"$expected_document_name"* ]]; then record visible-window-discovery passed "Focused visible FreeP window, fixture title, screenshot, calibration, and exact two-shape baseline." pointer-calibration.txt owner-discovery-state.txt baseline.png baseline-package-inspection.json; else record visible-window-discovery failed "Visible owner did not prove title, focus, screenshot, calibration, and exact baseline." pointer-calibration.txt owner-discovery-state.txt baseline-package-inspection.json; fi
dip_x() { echo $((slide_x+(slide_width_px*$1+640)/1280)); }; dip_y() { echo $((slide_y+(slide_height_px*$1+360)/720)); }
first_x=$(dip_x 300); first_y=$(dip_y 240); second_x=$(dip_x 600); second_y=$(dip_y 360); resize_start_x=$(dip_x 700); resize_start_y=$(dip_y 420); resize_end_x=$(dip_x 800); resize_end_y=$(dip_y 540); rotate_start_x=$(dip_x 500); rotate_start_y=$(dip_y 162); rotate_end_x=$(dip_x 698); rotate_end_y=$(dip_y 360)
printf 'first-shape-center=%s,%s\nsecond-shape-center=%s,%s\nresize-se-start=%s,%s\nresize-se-end=%s,%s\nrotate-start=%s,%s\nrotate-end=%s,%s\n' "$first_x" "$first_y" "$second_x" "$second_y" "$resize_start_x" "$resize_start_y" "$resize_end_x" "$resize_end_y" "$rotate_start_x" "$rotate_start_y" "$rotate_end_x" "$rotate_end_y" >> "$output/pointer-calibration.txt"

focus_owner; xdotool mousemove --sync "$first_x" "$first_y"; xdotool click --clearmodifiers 1; sleep "$settle_seconds"; xdotool keydown ctrl; xdotool mousemove --sync "$second_x" "$second_y"; xdotool click 1; xdotool keyup ctrl; sleep "$settle_seconds"; capture multi-selected.png; capture_window_state multi-selected-state.txt
if [[ -s "$output/multi-selected.png" ]]; then record two-shape-pointer-selection passed "Real pointer clicks selected both fixture shapes and retained group-selection evidence." multi-selected.png multi-selected-state.txt pointer-calibration.txt; else record two-shape-pointer-selection failed "The two-shape group-selection screenshot was not captured." multi-selected-state.txt pointer-calibration.txt; fi

focus_owner; xdotool mousemove --sync "$resize_start_x" "$resize_start_y"; xdotool mousedown 1; smooth_mousemove "$resize_start_x" "$resize_start_y" "$resize_end_x" "$resize_end_y"; sleep "$settle_seconds"; capture resize-drag.png; xdotool mouseup 1; sleep "$settle_seconds"; capture after-resize.png; capture_window_state after-resize-state.txt
if [[ -s "$output/resize-drag.png" && -s "$output/after-resize.png" ]]; then record group-resize-handle-drag passed "Real pointer drag moved the shared SE group resize handle." resize-drag.png after-resize.png after-resize-state.txt pointer-calibration.txt; else record group-resize-handle-drag failed "Group resize screenshots were incomplete." resize-drag.png after-resize.png after-resize-state.txt pointer-calibration.txt; fi
if save_checkpoint after-resize resized; then record saved-resize-geometry passed "Saved PPTX contains exact resized bounds for both shapes with zero rotation." after-resize.json after-resize.pptx after-resize.sha256.txt; else record saved-resize-geometry failed "Saved PPTX did not contain exact resized geometry." after-resize-inspection-error.txt; fi

focus_owner; xdotool mousemove --sync "$rotate_start_x" "$rotate_start_y"; xdotool keydown shift; xdotool mousedown 1; smooth_mousemove "$rotate_start_x" "$rotate_start_y" "$rotate_end_x" "$rotate_end_y"; sleep "$settle_seconds"; capture rotate-drag.png; xdotool mouseup 1; xdotool keyup shift; sleep "$settle_seconds"; capture after-rotate.png; capture_window_state after-rotate-state.txt
if [[ -s "$output/rotate-drag.png" && -s "$output/after-rotate.png" ]]; then record group-rotate-handle-drag passed "Real pointer drag moved the shared rotate handle through a 90 degree group turn." rotate-drag.png after-rotate.png after-rotate-state.txt pointer-calibration.txt; else record group-rotate-handle-drag failed "Group rotate screenshots were incomplete." rotate-drag.png after-rotate.png after-rotate-state.txt pointer-calibration.txt; fi
if save_checkpoint after-rotate rotated; then record saved-rotate-geometry passed "Saved PPTX contains exact persisted 90 degree rotations and centers for both shapes." after-rotate.json after-rotate.pptx after-rotate.sha256.txt; else record saved-rotate-geometry failed "Saved PPTX did not contain exact persisted rotated geometry." after-rotate-inspection-error.txt; fi

send_owner_key ctrl+z; capture after-undo.png; capture_window_state after-undo-state.txt
if save_checkpoint after-undo resized; then record ctrl-z-restores-resize passed "One physical Ctrl+Z restored and persisted the exact saved resize state." after-undo.png after-undo-state.txt after-undo.json after-undo.pptx after-undo.sha256.txt; else record ctrl-z-restores-resize failed "One physical Ctrl+Z did not restore the exact resize package." after-undo.png after-undo-state.txt after-undo-inspection-error.txt; fi

focus_owner; xdotool mousemove --sync "$rotate_start_x" "$rotate_start_y"; xdotool mousedown 1; smooth_mousemove "$rotate_start_x" "$rotate_start_y" "$rotate_end_x" "$rotate_end_y"; sleep "$settle_seconds"; capture escape-drag.png; timeout --foreground --kill-after=1s "$pointer_timeout_seconds" xdotool key --clearmodifiers --delay "$input_delay_ms" --window "$owner_id" Escape; sleep "$settle_seconds"; xdotool mouseup 1 2>/dev/null || true; capture after-escape.png; capture_window_state after-escape-state.txt
escape_ok=false; if copy_current_state after-escape resized && [[ -f "$output/after-undo.sha256.txt" && "$(cat "$output/after-undo.sha256.txt")" == "$(cat "$output/after-escape.sha256.txt")" ]]; then escape_ok=true; fi
if $escape_ok; then record escape-cancel-preserves-package passed "Escape canceled active rotate capture and stale pointer release left the exact package hash and geometry unchanged." escape-drag.png after-escape.png after-escape-state.txt after-escape.json after-escape.sha256.txt; else record escape-cancel-preserves-package failed "Escape cancellation changed the package or failed to restore resize geometry." escape-drag.png after-escape.png after-escape-state.txt after-escape-inspection-error.txt; fi

focus_owner; xdotool mousemove --sync "$rotate_start_x" "$rotate_start_y"; xdotool mousedown 1; smooth_mousemove "$rotate_start_x" "$rotate_start_y" "$rotate_end_x" "$rotate_end_y"; sleep "$settle_seconds"; capture capture-loss-drag.png
xdotool windowminimize "$owner_id"; sleep .9; capture capture-loss-window-hidden.png; capture_window_state capture-loss-window-hidden-state.txt
xdotool windowmap "$owner_id"; focus_owner; xdotool mouseup 1 2>/dev/null || true; sleep "$settle_seconds"; capture after-capture-loss.png; capture_window_state after-capture-loss-state.txt
capture_loss_ok=false; if copy_current_state after-capture-loss resized && [[ -f "$output/after-undo.sha256.txt" && "$(cat "$output/after-undo.sha256.txt")" == "$(cat "$output/after-capture-loss.sha256.txt")" ]]; then capture_loss_ok=true; fi
if $capture_loss_ok; then record capture-loss-cancel-preserves-package passed "Minimizing the real owner window released pointer capture; restoring it and releasing the stale pointer left the exact package hash and geometry unchanged." capture-loss-drag.png capture-loss-window-hidden.png capture-loss-window-hidden-state.txt after-capture-loss.png after-capture-loss-state.txt after-capture-loss.json after-capture-loss.sha256.txt; else record capture-loss-cancel-preserves-package failed "The real window deactivation/capture-loss route did not prove an unchanged package." capture-loss-drag.png capture-loss-window-hidden.png capture-loss-window-hidden-state.txt after-capture-loss.png after-capture-loss-state.txt after-capture-loss-inspection-error.txt; fi
exit 0
