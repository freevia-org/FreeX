#!/usr/bin/env bash
set -euo pipefail

output_directory="${1:-/work/accessibility-validation}"
mkdir -p "$output_directory"

window_id=""
for _ in $(seq 1 30); do
    window_id="$(DISPLAY=:99 xdotool search --onlyvisible --name 'FreeP' 2>/dev/null | tail -1 || true)"
    [[ -n "$window_id" ]] && break
    sleep 0.25
done

bus_socket=""
for candidate in /tmp/dbus-*; do
    if [[ -S "$candidate" ]]; then
        bus_socket="$candidate"
        break
    fi
done

export DISPLAY=:99
if [[ -n "$bus_socket" ]]; then
    export DBUS_SESSION_BUS_ADDRESS="unix:path=$bus_socket"
fi

python3 - "$output_directory" "$window_id" <<'PY'
import json
import os
import sys
import time

output_directory = sys.argv[1]
window_id = sys.argv[2]
target_contracts = {
    "slides": {"name": "slides", "roles": {"list", "list box", "listbox"}},
    "notes": {"name": "notes", "roles": {"entry"}},
    "comments": {"name": "comments", "roles": {"panel"}},
    "selection": {"name": "selection pane", "roles": {"panel"}},
    "animation": {"name": "animation pane", "roles": {"panel"}},
}
result = {
    "schemaVersion": 1,
    "suite": "freep-atspi-accessibility",
    "platform": "linux",
    "shell": "avalonia",
    "app": "FreeP",
    "evidenceLevel": "os-atspi-x11",
    "windowId": window_id,
    "status": "not-proven",
    "applications": [],
    "observations": [],
    "limitation": "",
}

try:
    import pyatspi
except Exception as exc:
    result["limitation"] = f"AT-SPI Python bindings were unavailable: {type(exc).__name__}: {exc}"
else:
    try:
        desktop = pyatspi.Registry.getDesktop(0)
        applications = []
        for index in range(desktop.childCount):
            try:
                application = desktop.getChildAtIndex(index)
                applications.append({
                    "name": application.name or "",
                    "role": application.getRoleName(),
                })
            except Exception:
                continue
        result["applications"] = applications

        def find_freep_window(node, depth=0):
            if depth > 32:
                return None
            try:
                if "freep" in (node.name or "").lower():
                    return node
                for child_index in range(node.childCount):
                    try:
                        match = find_freep_window(node.getChildAtIndex(child_index), depth + 1)
                        if match is not None:
                            return match
                    except Exception:
                        continue
            except Exception:
                return None
            return None

        freep_application = None
        freep_window = None
        for index in range(desktop.childCount):
            try:
                application = desktop.getChildAtIndex(index)
                match = find_freep_window(application)
                if match is not None:
                    freep_application = application
                    freep_window = match
                    break
            except Exception:
                continue

        if freep_application is None:
            result["limitation"] = (
                "The running FreeP X11 window was found, but no AT-SPI application tree "
                "contained a FreeP-titled window. The desktop exposed "
                f"{len(applications)} application accessible(s)."
            )
        else:
            result["applicationName"] = freep_application.name or ""
            result["windowName"] = freep_window.name or ""

            def read_value(node):
                try:
                    value = node.queryValue()
                    return str(value.currentValue)
                except Exception:
                    return ""

            def normalize_role(role):
                return " ".join((role or "").lower().replace("-", " ").split())

            def visit(node, depth=0):
                if depth > 32:
                    return
                try:
                    name = node.name or ""
                    role = node.getRoleName()
                    states = [str(state) for state in node.getState().getStates()]
                    lower_name = name.lower()
                    role_name = normalize_role(role)
                    for key, contract in target_contracts.items():
                        if (lower_name == contract["name"] and
                                role_name in contract["roles"] and
                                not any(item["target"] == key for item in result["observations"])):
                            result["observations"].append({
                                "target": key,
                                "name": name,
                                "role": role,
                                "state": states,
                                "value": read_value(node),
                            })
                    for child_index in range(node.childCount):
                        try:
                            visit(node.getChildAtIndex(child_index), depth + 1)
                        except Exception:
                            continue
                except Exception:
                    return

            visit(freep_application)
            expected = set(target_contracts)
            observed = {item["target"] for item in result["observations"]}
            if expected.issubset(observed):
                result["status"] = "passed"
                result["limitation"] = "AT-SPI exposed all representative live pane names, roles, states, and values."
            else:
                result["limitation"] = (
                    "AT-SPI exposed the FreeP application, but not all representative pane names; "
                    f"missing={','.join(sorted(expected - observed))}."
                )
    except Exception as exc:
        result["limitation"] = f"AT-SPI query failed: {type(exc).__name__}: {exc}"

result["timestampUtc"] = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
path = os.path.join(output_directory, "atspi-result.json")
temporary = path + ".tmp"
with open(temporary, "w", encoding="utf-8") as handle:
    json.dump(result, handle, indent=2)
    handle.write("\n")
os.replace(temporary, path)
PY
