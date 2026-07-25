#!/usr/bin/env bash
set -Eeuo pipefail

inspect_pptx() {
    local package_path="$1" destination="$2"
    python3 - "$package_path" "$destination" <<'PY'
import hashlib
import json
import sys
import zipfile
import xml.etree.ElementTree as ET

package_path, destination = sys.argv[1:]
NS = {"p": "http://schemas.openxmlformats.org/presentationml/2006/main",
      "a": "http://schemas.openxmlformats.org/drawingml/2006/main"}

def local_name(tag):
    return tag.rsplit("}", 1)[-1]

def text_of(node):
    return "".join(part.text or "" for part in node.iter("{" + NS["a"] + "}t"))

def bounds(shape):
    xfrm = shape.find(".//p:spPr/a:xfrm", NS)
    if xfrm is None:
        xfrm = shape.find(".//p:txBody/../a:xfrm", NS)
    if xfrm is None:
        return {"x": None, "y": None, "cx": None, "cy": None}
    off = xfrm.find("a:off", NS)
    ext = xfrm.find("a:ext", NS)
    return {"x": int(off.get("x")) if off is not None else None,
            "y": int(off.get("y")) if off is not None else None,
            "cx": int(ext.get("cx")) if ext is not None else None,
            "cy": int(ext.get("cy")) if ext is not None else None}

with open(package_path, "rb") as handle:
    package_sha256 = hashlib.sha256(handle.read()).hexdigest()
with zipfile.ZipFile(package_path) as package:
    slide = ET.fromstring(package.read("ppt/slides/slide1.xml"))
    shapes = []
    for shape in slide.findall(".//p:sp", NS):
        nv = shape.find("p:nvSpPr/p:cNvPr", NS)
        shapes.append({"id": int(nv.get("id")) if nv is not None else None,
                       "name": nv.get("name") if nv is not None else None,
                       "text": text_of(shape),
                       "bounds": bounds(shape)})
    result = {"packageSha256": package_sha256,
              "slide": 1,
              "editableShapes": shapes,
              "pPicCount": len(slide.findall(".//p:pic", NS)),
              "pGraphicFrameCount": len(slide.findall(".//p:graphicFrame", NS))}
with open(destination, "w", encoding="utf-8", newline="\n") as handle:
    json.dump(result, handle, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    handle.write("\n")
PY
}

assert_baseline_inspection() {
    local file="$1" expected_text="$2" expected_bounds="$3"
    python3 - "$file" "$expected_text" "$expected_bounds" <<'PY'
import json, sys
d = json.load(open(sys.argv[1], encoding="utf-8")); shapes = d["editableShapes"]
expected = tuple(map(int, sys.argv[3].split(",")))
if len(shapes) != 1 or shapes[0]["text"] != sys.argv[2] or tuple(shapes[0]["bounds"].values()) != expected or d["pPicCount"] != 0 or d["pGraphicFrameCount"] != 0:
    raise SystemExit(1)
PY
}

assert_duplicate_inspection() {
    local file="$1" first_text="$2" second_text="$3" first_bounds="$4" second_bounds="$5"
    python3 - "$file" "$first_text" "$second_text" "$first_bounds" "$second_bounds" <<'PY'
import json, sys
d = json.load(open(sys.argv[1], encoding="utf-8")); s = d["editableShapes"]
if len(s) != 2 or d["pPicCount"] != 0 or d["pGraphicFrameCount"] != 0:
    raise SystemExit(1)
for shape, text, bounds in zip(s, sys.argv[2:4], sys.argv[4:6]):
    if shape["text"] != text or tuple(shape["bounds"].values()) != tuple(map(int, bounds.split(","))):
        raise SystemExit(1)
PY
}

assert_empty_user_shape_checkpoint() {
    python3 - "$1" <<'PY'
import json, sys
d = json.load(open(sys.argv[1], encoding="utf-8"))
if len(d["editableShapes"]) != 0 or d["pPicCount"] != 0 or d["pGraphicFrameCount"] != 0: raise SystemExit(1)
PY
}

assert_restored_copies() { assert_duplicate_inspection "$@"; }

self_test_inspect_pptx() {
    local inspection="$1"
    python3 - "$inspection" <<'PY'
import json, sys
d = json.load(open(sys.argv[1], encoding="utf-8"))
required = {"packageSha256", "editableShapes", "pPicCount", "pGraphicFrameCount"}
if not required <= d.keys() or d["slide"] != 1 or not isinstance(d["packageSha256"], str):
    raise SystemExit("invalid inspect_pptx output")
for shape in d["editableShapes"]:
    if set(shape) != {"id", "name", "text", "bounds"} or set(shape["bounds"]) != {"x", "y", "cx", "cy"}:
        raise SystemExit("invalid editable shape record")
PY
}

if [[ "${1:-}" == "--inspect-pptx" ]]; then
    [[ $# == 3 ]] || { printf 'usage: %s --inspect-pptx PACKAGE OUTPUT_JSON\n' "$0" >&2; exit 2; }
    inspect_pptx "$2" "$3"
    exit 0
fi

export DISPLAY="${DISPLAY:-:99}"
output="${1:-/work/fps}"
window_pattern="${FREEP_EXPECTED_WINDOW_PATTERN:-FreeP}"
document_path="${FREEP_DOCUMENT_PATH:-}"
expected_document_name="${FREEP_EXPECTED_DOCUMENT_NAME:-$(basename "${document_path:-presentation.pptx}")}"
records="$output/result-records.jsonl"
screenshots_file="$output/screenshot-names.txt"
manifest="$output/results.json"
required_ids=(visible-window-discovery clipboard-copy-x11-preserves-source clipboard-paste-native-editable-shape select-all-multi-shape-mutation cut-all-x11-undoable undo-restores-editable-shapes redo-reapplies-cut paste-after-cut-restores-editable-shapes)

mkdir -p "$output"
: > "$records"
: > "$screenshots_file"
: > "$output/stage1-not-executed.txt"

record() {
    local id="$1" status="$2" note="$3"; shift 3
    python3 - "$records" "$id" "$status" "$note" "$@" <<'PY'
import json, sys
path, result_id, status, note, *evidence = sys.argv[1:]
row = {"id": result_id, "category": "physical-x11-clipboard-shortcut", "status": status,
       "evidenceLevel": "physical-x11-input", "evidence": evidence, "note": note}
with open(path, "a", encoding="utf-8") as h: h.write(json.dumps(row, ensure_ascii=False, sort_keys=True) + "\n")
PY
}

track_screenshot() { printf '%s\n' "$1" >> "$screenshots_file"; }
capture() { local name="$1"; if command -v scrot >/dev/null 2>&1; then scrot -o "$output/$name" && track_screenshot "$name" || true; else printf 'scrot unavailable\n' > "$output/$name"; fi; }
window_ids() { wmctrl -l 2>/dev/null | while read -r id _; do [[ "$id" =~ ^0x[0-9A-Fa-f]+$ ]] && printf '%d\n' "$id"; done; }
focus_owner() { timeout --foreground --kill-after=1s "${FREEP_X11_POINTER_TIMEOUT_SECONDS:-3}" xdotool windowactivate --sync "$owner_id" >/dev/null 2>&1 || true; timeout --foreground --kill-after=1s "${FREEP_X11_POINTER_TIMEOUT_SECONDS:-3}" xdotool windowfocus "$owner_id" >/dev/null 2>&1 || true; sleep 0.12; }
active_owner_now() { [[ "$(xdotool getactivewindow 2>/dev/null || true)" == "$owner_id" && "$(xdotool getwindowfocus 2>/dev/null || true)" == "$owner_id" ]]; }
capture_window_state() { local name="$1"; { printf 'owner-window-id=%s\n' "${owner_id:-}"; printf 'owner-window-title=%s\n' "${owner_title:-}"; printf 'active-window=%s\n' "$(xdotool getactivewindow 2>/dev/null || true)"; printf 'focus-window=%s\n' "$(xdotool getwindowfocus 2>/dev/null || true)"; printf 'top-level-window-ids='; window_ids | tr '\n' ' '; printf '\n'; printf 'wmctrl-list-begin\n'; wmctrl -l 2>/dev/null || true; printf 'wmctrl-list-end\n'; } > "$output/$name"; }

finalize() {
    local exit_code=$?
    if [[ ! -s "$output/stage1-not-executed.txt" ]]; then printf 'Stage 1 does not execute physical clipboard, selection, cut, undo, redo, or paste actions.\n' > "$output/stage1-not-executed.txt"; fi
    python3 - "$records" "$screenshots_file" "$manifest" "${owner_id:-}" "${owner_title:-}" "$expected_document_name" "${exit_code:-0}" <<'PY'
import json, os, sys
records, screenshots, manifest, owner_id, owner_title, fixture, exit_code = sys.argv[1:]
by_id = {}
try:
    with open(records, encoding="utf-8") as h:
        for line in h:
            if line.strip(): by_id[json.loads(line)["id"]] = json.loads(line)
except FileNotFoundError: pass
stage = "stage1-not-executed.txt"
ids = ["visible-window-discovery", "clipboard-copy-x11-preserves-source", "clipboard-paste-native-editable-shape", "select-all-multi-shape-mutation", "cut-all-x11-undoable", "undo-restores-editable-shapes", "redo-reapplies-cut", "paste-after-cut-restores-editable-shapes"]
for result_id in ids:
    by_id.setdefault(result_id, {"id": result_id, "category": "physical-x11-clipboard-shortcut", "status": "failed", "evidenceLevel": "physical-x11-input", "evidence": [stage], "note": "Stage 1 did not execute this physical clipboard shortcut contract."})
results = [by_id[i] for i in ids]
with open(screenshots, encoding="utf-8") as h: shot_names = list(dict.fromkeys(x.strip() for x in h if x.strip()))
data = {"schemaVersion": 1, "suite": "freep-linux-clipboard-shortcut-physical", "platform": "linux", "shell": "avalonia", "app": "FreeP", "baseline": False, "appSurface": "document-editor-clipboard-shortcuts", "window": {"id": owner_id, "title": owner_title, "pattern": fixture, "visible": bool(owner_id)}, "contractValidation": {"status": "pending"}, "screenshots": [{"name": x, "kind": "screenshot"} for x in shot_names], "summary": {"passed": sum(x["status"] == "passed" for x in results), "failed": sum(x["status"] == "failed" for x in results), "total": len(results)}, "results": results, "processExitCode": int(exit_code)}
with open(manifest, "w", encoding="utf-8") as h: json.dump(data, h, ensure_ascii=False, indent=2); h.write("\n")
PY
    return "$exit_code"
}
trap finalize EXIT

if [[ -n "$document_path" && -f "$document_path" ]]; then
    sha256sum "$document_path" > "$output/mounted-document.sha256"
    inspect_pptx "$document_path" "$output/baseline-package-inspection.json"
    self_test_inspect_pptx "$output/baseline-package-inspection.json"
else
    printf 'FREEP_DOCUMENT_PATH was absent or not a file; mounted hash and baseline inspection were not available.\n' > "$output/precondition-error.txt"
fi

mapfile -t visible_owner_ids < <(xdotool search --onlyvisible --name "$window_pattern" 2>/dev/null || true)
if (( ${#visible_owner_ids[@]} )); then
    owner_id="${visible_owner_ids[${#visible_owner_ids[@]}-1]}"
    owner_title="$(xdotool getwindowname "$owner_id" 2>/dev/null || true)"
    capture "baseline.png"; capture_window_state "owner-discovery-state.txt"
    printf 'owner-window-id=%s\nowner-window-title=%s\nvisible-owner-count=%s\n' "$owner_id" "$owner_title" "${#visible_owner_ids[@]}" > "$output/visible-window-discovery-proof.txt"
    if active_owner_now && [[ "$owner_title" == *FreeP* || "$owner_title" == *Freep* ]]; then
        record visible-window-discovery passed "Visible FreeP owner discovered through X11." visible-window-discovery-proof.txt baseline.png owner-discovery-state.txt
    else
        record visible-window-discovery failed "A visible window was found, but it did not prove the focused FreeP owner." visible-window-discovery-proof.txt baseline.png owner-discovery-state.txt
    fi
else
    printf 'No visible FreeP window matched %s.\n' "$window_pattern" > "$output/window-discovery-error.txt"
    record visible-window-discovery failed "No visible FreeP owner matched the X11 precondition." window-discovery-error.txt
fi

for id in "${required_ids[@]:1}"; do
    record "$id" failed "Stage 1 does not execute physical clipboard, selection, cut, undo, redo, or paste actions." stage1-not-executed.txt
done
