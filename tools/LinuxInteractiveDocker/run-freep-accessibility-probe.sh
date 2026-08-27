#!/usr/bin/env bash
set -euo pipefail

output_directory="${1:-/work/accessibility-validation}"
window_id="${2:-}"
mkdir -p "$output_directory"

if [[ -z "$window_id" ]]; then
    for _ in $(seq 1 30); do
        window_id="$(DISPLAY=:99 xdotool search --onlyvisible --name 'FreeP' 2>/dev/null | tail -1 || true)"
        [[ -n "$window_id" ]] && break
        sleep 0.25
    done
fi

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
import subprocess
import sys
import threading
import time

output_directory = sys.argv[1]
window_id = sys.argv[2]
expected_order = ["slides", "notes", "comments", "selection", "animation"]
target_contracts = {
    "slides": {"name": "slides", "roles": {"list", "list box", "listbox"}, "order": 0},
    "notes": {"name": "notes", "roles": {"entry"}, "order": 1},
    "comments": {"name": "comments", "roles": {"panel"}, "order": 2},
    "selection": {"name": "selection pane", "roles": {"panel"}, "order": 9},
    "animation": {"name": "animation pane", "roles": {"panel"}, "order": 10},
}
focus_events = []
focus_order = []
target_nodes = {}
listener_started = False

result = {
    "schemaVersion": 2,
    "suite": "freep-atspi-accessibility",
    "platform": "linux",
    "shell": "avalonia",
    "app": "FreeP",
    "evidenceLevel": "os-atspi-x11-focus-events",
    "windowId": window_id,
    "status": "not-proven",
    "applications": [],
    "observations": [],
    "focusEvents": focus_events,
    "expectedFocusOrder": expected_order,
    "focusTraversal": focus_order,
    "keyboardTraversal": {
        "method": "xdotool X11 key Tab",
        "key": "Tab",
        "physical": True,
    },
    "limitation": "",
}


def write_json(name, value):
    path = os.path.join(output_directory, name)
    temporary = path + ".tmp"
    with open(temporary, "w", encoding="utf-8") as handle:
        json.dump(value, handle, indent=2)
        handle.write("\n")
    os.replace(temporary, path)


def normalize_role(role):
    return " ".join((role or "").lower().replace("-", " ").split())


def normalize_state(state):
    text = str(state)
    return text.rsplit(".", 1)[-1].replace("STATE_", "").lower()


def read_states(node):
    try:
        state_set = node.getState()
        known_states = [
            ("focusable", pyatspi.STATE_FOCUSABLE),
            ("visible", pyatspi.STATE_VISIBLE),
            ("showing", pyatspi.STATE_SHOWING),
            ("focused", pyatspi.STATE_FOCUSED),
            ("enabled", pyatspi.STATE_ENABLED),
        ]
        return [name for name, state in known_states if state_set.contains(state)]
    except Exception:
        return []


def node_state(node):
    states = read_states(node)
    return {
        "state": states,
        "focusable": "focusable" in states,
        "visible": "visible" in states,
        "showing": "showing" in states,
        "focused": "focused" in states,
    }


def read_value(node):
    try:
        value = node.queryValue()
        return str(value.currentValue)
    except Exception:
        return ""


def describe(node, target, include_count=False):
    state = node_state(node)
    item = {
        "target": target,
        "name": node.name or "",
        "role": node.getRoleName(),
        "state": state["state"],
        "focusable": state["focusable"],
        "visible": state["visible"],
        "showing": state["showing"],
        "focused": state["focused"],
        "value": read_value(node),
    }
    if include_count:
        item["focusEventCount"] = sum(1 for event in focus_events if event["target"] == target)
    return item


def find_freep_window(node, depth=0):
    if depth > 32:
        return None
    try:
        role = normalize_role(node.getRoleName())
        name = (node.name or "").lower()
        if role in {"window", "frame"} and ("freep" in name or name == "untitled * - freep"):
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


def collect_target_candidates(node, candidates, depth=0):
    if depth > 40:
        return
    try:
        lower_name = (node.name or "").lower()
        role_name = normalize_role(node.getRoleName())
        for key, contract in target_contracts.items():
            if lower_name == contract["name"] and role_name in contract["roles"]:
                candidates.setdefault(key, []).append(node)
        for child_index in range(node.childCount):
            try:
                collect_target_candidates(node.getChildAtIndex(child_index), candidates, depth + 1)
            except Exception:
                continue
    except Exception:
        return


def target_for_source(source):
    for key, node in target_nodes.items():
        try:
            if source == node:
                return key
        except Exception:
            continue
    # Matching is still exact name plus the contract role, and candidates were
    # required to be unique above, so a label cannot become a focus match.
    try:
        source_name = (source.name or "").lower()
        source_role = normalize_role(source.getRoleName())
        matches = [
            key for key, contract in target_contracts.items()
            if source_name == contract["name"] and source_role in contract["roles"]
        ]
        return matches[0] if len(matches) == 1 else None
    except Exception:
        return None


def on_focus(event):
    try:
        if not bool(event.detail1):
            return
        target = target_for_source(event.source)
        if target is None:
            return
        observation = describe(event.source, target)
        observation["timestampUtc"] = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
        focus_events.append(observation)
        if target not in focus_order:
            focus_order.append(target)
    except Exception:
        return


def drive_keyboard_traversal(pyatspi):
    time.sleep(0.5)
    if window_id:
        subprocess.run(
            ["xdotool", "windowactivate", "--sync", window_id],
            check=False,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
    started = time.monotonic()
    for _ in range(160):
        subprocess.run(
            ["xdotool", "key", "--clearmodifiers", "Tab"],
            check=False,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        time.sleep(0.08)
        if focus_order == expected_order and time.monotonic() - started > 1.0:
            break
    time.sleep(0.5)
    pyatspi.Registry.stop()


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
                "contained a uniquely identified FreeP window. The desktop exposed "
                f"{len(applications)} application accessible(s)."
            )
        else:
            result["applicationName"] = freep_application.name or ""
            result["windowName"] = freep_window.name or ""
            candidates = {}
            collect_target_candidates(freep_application, candidates)
            duplicate_targets = sorted(key for key in target_contracts if len(candidates.get(key, [])) != 1)
            if duplicate_targets:
                result["limitation"] = (
                    "AT-SPI did not expose exactly one uniquely role-qualified node for every target; "
                    f"invalid={','.join(duplicate_targets)}. Labels are excluded by role matching."
                )
            else:
                target_nodes = {key: candidates[key][0] for key in target_contracts}
                result["observations"] = [
                    describe(target_nodes[key], key, include_count=True)
                    for key in expected_order
                ]
                result["limitation"] = (
                    "Focus events and Tab traversal were observed through X11 AT-SPI. "
                    "Actual screen-reader speech or synthesized announcement text was not certified."
                )

                if window_id:
                    subprocess.run(
                        ["xdotool", "windowactivate", "--sync", window_id],
                        check=False,
                        stdout=subprocess.DEVNULL,
                        stderr=subprocess.DEVNULL,
                    )
                pyatspi.Registry.registerEventListener(on_focus, "object:state-changed:focused")
                listener_started = True
                write_json("atspi-ready.json", {
                    "schemaVersion": 1,
                    "suite": "freep-atspi-focus-ready",
                    "targets": expected_order,
                    "method": "object:state-changed:focused",
                })
                driver = threading.Thread(target=drive_keyboard_traversal, args=(pyatspi,), daemon=True)
                driver.start()
                pyatspi.Registry.start()
                driver.join(timeout=2.0)

                for observation in result["observations"]:
                    observation["focusEventCount"] = sum(
                        1 for event in focus_events if event["target"] == observation["target"]
                    )
                all_target_states = all(
                    observation["focusable"] and observation["visible"] and observation["showing"]
                    for observation in result["observations"]
                )
                all_targets_observed = all(
                    any(event["target"] == target for event in focus_events)
                    for target in expected_order
                )
                if all_target_states and all_targets_observed and focus_order == expected_order:
                    result["status"] = "passed"
                else:
                    result["limitation"] = (
                        "AT-SPI exposed the target nodes, but the physical focus trail did not prove "
                        "the complete shared order or target state contract; "
                        f"focusOrder={','.join(focus_order)}. "
                        "Actual screen-reader speech or synthesized announcement text was not certified."
                    )
    except Exception as exc:
        result["limitation"] = f"AT-SPI focus query failed: {type(exc).__name__}: {exc}"
    finally:
        if listener_started:
            try:
                pyatspi.Registry.unregisterEventListener(on_focus, "object:state-changed:focused")
            except Exception:
                pass

result["timestampUtc"] = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
write_json("atspi-result.json", result)
PY
